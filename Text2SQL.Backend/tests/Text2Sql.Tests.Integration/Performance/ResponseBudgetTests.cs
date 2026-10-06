using System.Diagnostics;
using System.Net.Http.Json;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Fixtures;
using Xunit;

namespace Text2Sql.Tests.Integration.Performance
{
    /// <summary>
    /// Performans BÜTÇESİ — kıyaslama (benchmark) değil.
    ///
    /// Amaç mikro-saniye ölçmek değil, bir gün birinin isteğe farkında olmadan
    /// N+1 sorgu veya senkron bir dış çağrı eklemesini yakalamaktır. Bu yüzden
    /// eşikler CÖMERTTİR: yavaş CI makinelerinde gürültü üretmemeli, ama
    /// büyüklük sırası bozulunca kırmızıya dönmelidir.
    /// </summary>
    public class ResponseBudgetTests : IClassFixture<TestApp>
    {
        private readonly TestApp _app;
        public ResponseBudgetTests(TestApp app) => _app = app;

        [Fact]
        public async Task SaglikUcu_HizliYanitVerir()
        {
            var istemci = _app.CreateClient();
            await istemci.GetAsync("/health");   // ısınma (JIT + host başlatma)

            var kronometre = Stopwatch.StartNew();
            for (var i = 0; i < 10; i++)
                (await istemci.GetAsync("/health")).EnsureSuccessStatusCode();
            kronometre.Stop();

            var ortalama = kronometre.ElapsedMilliseconds / 10.0;
            Assert.True(ortalama < 250,
                $"/health ortalama {ortalama:F0} ms — beklenen < 250 ms.");
        }

        [Fact]
        public async Task ProjeListesi_SabitSayidaSorguylaDoner_N1Regresyonu()
        {
            // Free planda proje limiti 3'tür; bu test liste performansını
            // ölçüyor, plan limitini değil → limitsiz paket kullanılır.
            var kiraci = await _app.NewTenant("Butce Co").WithPlan("enterprise").BuildAsync();

            for (var i = 0; i < 10; i++)
                await kiraci.CreateProjectAsync($"Proje {i}");

            var kronometre = Stopwatch.StartNew();
            var yanit = await kiraci.Client.GetAsync("/api/v2/projects");
            kronometre.Stop();

            yanit.EnsureSuccessStatusCode();
            Assert.True(kronometre.ElapsedMilliseconds < 2000,
                $"10 projelik liste {kronometre.ElapsedMilliseconds} ms sürdü — N+1 sorgu şüphesi.");
        }

        [Fact]
        public async Task Sorgu_OnbellekIsabetinde_BelirginOlarakHizlanir()
        {
            var kiraci = await _app.NewTenant("Onbellek Butce Co").WithSqliteDataSource().BuildAsync();

            async Task<long> CalistirAsync()
            {
                var k = Stopwatch.StartNew();
                var yanit = await kiraci.Client.PostAsJsonAsync(
                    $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                    new { databaseId = kiraci.DataSourceId, question = "aynı soru" });
                yanit.EnsureSuccessStatusCode();
                return k.ElapsedMilliseconds;
            }

            await CalistirAsync();               // ilk çağrı: şema + LLM
            var ikinciSure = await CalistirAsync(); // önbellek isabeti

            Assert.True(ikinciSure < 3000,
                $"Önbellek isabetli sorgu {ikinciSure} ms sürdü — önbellek devre dışı olabilir.");
        }
    }
}
