using System.Net;
using Text2Sql.Tests.Common.Fixtures;
using Xunit;

namespace Text2Sql.Tests.Integration.Reliability
{
    /// <summary>
    /// Sağlık uçları. Yük dengeleyici ve Docker healthcheck bunlara bağlıdır:
    /// biri sessizce kimlik doğrulama arkasına düşerse dağıtım "hasta" görünür
    /// ve trafik kesilir.
    /// </summary>
    public class HealthEndpointTests : IClassFixture<TestApp>
    {
        private readonly TestApp _app;
        public HealthEndpointTests(TestApp app) => _app = app;

        [Theory]
        [InlineData("/health")]
        [InlineData("/health/live")]
        [InlineData("/health/ready")]
        public async Task SaglikUclari_AnonimErisilebilir_ve_200Doner(string yol)
        {
            var yanit = await _app.CreateClient().GetAsync(yol);

            Assert.Equal(HttpStatusCode.OK, yanit.StatusCode);
        }
    }
}
