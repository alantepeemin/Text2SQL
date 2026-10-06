using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.ApiKeys
{
    /// <summary>
    /// SaaS-6 — API anahtarı: üretim, kullanım, scope zorlaması, iptal, izolasyon.
    /// </summary>
    public class ApiKeyTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public ApiKeyTests(TestApp factory) => _factory = factory;

        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

        private async Task<(HttpClient admin, int projectId, int dbId)> OrtamKurAsync(string ad)
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, ad, $"apikey-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);

            // SaaS-5: API anahtarları Pro+ özelliği; denetim testi için Enterprise
            await Plans.UpgradeAsync(_factory, ad, "enterprise");

            var projectId = await Projects.CreateAsync(client, $"{ad} Projesi");
            var (dbId, upload) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

            return (client, projectId, dbId);
        }

        private static async Task<string> AnahtarUretAsync(
            HttpClient admin, params string[] scopes)
        {
            var resp = await admin.PostAsJsonAsync("/api/api-keys",
                new { name = "Entegrasyon anahtarı", scopes, expiresInDays = 30 });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<JsonElement>(J);
            var plain = body.GetProperty("data").GetProperty("plainKey").GetString()!;
            Assert.StartsWith("t2s_live_", plain);
            return plain;
        }

        [Fact]
        public async Task Anahtar_UretilirVeTamDegerSadeceBirKezDoner()
        {
            var (admin, _, _) = await OrtamKurAsync("KeyGen Co");
            var plain = await AnahtarUretAsync(admin, "queries.execute", "queries.read");

            // Listede tam anahtar ASLA görünmemeli — yalnızca önek
            var list = await admin.GetAsync("/api/api-keys");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var raw = await list.Content.ReadAsStringAsync();

            Assert.DoesNotContain(plain, raw);
            Assert.Contains(plain[..12], raw);          // önek görünür
            Assert.DoesNotContain("keyHash", raw);      // özet dışa açılmaz
        }

        [Fact]
        public async Task Anahtar_SorguCalistirabilir_HemHeaderHemBearerIle()
        {
            var (admin, projectId, dbId) = await OrtamKurAsync("KeyUse Co");
            var plain = await AnahtarUretAsync(admin, "queries.execute", "queries.read");

            // 1) X-Api-Key başlığı
            var c1 = _factory.CreateClient();
            c1.DefaultRequestHeaders.Add("X-Api-Key", plain);
            var r1 = await c1.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            var b1 = await r1.Content.ReadFromJsonAsync<JsonElement>(J);
            Assert.True(b1.GetProperty("success").GetBoolean());

            // 2) Authorization: Bearer t2s_...
            var c2 = _factory.CreateClient();
            c2.UseBearer(plain);
            var r2 = await c2.GetAsync($"/api/projects/{projectId}/queries/history");
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        }

        [Fact]
        public async Task Anahtar_ScopeDisiUcaErisemez()
        {
            var (admin, projectId, dbId) = await OrtamKurAsync("Scope Co");
            // Yalnızca okuma scope'u
            var plain = await AnahtarUretAsync(admin, "queries.read");

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", plain);

            // queries.execute scope'u YOK → 403
            var exec = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });
            Assert.Equal(HttpStatusCode.Forbidden, exec.StatusCode);

            // Okuma izni var → 200
            var history = await client.GetAsync($"/api/projects/{projectId}/queries/history");
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        }

        [Fact]
        public async Task Anahtar_KendiniYonetemez_AyricalikYukseltmeKoruması()
        {
            var (admin, _, _) = await OrtamKurAsync("SelfMgmt Co");

            // apikeys.* scope'u istemek reddedilmeli (sızan anahtar kalıcılaşamaz)
            var resp = await admin.PostAsJsonAsync("/api/api-keys",
                new { name = "kötü anahtar", scopes = new[] { "apikeys.manage" }, expiresInDays = 30 });
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            // Geçerli bir anahtar bile anahtar yönetimi uçlarını kullanamaz
            var plain = await AnahtarUretAsync(admin, "queries.execute");
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", plain);

            var list = await client.GetAsync("/api/api-keys");
            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        }

        [Fact]
        public async Task IptalEdilenAnahtar_Reddedilir()
        {
            var (admin, projectId, dbId) = await OrtamKurAsync("Revoke Co");
            var plain = await AnahtarUretAsync(admin, "queries.execute");

            var list = await admin.GetAsync("/api/api-keys");
            var lb = await list.Content.ReadFromJsonAsync<JsonElement>(J);
            var keyId = lb.GetProperty("data")[0].GetProperty("id").GetInt32();

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", plain);

            // İptal öncesi çalışıyor
            var once = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "soru" });
            Assert.Equal(HttpStatusCode.OK, once.StatusCode);

            var revoke = await admin.DeleteAsync($"/api/api-keys/{keyId}");
            Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

            // İptal sonrası 401 (kimlik doğrulama başarısız)
            var sonra = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "soru" });
            Assert.Equal(HttpStatusCode.Unauthorized, sonra.StatusCode);
        }

        [Fact]
        public async Task GecersizAnahtar_401Doner()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", "t2s_live_kesinlikleGecersizBirAnahtarDegeri");

            var resp = await client.GetAsync("/api/users/me/organizations");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Anahtar_BaskaKiracininVerisineErisemez()
        {
            var (adminA, projeA, _) = await OrtamKurAsync("KeyTenant A");
            var (adminB, _, _)      = await OrtamKurAsync("KeyTenant B");

            var plainB = await AnahtarUretAsync(adminB, "queries.execute", "queries.read", "projects.read");

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", plainB);

            // B'nin anahtarı A'nın projesini görememeli
            var resp = await client.GetAsync($"/api/projects/{projeA}");
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }

        [Fact]
        public async Task Anahtar_KullanimOlcumuneVeDenetimKaydinaYansir()
        {
            var (admin, projectId, dbId) = await OrtamKurAsync("KeyAudit Co");
            var plain = await AnahtarUretAsync(admin, "queries.execute");

            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", plain);
            var exec = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "soru" });
            Assert.Equal(HttpStatusCode.OK, exec.StatusCode);

            // Ölçüm: anahtar üzerinden gelen sorgu da token tüketimi olarak kaydedilir
            var usage = await admin.GetAsync("/api/company/usage");
            var ub = (await usage.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");
            Assert.True(ub.GetProperty("totalTokens").GetInt64() > 0);

            // Denetim: anahtar üretimi kaydedilmiş olmalı
            var audit = await admin.GetAsync("/api/company/audit-logs");
            var ab = await audit.Content.ReadFromJsonAsync<JsonElement>(J);
            var actions = ab.GetProperty("data").EnumerateArray()
                .Select(a => a.GetProperty("action").GetString()).ToList();
            Assert.Contains("apikey.created", actions);

            // Denetim kaydında tam anahtar ASLA bulunmamalı
            Assert.DoesNotContain(plain, ab.GetProperty("data").ToString());
        }
    }
}
