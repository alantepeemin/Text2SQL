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

namespace Text2Sql.Tests.Integration.Scenarios
{
    /// <summary>
    /// FAZ 0 — Karakterizasyon testleri.
    ///
    /// Amaç: Mevcut davranışı mühürlemek. Bu testler "doğru" davranışı değil,
    /// SİSTEMİN BUGÜNKÜ davranışını doğrular; sonraki fazlardaki refactor'lar
    /// bu paket yeşil kaldığı sürece davranışı korumuş sayılır.
    ///
    /// Bilinçli karakterizasyon notları:
    /// - Faz 2 güncellemesi: yanlış şifre artık doğru şekilde 401 döner
    ///   (UnauthorizedException), kota dolumu 429 döner (QuotaExceededException).
    /// - Başarısız sorgu yürütme hâlâ 200 OK + success=false döner. v2'de düzelecek.
    /// - Tenant izolasyonu testi Faz 0 güvenlik yaması SONRASI davranışı
    ///   (404) doğrular — yama öncesi bu bir veri sızıntısıydı.
    /// </summary>
    public class CharacterizationTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;

        public CharacterizationTests(TestApp factory) => _factory = factory;

        // ── Kimlik doğrulama ─────────────────────────────────────────────────

        [Fact]
        public async Task Kayit_Login_ProfilErisimi_Calisir()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Acme A.S.", "owner@acme-test-1.com");

            client.UseBearer(token);
            var profile = await client.GetAsync("/api/users/profile");

            Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
            var body = await profile.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
            Assert.Equal("owner@acme-test-1.com",
                body.GetProperty("data").GetProperty("email").GetString());
        }

        [Fact]
        public async Task Login_YanlisSifre_401Doner()
        {
            var client = _factory.CreateClient();
            await Tenants.RegisterAsync(
                client, "WrongPw Co", "owner@wrongpw-test.com");

            var resp = await client.PostAsJsonAsync("/api/auth/login", new
            {
                email = "owner@wrongpw-test.com",
                password = "yanlis-sifre-123"
            });

            // Faz 2: UnauthorizedException → 401 (eski davranış 403'tü, bilinçli düzeltildi)
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task GecersizToken_401Doner()
        {
            var client = _factory.CreateClient();
            client.UseBearer("gecersiz.jwt.token");

            var resp = await client.GetAsync("/api/users/profile");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        // ── Tenant izolasyonu (Faz 0 güvenlik yaması) ───────────────────────

        [Fact]
        public async Task TenantIzolasyonu_BaskaSirketinProjesine_404()
        {
            // Şirket A: proje oluşturur
            var clientA = _factory.CreateClient();
            var tokenA = await Tenants.RegisterAsync(
                clientA, "Tenant A", "admin@tenant-a-test.com");
            clientA.UseBearer(tokenA);
            var projectId = await Projects.CreateAsync(clientA, "A'nin Projesi");

            // Şirket B'nin ADMIN'i A'nın projesine erişmeye çalışır
            var clientB = _factory.CreateClient();
            var tokenB = await Tenants.RegisterAsync(
                clientB, "Tenant B", "admin@tenant-b-test.com");
            clientB.UseBearer(tokenB);

            // Yama öncesi: admin bypass nedeniyle 200 + veri sızıntısı.
            // Yama sonrası: 404 (varlık bilgisi dahi sızdırılmaz).
            var details = await clientB.GetAsync($"/api/projects/{projectId}");
            Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);

            var databases = await clientB.GetAsync($"/api/projects/{projectId}/databases");
            Assert.Equal(HttpStatusCode.NotFound, databases.StatusCode);

            var update = await clientB.PutAsJsonAsync($"/api/projects/{projectId}",
                new { name = "ele-gecirildi", description = "x" });
            Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);

            // Sahibi hâlâ erişebiliyor
            var owner = await clientA.GetAsync($"/api/projects/{projectId}");
            Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        }

        // ── Sorgu akışı (uçtan uca) ─────────────────────────────────────────

        [Fact]
        public async Task SorguAkisi_SqliteYukle_Calistir_GecmisiGor()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Query Co", "admin@query-test.com");
            client.UseBearer(token);

            var projectId = await Projects.CreateAsync(client, "Sorgu Projesi");
            var (dbId, upload) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
            Assert.True(dbId > 0);

            // Sorgu çalıştır
            var exec = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "En kalabalik sehir hangisi?" });
            Assert.Equal(HttpStatusCode.OK, exec.StatusCode);

            var result = await exec.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
            Assert.True(result.GetProperty("success").GetBoolean());
            Assert.Equal(FakeSqlGenerator.GeneratedSql, result.GetProperty("sql").GetString());
            Assert.Equal(3, result.GetProperty("rows").GetArrayLength());
            Assert.Equal("Istanbul",
                result.GetProperty("rows")[0][0].GetString()); // population DESC

            // Geçmiş — karakterizasyon: sarmalayıcısız çıplak liste döner
            var history = await client.GetAsync($"/api/projects/{projectId}/queries/history");
            Assert.Equal(HttpStatusCode.OK, history.StatusCode);
            var items = await history.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
            Assert.Equal(1, items.GetArrayLength());
            Assert.True(items[0].GetProperty("success").GetBoolean());
        }

        [Fact]
        public async Task SahteDosya_MagicByteKontrolu_400()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Magic Co", "admin@magic-test.com");
            client.UseBearer(token);

            var projectId = await Projects.CreateAsync(client, "Upload Projesi");

            // .db uzantılı ama SQLite olmayan içerik (Faz 0 yaması bunu reddeder)
            var fakeBytes = "Bu bir SQLite dosyasi degil, duz metin."u8.ToArray();
            var (_, resp) = await DataSources.UploadSqliteAsync(client, projectId, fakeBytes, "fake.db");

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        // ── Kota (Faz 0: atomik rezervasyon) ────────────────────────────────

        [Fact]
        public async Task Kota_SonToken_TuketilirVeIkinciIstek_400()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Quota Co", "admin@quota-test.com");
            client.UseBearer(token);

            var projectId = await Projects.CreateAsync(client, "Kota Projesi");
            var (dbId, _) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());

            // Kalan token'ı 1'e indir
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var quota = await db.UserTokens
                    .Include(t => t.User)
                    .SingleAsync(t => t.User.Email == "admin@quota-test.com");
                quota.RemainingTokens = 1;
                await db.SaveChangesAsync();
            }

            // 1. istek: son token'ı tüketir
            var first = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "soru 1" });
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            var r1 = await first.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
            Assert.True(r1.GetProperty("success").GetBoolean());

            // DB'de kalan 0 olmalı (tam olarak 1 tüketildi)
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var quota = await db.UserTokens
                    .Include(t => t.User).AsNoTracking()
                    .SingleAsync(t => t.User.Email == "admin@quota-test.com");
                Assert.Equal(0, quota.RemainingTokens);
            }

            // 2. istek: limit dolu → 429 (Faz 2: QuotaExceededException; eski davranış 400'dü)
            var second = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "soru 2" });
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        }
    }
}

namespace Text2Sql.Tests.Integration.Scenarios
{
    /// <summary>Faz 2: /api/v1 alias'ının eski route'larla aynı davrandığını doğrular.</summary>
    public class ApiV1AliasTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public ApiV1AliasTests(TestApp factory) => _factory = factory;

        [Fact]
        public async Task V1Alias_EskiRouteIleAyniCalisir()
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, "Alias Co", "admin@alias-test.com");
            client.UseBearer(token);

            var legacy = await client.GetAsync("/api/users/profile");
            var v1     = await client.GetAsync("/api/v1/users/profile");

            Assert.Equal(System.Net.HttpStatusCode.OK, legacy.StatusCode);
            Assert.Equal(System.Net.HttpStatusCode.OK, v1.StatusCode);
        }
    }
}
