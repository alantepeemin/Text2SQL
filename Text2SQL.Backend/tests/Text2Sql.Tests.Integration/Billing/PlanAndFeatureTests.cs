using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;

namespace Text2Sql.Tests.Integration.Billing
{
    /// <summary>
    /// SaaS-5 — Plan, abonelik ve özellik bayrakları.
    /// </summary>
    public class PlanAndFeatureTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public PlanAndFeatureTests(TestApp factory) => _factory = factory;

        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

        private async Task<HttpClient> OrganizasyonKurAsync(string ad)
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, ad, $"plan-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);
            return client;
        }

        [Fact]
        public async Task YeniOrganizasyon_FreePlanIleBaslar()
        {
            var client = await OrganizasyonKurAsync("Free Start Co");

            var resp = await client.GetAsync("/api/billing/subscription");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var data = (await resp.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");
            Assert.Equal("free", data.GetProperty("planCode").GetString());
            Assert.Equal("active", data.GetProperty("status").GetString());
            Assert.True(data.GetProperty("isServiceable").GetBoolean());

            // Free limitleri ve mevcut kullanım birlikte dönmeli
            var limits = data.GetProperty("limits");
            Assert.Equal(3, limits.GetProperty("maxProjects").GetInt32());
            Assert.Equal(0, limits.GetProperty("currentProjects").GetInt32());
        }

        [Fact]
        public async Task PlanKatalogu_UcPlanDoner_MevcutPlanIsaretli()
        {
            var client = await OrganizasyonKurAsync("Catalog Co");

            var resp = await client.GetAsync("/api/billing/plans");
            var plans = (await resp.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");

            Assert.Equal(3, plans.GetArrayLength());

            var kodlar = plans.EnumerateArray().Select(p => p.GetProperty("code").GetString()).ToList();
            Assert.Equal(new[] { "free", "pro", "enterprise" }, kodlar);

            var current = plans.EnumerateArray().Single(p => p.GetProperty("isCurrent").GetBoolean());
            Assert.Equal("free", current.GetProperty("code").GetString());

            // Enterprise sınırsız token (0) ve denetim kaydı özelliğine sahip
            var ent = plans.EnumerateArray().Single(p => p.GetProperty("code").GetString() == "enterprise");
            Assert.Equal(0, ent.GetProperty("monthlyTokenLimit").GetInt64());
            Assert.Contains("audit_logs",
                ent.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        }

        [Fact]
        public async Task FreePlan_ApiAnahtariUcu_402Doner()
        {
            var client = await OrganizasyonKurAsync("Free Gate Co");

            // İzin var (admin) ama PAKET kapsamıyor → 402 (403 değil!)
            var create = await client.PostAsJsonAsync("/api/api-keys",
                new { name = "deneme", scopes = new[] { "queries.execute" } });
            Assert.Equal(HttpStatusCode.PaymentRequired, create.StatusCode);

            var list = await client.GetAsync("/api/api-keys");
            Assert.Equal(HttpStatusCode.PaymentRequired, list.StatusCode);
        }

        [Fact]
        public async Task FreePlan_DenetimKaydi_402_KullanimRaporu_200()
        {
            var client = await OrganizasyonKurAsync("Free Mixed Co");

            // audit_logs yalnızca Enterprise → 402
            var audit = await client.GetAsync("/api/company/audit-logs");
            Assert.Equal(HttpStatusCode.PaymentRequired, audit.StatusCode);

            // usage_reports Free'de de var → 200
            var usage = await client.GetAsync("/api/company/usage");
            Assert.Equal(HttpStatusCode.OK, usage.StatusCode);
        }

        [Fact]
        public async Task PlanYukseltme_OzellikleriAnindaAcar()
        {
            var client = await OrganizasyonKurAsync("Upgrade Co");

            // Önce kapalı
            Assert.Equal(HttpStatusCode.PaymentRequired,
                (await client.GetAsync("/api/api-keys")).StatusCode);

            // API üzerinden plan değişikliği (billing.manage izni admin'de var)
            var change = await client.PostAsJsonAsync("/api/billing/subscription/change-plan",
                new { planCode = "pro" });
            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
            var data = (await change.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");
            Assert.Equal("pro", data.GetProperty("planCode").GetString());

            // Cache düşürüldüğü için özellik ANINDA açılmalı
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/api-keys")).StatusCode);

            // Ama Enterprise özelliği hâlâ kapalı
            Assert.Equal(HttpStatusCode.PaymentRequired,
                (await client.GetAsync("/api/company/audit-logs")).StatusCode);
        }

        [Fact]
        public async Task ProjeLimiti_Asildiginda_402Doner()
        {
            var client = await OrganizasyonKurAsync("Limit Co");

            // Free planda 3 proje sınırı
            for (var i = 1; i <= 3; i++)
            {
                var ok = await client.PostAsJsonAsync("/api/projects",
                    new { name = $"Proje {i}", description = "limit testi" });
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            }

            var fourth = await client.PostAsJsonAsync("/api/projects",
                new { name = "Proje 4", description = "limit aşımı" });
            Assert.Equal(HttpStatusCode.PaymentRequired, fourth.StatusCode);

            // Plan yükseltilince ANINDA izin verilmeli (limit cache'i düşer)
            await client.PostAsJsonAsync("/api/billing/subscription/change-plan",
                new { planCode = "pro" });

            var afterUpgrade = await client.PostAsJsonAsync("/api/projects",
                new { name = "Proje 4", description = "yükseltme sonrası" });
            Assert.Equal(HttpStatusCode.OK, afterUpgrade.StatusCode);
        }

        [Fact]
        public async Task PlanDusurme_MevcutKaynaklariSilmez()
        {
            var client = await OrganizasyonKurAsync("Downgrade Co");
            await client.PostAsJsonAsync("/api/billing/subscription/change-plan", new { planCode = "pro" });

            // Pro'da 4 proje oluştur (Free limiti 3)
            for (var i = 1; i <= 4; i++)
                Assert.Equal(HttpStatusCode.OK,
                    (await client.PostAsJsonAsync("/api/projects",
                        new { name = $"P{i}", description = "x" })).StatusCode);

            // Free'ye düş
            var down = await client.PostAsJsonAsync("/api/billing/subscription/change-plan",
                new { planCode = "free" });
            Assert.Equal(HttpStatusCode.OK, down.StatusCode);

            // BİLİNÇLİ DAVRANIŞ: mevcut projeler SİLİNMEZ (veri kaybı yok)...
            var list = await client.GetAsync("/api/projects");
            var projects = (await list.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");
            Assert.Equal(4, projects.GetArrayLength());

            // ...ama YENİ ekleme limitle engellenir
            var yeni = await client.PostAsJsonAsync("/api/projects",
                new { name = "P5", description = "x" });
            Assert.Equal(HttpStatusCode.PaymentRequired, yeni.StatusCode);
        }

        [Fact]
        public async Task GecersizPlanKodu_404Doner()
        {
            var client = await OrganizasyonKurAsync("BadPlan Co");

            var resp = await client.PostAsJsonAsync("/api/billing/subscription/change-plan",
                new { planCode = "ultra-premium" });
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task SqliteYukleme_FreePlandaCalisir_UzakKaynakEngellenir()
        {
            var client = await OrganizasyonKurAsync("DataSource Gate Co");
            var projectId = await Projects.CreateAsync(client, "Kaynak Projesi");

            // SQLite yükleme HER planda mevcut
            var (_, upload) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

            // Uzak PostgreSQL bağlama Pro+ → 402
            using var form = new MultipartFormDataContent
            {
                { new StringContent("Uzak DB"), "Name" },
                { new StringContent("Remote"),  "Mode" },
                { new StringContent("Postgres"), "DbType" },
                { new StringContent("localhost"), "Host" },
                { new StringContent("5432"), "Port" },
                { new StringContent("bir_db"), "Database" },
                { new StringContent("kullanici"), "Username" },
                { new StringContent("parola"), "Password" }
            };
            var remote = await client.PostAsync($"/api/projects/{projectId}/databases", form);
            Assert.Equal(HttpStatusCode.PaymentRequired, remote.StatusCode);
        }
    }
}
