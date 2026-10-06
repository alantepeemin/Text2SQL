using System.Net.Http.Json;
using System.Text.Json;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Contracts
{
    /// <summary>
    /// API sürüm sözleşmeleri.
    ///
    /// v1 ve v2 aynı controller kodundan beslenir; bu testler iki sözleşmenin
    /// birbirine karışmadığını mühürler. Buradaki bir kırılma, ön yüzün
    /// sessizce bozulması demektir.
    /// </summary>
    public class ApiContractTests : IClassFixture<TestApp>
    {
        private readonly TestApp _app;
        public ApiContractTests(TestApp app) => _app = app;

        [Fact]
        public async Task V1_Zarfli_Yanit_Doner()
        {
            var kiraci = await _app.NewTenant("Sozlesme V1").BuildAsync();

            var yanit = await kiraci.Client.GetAsync("/api/v1/projects");
            var veri = await ApiAssert.EnvelopeAsync(yanit);

            Assert.Equal(JsonValueKind.Array, veri.ValueKind);
        }

        [Fact]
        public async Task V2_Ciplak_Yanit_Doner()
        {
            var kiraci = await _app.NewTenant("Sozlesme V2").BuildAsync();

            var yanit = await kiraci.Client.GetAsync("/api/v2/projects");
            var govde = await ApiAssert.BareAsync(yanit);

            Assert.Equal(JsonValueKind.Array, govde.ValueKind);
        }

        [Fact]
        public async Task EskiRoute_V1AliasIle_AyniSonucuVerir()
        {
            var kiraci = await _app.NewTenant("Alias Co").BuildAsync();

            var legacy = await kiraci.Client.GetAsync("/api/users/profile");
            var v1     = await kiraci.Client.GetAsync("/api/v1/users/profile");

            Assert.Equal(legacy.StatusCode, v1.StatusCode);
            Assert.Equal(await legacy.Content.ReadAsStringAsync(),
                         await v1.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task V1_ZatenCiplakOlanUclar_Sarmalanmaz()
        {
            // queries/execute ve queries/history v1'de de çıplak dönüyordu.
            // Zarf filtresi bunları sarmalarsa mevcut ön yüz sessizce kırılır.
            var kiraci = await _app.NewTenant("Ciplak V1").WithSqliteDataSource().BuildAsync();

            var exec = await kiraci.Client.PostAsJsonAsync(
                $"/api/v1/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = kiraci.DataSourceId, question = "şehirler" });
            exec.EnsureSuccessStatusCode();

            var sonuc = await exec.ReadJsonAsync();
            Assert.True(sonuc.TryGetProperty("sql", out _), "execute yanıtı çıplak kalmalı.");
            Assert.False(sonuc.TryGetProperty("data", out _), "execute yanıtı sarmalanmamalı.");

            var gecmis = await kiraci.Client.GetAsync(
                $"/api/v1/projects/{kiraci.ProjectId}/queries/history");
            gecmis.EnsureSuccessStatusCode();

            Assert.Equal(JsonValueKind.Array, (await gecmis.ReadJsonAsync()).ValueKind);
        }

        [Fact]
        public async Task V2_SayfalamaBilgisi_BasliklardaDoner()
        {
            var kiraci = await _app.NewTenant("Sayfalama Co").WithSqliteDataSource().BuildAsync();

            for (var i = 0; i < 3; i++)
                await kiraci.Client.PostAsJsonAsync(
                    $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                    new { databaseId = kiraci.DataSourceId, question = $"soru {i}" });

            var yanit = await kiraci.Client.GetAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/history?page=1&pageSize=2");

            ApiAssert.PaginationHeaders(yanit, beklenenSayfa: 1, beklenenBoyut: 2, beklenenToplam: 3);

            var liste = await ApiAssert.BareAsync(yanit);
            Assert.Equal(2, liste.GetArrayLength());

            // İkinci sayfa kalan tek kaydı döndürmeli — sayfalama gerçekten çalışıyor
            var ikinci = await kiraci.Client.GetAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/history?page=2&pageSize=2");
            ApiAssert.PaginationHeaders(ikinci, beklenenSayfa: 2, beklenenBoyut: 2, beklenenToplam: 3);
            Assert.Equal(1, (await ApiAssert.BareAsync(ikinci)).GetArrayLength());
        }

        [Theory]
        [InlineData("/api/projects")]
        [InlineData("/api/v1/projects")]
        public async Task V1_Uclari_DeprecationBasligiTasir(string yol)
        {
            // v1'i sessizce yaşatmak istemcilere geçiş sinyali vermez.
            var kiraci = await _app.NewTenant($"Deprecation {yol.Length}").BuildAsync();

            var yanit = await kiraci.Client.GetAsync(yol);

            ApiAssert.Deprecated(yanit);
            Assert.Contains("successor-version", yanit.Headers.GetValues("Link").First());
        }

        [Fact]
        public async Task V2_Uclari_DeprecationBasligiTasimaz()
        {
            var kiraci = await _app.NewTenant("Guncel Co").BuildAsync();

            ApiAssert.NotDeprecated(await kiraci.Client.GetAsync("/api/v2/projects"));
        }
    }
}
