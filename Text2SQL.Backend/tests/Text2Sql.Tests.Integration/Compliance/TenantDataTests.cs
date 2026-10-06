using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;   // HttpMethodAttribute burada tanımlı
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Text2Sql.Api.Attributes;
using Text2Sql.Api.Auth;
using Text2Sql.Infrastructure.Persistence;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Compliance
{
    /// <summary>SaaS-8 — KVKK/GDPR: veri ihracı ve kiracı silme.</summary>
    public class TenantDataTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public TenantDataTests(TestApp factory) => _factory = factory;

        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

        [Fact]
        public async Task VeriIhraci_TumKiraciVerisiniDoner_HassasAlanlarHaric()
        {
            var client = _factory.CreateClient();
            var email = $"export-{Guid.NewGuid():N}@test.local";
            var token = await Tenants.RegisterAsync(client, "Export Co", email);
            client.UseBearer(token);

            var projectId = await Projects.CreateAsync(client, "İhraç Projesi");
            var (dbId, _) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());
            await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });

            var resp = await client.GetAsync("/api/company/export");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var data = (await resp.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");

            Assert.Equal("Export Co", data.GetProperty("organization").GetProperty("name").GetString());
            Assert.Equal("free", data.GetProperty("organization").GetProperty("planCode").GetString());
            Assert.Equal(1, data.GetProperty("members").GetArrayLength());
            Assert.Equal(1, data.GetProperty("projects").GetArrayLength());
            Assert.Equal(1, data.GetProperty("dataSources").GetArrayLength());
            Assert.Equal(1, data.GetProperty("queries").GetArrayLength());
            Assert.True(data.GetProperty("usage").GetProperty("totalTokens").GetInt64() > 0);

            // HASSAS ALANLAR ihraç edilmemeli
            var raw = data.ToString();
            Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("keyHash", raw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("passwordEncrypted", raw, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task VeriIhraci_BaskaKiracininVerisiniIcermez()
        {
            var clientA = _factory.CreateClient();
            var tokenA = await Tenants.RegisterAsync(
                clientA, "Export Tenant A", $"exa-{Guid.NewGuid():N}@test.local");
            clientA.UseBearer(tokenA);
            await Projects.CreateAsync(clientA, "A Gizli Proje");

            var clientB = _factory.CreateClient();
            var tokenB = await Tenants.RegisterAsync(
                clientB, "Export Tenant B", $"exb-{Guid.NewGuid():N}@test.local");
            clientB.UseBearer(tokenB);

            var resp = await clientB.GetAsync("/api/company/export");
            var raw = (await resp.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data").ToString();

            Assert.DoesNotContain("A Gizli Proje", raw);
            Assert.DoesNotContain("Export Tenant A", raw);
        }

        [Fact]
        public async Task KiraciSilme_YanlisOnayMetniyle_400Doner()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Delete Guard Co", $"dg-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);

            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
                "/api/company/organization")
            {
                Content = JsonContent.Create(new { confirmationText = "yanlis ad" })
            });

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            // Organizasyon hâlâ erişilebilir olmalı
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/company/users")).StatusCode);
        }

        [Fact]
        public async Task KiraciSilme_DogruOnayMetniyle_TumVeriyiSiler()
        {
            var client = _factory.CreateClient();
            var orgName = $"Delete Me {Guid.NewGuid():N}"[..20];
            var email = $"del-{Guid.NewGuid():N}@test.local";
            var token = await Tenants.RegisterAsync(client, orgName, email);
            client.UseBearer(token);

            var projectId = await Projects.CreateAsync(client, "Silinecek Proje");
            int companyId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                companyId = (await db.Projects.IgnoreQueryFilters()
                    .AsNoTracking().FirstAsync(p => p.Id == projectId)).CompanyId;
            }

            var resp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
                "/api/company/organization")
            {
                Content = JsonContent.Create(new { confirmationText = orgName })
            });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Organizasyon ve bağlı veriler cascade ile gitmiş olmalı
                Assert.False(await db.Companies.IgnoreQueryFilters().AnyAsync(c => c.Id == companyId));
                Assert.False(await db.Projects.IgnoreQueryFilters().AnyAsync(p => p.CompanyId == companyId));
                Assert.False(await db.Memberships.IgnoreQueryFilters().AnyAsync(m => m.CompanyId == companyId));
                Assert.False(await db.Subscriptions.IgnoreQueryFilters().AnyAsync(s => s.CompanyId == companyId));

                // Tek üyeliği bu organizasyonda olan hesap SİLİNİR
                // (amacı kalmayan yetim hesap tutulmaz — KVKK/GDPR ilkesi)
                Assert.False(await db.Users.AnyAsync(u => u.Email == email));

                // Silme olayı platform düzeyi denetim kaydında KALMALI
                Assert.True(await db.AuditLogs.IgnoreQueryFilters()
                    .AnyAsync(a => a.Action == "organization.deleted" && a.CompanyId == 0));
            }
        }

        [Fact]
        public async Task KiraciSilme_BaskaOrganizasyondaUyeligiOlanHesabiKORUR()
        {
            // SaaS-8 (r2) REGRESYON TESTİ: Bu senaryo bir tasarım kusurunu ortaya
            // çıkardı — legacy User.CompanyId FK'sı üzerinden cascade, başka
            // organizasyonlarda üyeliği olan kullanıcıları da siliyordu.
            // Bir müşterinin admin'i, ASLA başka müşterinin kullanıcısını silememeli.
            var paylasilanEmail = $"shared-{Guid.NewGuid():N}@test.local";

            // Kullanıcı kendi organizasyonunu kurar (burası SİLİNMEYECEK)
            var kalanClient = _factory.CreateClient();
            var kalanOrg = $"Keeper {Guid.NewGuid():N}"[..16];
            var kalanToken = await Tenants.RegisterAsync(
                kalanClient, kalanOrg, paylasilanEmail);
            kalanClient.UseBearer(kalanToken);

            // İkinci organizasyon aynı kişiyi davet eder (burası SİLİNECEK)
            var silinecekClient = _factory.CreateClient();
            var silinecekOrg = $"Doomed {Guid.NewGuid():N}"[..16];
            var silinecekToken = await Tenants.RegisterAsync(
                silinecekClient, silinecekOrg, $"doomed-{Guid.NewGuid():N}@test.local");
            silinecekClient.UseBearer(silinecekToken);

            await silinecekClient.PostAsJsonAsync("/api/company/invite-user",
                new { email = paylasilanEmail, role = "user" });
            var list = await silinecekClient.GetAsync("/api/company/invitations");
            var lb = await list.Content.ReadFromJsonAsync<JsonElement>(J);
            var invToken = lb.GetProperty("data")[0].GetProperty("invitationToken").GetString();

            var anon = _factory.CreateClient();
            await anon.PostAsJsonAsync("/api/auth/accept-invitation",
                new { invitationToken = invToken, username = "shareduser", password = "P@ssw0rd123" });

            // İkinci organizasyonu sil
            var resp = await silinecekClient.SendAsync(new HttpRequestMessage(HttpMethod.Delete,
                "/api/company/organization")
            {
                Content = JsonContent.Create(new { confirmationText = silinecekOrg })
            });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // KRİTİK: paylaşılan hesap KORUNMALI (diğer organizasyonda üyeliği var)
            var hesap = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == paylasilanEmail);
            Assert.NotNull(hesap);

            // Ve o organizasyondaki üyeliği hâlâ geçerli olmalı
            Assert.True(await db.Memberships.IgnoreQueryFilters()
                .AnyAsync(m => m.UserId == hesap!.Id && m.IsActive));

            // Kendi organizasyonuna erişimi bozulmamalı
            var login = await anon.PostAsJsonAsync("/api/auth/login",
                new { email = paylasilanEmail, password = "P@ssw0rd123" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }
    }
}
