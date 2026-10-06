using System.Net;
using System.Net.Http.Json;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fixtures;
using Xunit;

namespace Text2Sql.Tests.Integration.Security
{
    /// <summary>
    /// Hız sınırlama. Diğer testlerde sınır bilinçli olarak kapalıdır; burada
    /// AÇIK bir host kullanılır — aksi halde "yapılandırma var ama çalışıyor mu?"
    /// sorusu hiç yanıtlanmamış olurdu.
    /// </summary>
    public class RateLimitingTests
    {
        [Fact]
        public async Task KimlikUclari_LimitAsilinca_429Doner()
        {
            using var app = new RateLimitedTestApp();
            var istemci = app.CreateClient();
            var email = SampleData.UniqueEmail();

            // Kayıt akışının kendisi de "auth" politikasına tabidir
            await Tenants.RegisterAsync(istemci, "Limit Co", email);

            var durumlar = new List<HttpStatusCode>();
            for (var i = 0; i < 12; i++)
            {
                var yanit = await Tenants.LoginAsync(istemci, email);
                durumlar.Add(yanit.StatusCode);
                if (yanit.StatusCode == HttpStatusCode.TooManyRequests) break;
            }

            Assert.Contains(HttpStatusCode.TooManyRequests, durumlar);
        }

        [Fact]
        public async Task SorguUclari_LimitAsilinca_429Doner()
        {
            using var app = new RateLimitedTestApp();
            var kiraci = await app.NewTenant("Sorgu Limit Co").WithSqliteDataSource().BuildAsync();

            var durumlar = new List<HttpStatusCode>();
            for (var i = 0; i < RateLimitedTestApp.QueryPerMinute + 3; i++)
            {
                var yanit = await kiraci.Client.PostAsJsonAsync(
                    $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                    new { databaseId = kiraci.DataSourceId, question = $"soru {i}" });

                durumlar.Add(yanit.StatusCode);

                if (yanit.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    // Hız sınırı yanıtı da v2 hata sözleşmesine uymalı:
                    // istemci burada da "koda dallanabilmeli".
                    await ApiAssert.ProblemAsync(
                        yanit, HttpStatusCode.TooManyRequests, "RATE_LIMITED");
                    break;
                }
            }

            Assert.Contains(HttpStatusCode.TooManyRequests, durumlar);
        }
    }
}
