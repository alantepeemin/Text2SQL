using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Text2Sql.Infrastructure.Persistence;
using Xunit;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Integration.Compliance
{
    /// <summary>
    /// SaaS-3 — Denetim kaydı ve kullanım ölçümü.
    /// </summary>
    public class AuditAndUsageTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public AuditAndUsageTests(TestApp factory) => _factory = factory;

        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

        [Fact]
        public async Task Kayit_VeGiris_DenetimKaydinaYazilir()
        {
            var client = _factory.CreateClient();
            var email = $"audit-{Guid.NewGuid():N}@test.local";
            var token = await Tenants.RegisterAsync(client, "Audit Co", email);
            client.UseBearer(token);

            // SaaS-5: denetim kaydı Enterprise özelliği
            await Plans.UpgradeAsync(_factory, "Audit Co", "enterprise");

            var resp = await client.GetAsync("/api/company/audit-logs");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(J);
            var actions = body.GetProperty("data").EnumerateArray()
                .Select(a => a.GetProperty("action").GetString()).ToList();

            Assert.Contains("organization.created", actions);
        }

        [Fact]
        public async Task BasarisizGiris_DenetimKaydinaYazilir_ParolaSizdirmadan()
        {
            var client = _factory.CreateClient();
            var email = $"failaudit-{Guid.NewGuid():N}@test.local";
            var token = await Tenants.RegisterAsync(client, "FailAudit Co", email);
            await Plans.UpgradeAsync(_factory, "FailAudit Co", "enterprise");

            var bad = await client.PostAsJsonAsync("/api/auth/login",
                new { email, password = "kesinlikle-yanlis-parola" });
            Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);

            client.UseBearer(token);
            var resp = await client.GetAsync("/api/company/audit-logs");
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(J);

            var failed = body.GetProperty("data").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("action").GetString() == "auth.login.failed");

            Assert.True(failed.ValueKind != JsonValueKind.Undefined,
                "Başarısız giriş denetim kaydına yazılmadı.");

            // Denetim kaydı ASLA parola içermemeli
            var raw = failed.ToString();
            Assert.DoesNotContain("kesinlikle-yanlis-parola", raw);
        }

        [Fact]
        public async Task VeriKaynagiEkleme_DenetimKaydinaYazilir()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "DS Audit Co", $"ds-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);
            await Plans.UpgradeAsync(_factory, "DS Audit Co", "enterprise");

            var projectId = await Projects.CreateAsync(client, "Audit Projesi");
            var (dbId, upload) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

            var resp = await client.GetAsync("/api/company/audit-logs");
            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(J);
            var kayit = body.GetProperty("data").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("action").GetString() == "datasource.created");

            Assert.True(kayit.ValueKind != JsonValueKind.Undefined);
            Assert.Equal(dbId.ToString(), kayit.GetProperty("targetId").GetString());
        }

        [Fact]
        public async Task Sorgu_TokenKullanimiOlculur_VeMaliyetHesaplanir()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Usage Co", $"usage-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);

            var projectId = await Projects.CreateAsync(client, "Ölçüm Projesi");
            var (dbId, _) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());

            var exec = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });
            Assert.Equal(HttpStatusCode.OK, exec.StatusCode);

            var resp = await client.GetAsync("/api/company/usage");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var usage = (await resp.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");

            Assert.Equal(1, usage.GetProperty("queryCount").GetInt32());
            Assert.Equal(1, usage.GetProperty("successfulQueryCount").GetInt32());
            Assert.Equal(FakeSqlGenerator.FakePromptTokens,
                         usage.GetProperty("promptTokens").GetInt64());
            Assert.Equal(FakeSqlGenerator.FakeCompletionTokens,
                         usage.GetProperty("completionTokens").GetInt64());

            // Beklenen maliyet: 1200/1M × $1.00 + 60/1M × $2.00 = 0.00132
            var maliyet = usage.GetProperty("estimatedCostUsd").GetDecimal();
            Assert.InRange(maliyet, 0.0013m, 0.0014m);

            // Model dağılımı
            var byModel = usage.GetProperty("byModel");
            Assert.Equal(1, byModel.GetArrayLength());
            Assert.Equal(FakeSqlGenerator.FakeModel,
                         byModel[0].GetProperty("model").GetString());
        }

        [Fact]
        public async Task OrganizasyonTokenKotasi_Asildiginda_429Doner()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Quota Org", $"quotaorg-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);

            var projectId = await Projects.CreateAsync(client, "Kota Projesi");
            var (dbId, _) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());

            // SaaS-5: Token limiti artık PLANDAN geliyor. Testin hızlı kalması için
            // bu organizasyona özel, çok düşük limitli bir plan atıyoruz.
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var company = await db.Companies
                    .OrderByDescending(c => c.Id)
                    .FirstAsync(c => c.Name == "Quota Org");

                var kucukPlan = new Text2Sql.Domain.Entities.Plan
                {
                    Code = $"test-tiny-{Guid.NewGuid():N}",
                    Name = "Test Tiny",
                    MonthlyTokenLimit = 3000,   // ~2 sorgu (her sorgu 1260 token)
                    MaxMembers = 0, MaxProjects = 0, MaxDataSources = 0, MaxApiKeys = 0,
                    IsActive = true, SortOrder = 99
                };
                db.Plans.Add(kucukPlan);
                await db.SaveChangesAsync();

                var subscription = await db.Subscriptions
                    .IgnoreQueryFilters()
                    .FirstAsync(sub => sub.CompanyId == company.Id);
                subscription.PlanId = kucukPlan.Id;
                await db.SaveChangesAsync();

                var cache = scope.ServiceProvider
                    .GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
                cache.Remove(Text2Sql.Application.Common.CacheKeys.Plan(company.Id));
            }

            // Aritmetik: limit = 1 × 3000 = 3000 token, her sorgu 1260 token tüketir.
            // Kota kontrolü sorgudan ÖNCE yapılır → görülen tüketim 0, 1260, 2520
            // olduğu için ilk üç sorgu geçer; 4. sorgu (3780 ≥ 3000) reddedilir.
            // Sahte token değerlerine bağımlı kalmamak için ilk 429'u döngüyle arıyoruz.
            HttpStatusCode? sonKod = null;
            var basariliSayisi = 0;

            for (var i = 1; i <= 6; i++)
            {
                var resp = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                    new { databaseId = dbId, question = $"soru {i}" });
                sonKod = resp.StatusCode;

                if (resp.StatusCode == HttpStatusCode.TooManyRequests) break;

                Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
                basariliSayisi++;
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, sonKod);

            // Ters yönde sigorta: kota gereğinden erken devreye girmemeli
            Assert.True(basariliSayisi >= 2,
                $"Organizasyon kotası çok erken devreye girdi (yalnızca {basariliSayisi} sorgu geçti).");
        }

        [Fact]
        public async Task DenetimKaydi_KiracilarArasindaSizmaz()
        {
            var clientA = _factory.CreateClient();
            var tokenA = await Tenants.RegisterAsync(
                clientA, "Audit Tenant A", $"ata-{Guid.NewGuid():N}@test.local");
            clientA.UseBearer(tokenA);

            var clientB = _factory.CreateClient();
            var tokenB = await Tenants.RegisterAsync(
                clientB, "Audit Tenant B", $"atb-{Guid.NewGuid():N}@test.local");
            clientB.UseBearer(tokenB);
            await Plans.UpgradeAsync(_factory, "Audit Tenant B", "enterprise");

            var respB = await clientB.GetAsync("/api/company/audit-logs");
            var bodyB = await respB.Content.ReadFromJsonAsync<JsonElement>(J);

            // B, yalnızca kendi olaylarını görmeli — A'nın organizasyon adı geçmemeli
            var raw = bodyB.GetProperty("data").ToString();
            Assert.DoesNotContain("Audit Tenant A", raw);
        }
    }
}
