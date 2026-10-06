using System.Net;
using System.Net.Http.Json;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Validation
{
    /// <summary>
    /// Girdi doğrulama. Doğrulama kuralları sessizce gevşerse, kötü veri
    /// servis katmanına ulaşır ve hata mesajları anlamsızlaşır.
    /// </summary>
    public class RequestValidationTests : IClassFixture<TestApp>
    {
        private readonly TestApp _app;
        public RequestValidationTests(TestApp app) => _app = app;

        [Theory]
        [InlineData("", "Organizasyon adı boş olamaz")]
        [InlineData("A", "Tek karakterlik ad kabul edilmemeli")]
        public async Task GecersizOrganizasyonAdi_400Doner(string ad, string neden)
        {
            var yanit = await _app.CreateClient().PostAsJsonAsync("/api/v2/auth/create-company",
                new { name = ad, maxUsers = 10, monthlyQueryLimit = 100 });

            Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
            Assert.True(yanit.StatusCode == HttpStatusCode.BadRequest, neden);
        }

        [Fact]
        public async Task KisaKullaniciAdi_400Doner()
        {
            var istemci = _app.CreateClient();

            var adim1 = await istemci.PostAsJsonAsync("/api/v2/auth/create-company",
                new { name = "Dogrulama Co", maxUsers = 10, monthlyQueryLimit = 100 });
            adim1.EnsureSuccessStatusCode();
            var registrationToken = (await adim1.ReadJsonAsync())
                .GetProperty("registrationToken").GetString();

            var adim2 = await istemci.PostAsJsonAsync("/api/v2/auth/complete-registration", new
            {
                registrationToken,
                username = "ab",                      // MinLength(3) altında
                email = Text2Sql.Tests.Common.Data.SampleData.UniqueEmail(),
                password = Tenants.DefaultPassword
            });

            Assert.Equal(HttpStatusCode.BadRequest, adim2.StatusCode);
        }

        [Fact]
        public async Task GecersizEposta_400Doner()
        {
            var istemci = _app.CreateClient();

            var adim1 = await istemci.PostAsJsonAsync("/api/v2/auth/create-company",
                new { name = "Eposta Co", maxUsers = 10, monthlyQueryLimit = 100 });
            var registrationToken = (await adim1.ReadJsonAsync())
                .GetProperty("registrationToken").GetString();

            var adim2 = await istemci.PostAsJsonAsync("/api/v2/auth/complete-registration", new
            {
                registrationToken,
                username = "gecerlikullanici",
                email = "bu-bir-eposta-degil",
                password = Tenants.DefaultPassword
            });

            Assert.Equal(HttpStatusCode.BadRequest, adim2.StatusCode);
        }

        [Fact]
        public async Task ZayifParola_400Doner()
        {
            var istemci = _app.CreateClient();

            var adim1 = await istemci.PostAsJsonAsync("/api/v2/auth/create-company",
                new { name = "Parola Co", maxUsers = 10, monthlyQueryLimit = 100 });
            var registrationToken = (await adim1.ReadJsonAsync())
                .GetProperty("registrationToken").GetString();

            var adim2 = await istemci.PostAsJsonAsync("/api/v2/auth/complete-registration", new
            {
                registrationToken,
                username = "gecerlikullanici2",
                email = Text2Sql.Tests.Common.Data.SampleData.UniqueEmail(),
                password = "123"
            });

            Assert.Equal(HttpStatusCode.BadRequest, adim2.StatusCode);
        }

        [Fact]
        public async Task BosSoruMetni_400Doner()
        {
            var kiraci = await _app.NewTenant("Bos Soru Co").WithSqliteDataSource().BuildAsync();

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = kiraci.DataSourceId, question = "   " });

            Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        }

        [Fact]
        public async Task VeriKaynagiSecilmeden_400Doner()
        {
            var kiraci = await _app.NewTenant("Kaynak Yok Co").WithProject().BuildAsync();

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = 0, question = "kaç şehir var" });

            Assert.Equal(HttpStatusCode.BadRequest, yanit.StatusCode);
        }
    }
}
