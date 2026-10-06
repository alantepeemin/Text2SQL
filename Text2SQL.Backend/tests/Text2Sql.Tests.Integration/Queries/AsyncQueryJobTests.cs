using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Text2Sql.Domain.Entities;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Queries
{
    /// <summary>
    /// Asenkron sorgu yaşam döngüsü (202 + durum sorgulama).
    ///
    /// Kritik nokta: arka plan işçisinin HTTP bağlamı yoktur. Kiracı bağlamı
    /// ambient olarak taşınmazsa global kiracı filtresi pasif kalır ve
    /// asenkron sorgu izolasyonu deler. Son test tam olarak bunu mühürler.
    /// </summary>
    public class AsyncQueryJobTests
    {
        [Fact]
        public async Task Baslatma_202Doner_ve_ArkaPlandaTamamlanir()
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Asenkron Co").WithSqliteDataSource().BuildAsync();

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute-async",
                new { databaseId = kiraci.DataSourceId, question = "en kalabalık şehirler" });

            Assert.Equal(HttpStatusCode.Accepted, yanit.StatusCode);
            Assert.NotNull(yanit.Headers.Location);

            var kabul = await yanit.ReadJsonAsync();
            var jobId = kabul.GetProperty("jobId").GetInt32();
            Assert.Equal(QueryJobStatuses.Pending, kabul.GetProperty("status").GetString());

            var durum = await DurumBekleAsync(kiraci, jobId);

            Assert.Equal(QueryJobStatuses.Succeeded, durum.GetProperty("status").GetString());
            var sonuc = durum.GetProperty("result");
            Assert.Equal(FakeSqlGenerator.GeneratedSql, sonuc.GetProperty("sql").GetString());
            Assert.Equal(SampleDataRows, sonuc.GetProperty("rows").GetArrayLength());
        }

        private const int SampleDataRows = 3;

        [Fact]
        public async Task GecersizVeriKaynagi_HemenHataDoner_KuyrugaGirmez()
        {
            // İstemci hatalı isteğin sonucunu saniyeler sonra öğrenmemeli:
            // doğrulama iş oluşturulmadan yapılır.
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Asenkron Gecersiz").WithProject().BuildAsync();

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute-async",
                new { databaseId = 999999, question = "test" });

            await ApiAssert.ProblemAsync(yanit, HttpStatusCode.NotFound, "NOT_FOUND");

            var isSayisi = await app.QueryDatabaseAsync(
                db => db.QueryJobs.IgnoreQueryFilters().CountAsync());
            Assert.Equal(0, isSayisi);
        }

        [Fact]
        public async Task BaskaKiracininIsi_404Doner_KiraciIzolasyonuKorunur()
        {
            using var app = new TestApp();
            var a = await app.NewTenant("Asenkron KiraciA").WithSqliteDataSource().BuildAsync();
            var b = await app.NewTenant("Asenkron KiraciB").WithProject().BuildAsync();

            var olustur = await a.Client.PostAsJsonAsync(
                $"/api/v2/projects/{a.ProjectId}/queries/execute-async",
                new { databaseId = a.DataSourceId, question = "şehirler" });
            olustur.EnsureSuccessStatusCode();

            var jobId = (await olustur.ReadJsonAsync()).GetProperty("jobId").GetInt32();

            var yanit = await b.Client.GetAsync(
                $"/api/v2/projects/{b.ProjectId}/queries/jobs/{jobId}");

            ApiAssert.HiddenFromOtherTenant(yanit, "Başka kiracının asenkron işi");
        }

        [Fact]
        public async Task TamamlananIs_SorguGecmisineDeYazilir()
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Asenkron Gecmis").WithSqliteDataSource().BuildAsync();

            var olustur = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute-async",
                new { databaseId = kiraci.DataSourceId, question = "şehirler" });
            var jobId = (await olustur.ReadJsonAsync()).GetProperty("jobId").GetInt32();

            await DurumBekleAsync(kiraci, jobId);

            var gecmis = await kiraci.Client.GetAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/history");
            var liste = await ApiAssert.BareAsync(gecmis);

            Assert.True(liste.GetArrayLength() > 0,
                "Asenkron sorgu da geçmişe yazılmalı — senkron sorgudan farkı yok.");
        }

        /// <summary>İş sonlanana kadar durum uçunu yoklar.</summary>
        private static async Task<JsonElement> DurumBekleAsync(
            TestTenant kiraci, int jobId, int saniye = 30)
        {
            var bitis = DateTime.UtcNow.AddSeconds(saniye);
            JsonElement son = default;

            while (DateTime.UtcNow < bitis)
            {
                var yanit = await kiraci.Client.GetAsync(
                    $"/api/v2/projects/{kiraci.ProjectId}/queries/jobs/{jobId}");
                yanit.EnsureSuccessStatusCode();
                son = await yanit.ReadJsonAsync();

                var durum = son.GetProperty("status").GetString();
                if (durum is QueryJobStatuses.Succeeded or QueryJobStatuses.Failed) return son;

                await Task.Delay(200);
            }

            Assert.Fail($"İş {saniye} saniyede sonlanmadı. Son durum: {son}");
            return son;
        }
    }
}
