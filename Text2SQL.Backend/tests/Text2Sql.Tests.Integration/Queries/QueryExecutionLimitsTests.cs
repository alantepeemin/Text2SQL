using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Queries
{
    /// <summary>
    /// Sorgu çalıştırma sınırları ve LLM çıktısına duyulan GÜVENSİZLİK.
    ///
    /// LLM'in ürettiği SQL kullanıcı girdisi kadar güvenilmezdir. Doğrulayıcının
    /// birim testleri var; buradaki testler doğrulayıcının gerçekten devrede
    /// olduğunu (yani boru hattına bağlandığını) kanıtlar.
    /// </summary>
    public class QueryExecutionLimitsTests
    {
        [Fact]
        public async Task SatirTavani_AsanSonuc_Kirpilir()
        {
            using var app = new RowLimitedTestApp();
            var kiraci = await app.NewTenant("Satir Tavani Co").WithSqliteDataSource().BuildAsync();

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = kiraci.DataSourceId, question = "tüm şehirler" });

            var sonuc = await ApiAssert.BareAsync(yanit);

            Assert.True(sonuc.GetProperty("success").GetBoolean());
            Assert.Equal(RowLimitedTestApp.MaxRows, sonuc.GetProperty("rows").GetArrayLength());
            Assert.True(SampleData.CityCount > RowLimitedTestApp.MaxRows,
                "Test anlamlı olsun diye örnek veri tavandan büyük olmalı.");
        }

        [Theory]
        [InlineData("DELETE FROM cities;")]
        [InlineData("DROP TABLE cities;")]
        [InlineData("SELECT name FROM cities; DELETE FROM cities;")]
        [InlineData("UPDATE cities SET population = 0;")]
        public async Task LLM_TehlikeliSQLUretirse_CalistirilmazVeVeriKorunur(string tehlikeliSql)
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant($"Tehlike Co {tehlikeliSql.Length}")
                                  .WithSqliteDataSource().BuildAsync();

            app.Llm.NextSqlOverride = tehlikeliSql;

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = kiraci.DataSourceId, question = "şehirleri sil" });

            var sonuc = await ApiAssert.BareAsync(yanit);
            Assert.False(sonuc.GetProperty("success").GetBoolean(),
                $"Tehlikeli SQL çalıştırıldı: {tehlikeliSql}");

            // Veri bozulmamış olmalı — doğrulayıcı sadece hata döndürmüyor,
            // gerçekten ÇALIŞTIRMIYOR.
            app.Llm.NextSqlOverride = null;
            var kontrol = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = kiraci.DataSourceId, question = "şehirler" });

            var kontrolSonuc = await ApiAssert.BareAsync(kontrol);
            Assert.True(kontrolSonuc.GetProperty("success").GetBoolean());
            Assert.Equal(SampleData.CityCount, kontrolSonuc.GetProperty("rows").GetArrayLength());
        }

        [Fact]
        public async Task BasarisizSorgu_KullaniciKotasiniTuketmez()
        {
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Iade Co").WithSqliteDataSource().BuildAsync();

            var oncekiKota = await KalanTokenAsync(app);

            app.Llm.NextSqlOverride = "DELETE FROM cities;";
            await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = kiraci.DataSourceId, question = "hatalı sorgu" });
            app.Llm.NextSqlOverride = null;

            Assert.Equal(oncekiKota, await KalanTokenAsync(app));
        }

        [Fact]
        public async Task Model_SQLYerineAciklamaDondurdugunde_AnlasilirHataDoner()
        {
            // Model daraltılmış şemayla SQL üretemediğinde düz metin döndürür.
            // Bu metni doğrulayıcıya vermek "Güvenlik: yalnızca SELECT
            // çalıştırılabilir" gibi tamamen yanıltıcı bir hata üretiyordu:
            // kullanıcı da geliştirici de yanlış yere bakıyordu.
            using var app = new TestApp();
            var kiraci = await app.NewTenant("Aciklama Co").WithSqliteDataSource().BuildAsync();

            app.Llm.NextSqlOverride =
                "I cannot generate this query because the required tables are not " +
                "available in the provided schema.";

            var yanit = await kiraci.Client.PostAsJsonAsync(
                $"/api/v2/projects/{kiraci.ProjectId}/queries/execute",
                new { databaseId = kiraci.DataSourceId, question = "cevaplanamayan soru" });

            var sonuc = await ApiAssert.BareAsync(yanit);

            Assert.False(sonuc.GetProperty("success").GetBoolean());

            var hata = sonuc.GetProperty("errorMessage").GetString() ?? string.Empty;
            Assert.Contains("SQL üretemedi", hata);
            Assert.DoesNotContain("Güvenlik", hata);
        }

        private static Task<int> KalanTokenAsync(TestApp app)
            => app.QueryDatabaseAsync(db => db.UserTokens
                .OrderBy(t => t.Id)
                .Select(t => t.RemainingTokens)
                .FirstAsync());
    }
}
