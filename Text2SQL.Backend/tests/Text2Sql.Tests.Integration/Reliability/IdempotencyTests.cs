using Microsoft.EntityFrameworkCore;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Reliability
{
    /// <summary>
    /// Idempotency-Key davranışı.
    ///
    /// Somut sorun: kullanıcı "Çalıştır" düğmesine iki kez basınca iki LLM
    /// çağrısı yapılıyor ve kota iki kez tüketiliyordu.
    /// </summary>
    public class IdempotencyTests
    {
        private static Task<HttpResponseMessage> CalistirAsync(
            TestTenant kiraci, string idempotencyKey)
            => kiraci.Client.PostJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = kiraci.DataSourceId, question = "şehirler" },
                new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey });

        private static Task<int> SorguSayisiAsync(TestApp app)
            => app.QueryDatabaseAsync(db => db.QueryHistories.IgnoreQueryFilters().CountAsync());

        [Fact]
        public async Task AyniAnahtar_SorguyuTekrarCalistirmaz_IlkYanitiTekrarlar()
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Idempotent Co").WithSqliteDataSource().BuildAsync();

            var anahtar = Guid.NewGuid().ToString("N");

            var ilk = await CalistirAsync(kiraci, anahtar);
            ilk.EnsureSuccessStatusCode();

            var ikinci = await CalistirAsync(kiraci, anahtar);
            ikinci.EnsureSuccessStatusCode();

            Assert.True(ikinci.Headers.Contains("Idempotency-Replayed"),
                "Tekrarlanan istek Idempotency-Replayed başlığı taşımalı.");

            // Statik sayaç yerine DB üzerinden doğrulama: paralel koşuda güvenilir
            Assert.Equal(1, await SorguSayisiAsync(app));

            Assert.Equal(await ilk.Content.ReadAsStringAsync(),
                         await ikinci.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task FarkliAnahtar_YeniCalistirmaYapar()
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Idempotent Farkli").WithSqliteDataSource().BuildAsync();

            (await CalistirAsync(kiraci, Guid.NewGuid().ToString("N"))).EnsureSuccessStatusCode();
            var ikinci = await CalistirAsync(kiraci, Guid.NewGuid().ToString("N"));
            ikinci.EnsureSuccessStatusCode();

            Assert.False(ikinci.Headers.Contains("Idempotency-Replayed"));
            Assert.Equal(2, await SorguSayisiAsync(app));
        }

        [Fact]
        public async Task AnahtarYoksa_HerIstekYenidenCalisir()
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Idempotent Yok").WithSqliteDataSource().BuildAsync();

            for (var i = 0; i < 2; i++)
            {
                var yanit = await kiraci.Client.PostJsonAsync(
                    $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                    new { databaseId = kiraci.DataSourceId, question = "şehirler" });
                yanit.EnsureSuccessStatusCode();
                Assert.False(yanit.Headers.Contains("Idempotency-Replayed"));
            }

            Assert.Equal(2, await SorguSayisiAsync(app));
        }
    }
}
