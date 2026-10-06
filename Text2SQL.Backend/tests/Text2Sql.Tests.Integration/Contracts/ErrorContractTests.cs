using System.Net;
using System.Net.Http.Json;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Fixtures;
using Xunit;

namespace Text2Sql.Tests.Integration.Contracts
{
    /// <summary>
    /// Hata sözleşmesi. İstemci hata METNİNE değil KODUNA bakabilmelidir;
    /// aksi halde her dil/metin değişikliği istemciyi kırar.
    /// </summary>
    public class ErrorContractTests : IClassFixture<TestApp>
    {
        private readonly TestApp _app;
        public ErrorContractTests(TestApp app) => _app = app;

        [Fact]
        public async Task V2_Hata_ProblemDetails_ve_MakineOkurKod()
        {
            var kiraci = await _app.NewTenant("Problem V2").WithProject().BuildAsync();

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = 999999, question = "kaç şehir var" });

            await ApiAssert.ProblemAsync(yanit, HttpStatusCode.NotFound, "NOT_FOUND");
        }

        [Fact]
        public async Task V1_Hata_EskiZarfFormatiniKorur()
        {
            var kiraci = await _app.NewTenant("Problem V1").WithProject().BuildAsync();

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v1/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = 999999, question = "kaç şehir var" });

            await ApiAssert.LegacyErrorAsync(yanit, HttpStatusCode.NotFound);
        }

        [Fact]
        public async Task V2_KimlikDogrulanmamisIstek_401_ProblemDetails()
        {
            var istemci = _app.CreateClient();

            var yanit = await istemci.GetAsync("/api/v2/projects");

            Assert.Equal(HttpStatusCode.Unauthorized, yanit.StatusCode);
        }

        [Fact]
        public async Task V2_PaketDisiOzellik_402_FEATURE_NOT_AVAILABLE()
        {
            // Free planda API anahtarı yok → 403 değil 402: "yetkin yok" ile
            // "paketin kapsamıyor" farklı sorunlardır ve farklı çözümleri vardır.
            var kiraci = await _app.NewTenant("Ucretsiz Co").BuildAsync();

            var yanit = await kiraci.Client.PostAsJsonAsync("/api/v2/api-keys",
                new { name = "test", scopes = new[] { "queries.execute" } });

            await ApiAssert.ProblemAsync(yanit, HttpStatusCode.PaymentRequired, "FEATURE_NOT_AVAILABLE");
        }
    }
}
