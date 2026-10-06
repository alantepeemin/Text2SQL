using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using Text2Sql.Application.Queries;
using Text2Sql.Infrastructure.Persistence;
using Text2Sql.Tests.Common.Assertions;
using Text2Sql.Tests.Common.Builders;
using Text2Sql.Tests.Common.Data;
using Text2Sql.Tests.Common.Fakes;
using Text2Sql.Tests.Common.Fixtures;
using Text2Sql.Tests.Common.Http;
using Xunit;

namespace Text2Sql.Tests.Integration.Queries
{
    /// <summary>SaaS-9 — Sözlük, SQL cache ve geri bildirim (uçtan uca).</summary>
    public class SchemaDictionaryAndCacheTests : IClassFixture<TestApp>
    {
        private readonly TestApp _factory;
        public SchemaDictionaryAndCacheTests(TestApp factory) => _factory = factory;

        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

        private async Task<(HttpClient client, int projectId, int dbId)> OrtamKurAsync(string ad)
        {
            var client = _factory.CreateClient();
            var token = await Tenants.RegisterAsync(
                client, ad, $"s9-{Guid.NewGuid():N}@test.local");
            client.UseBearer(token);

            var projectId = await Projects.CreateAsync(client, $"{ad} Projesi");
            var (dbId, upload) = await DataSources.UploadSqliteAsync(
                client, projectId, SampleData.SqliteBytes());
            Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

            return (client, projectId, dbId);
        }

        [Fact]
        public async Task Sozluk_EklenirVePromptaEnjekteEdilir()
        {
            var (client, projectId, dbId) = await OrtamKurAsync("Glossary Co");

            var upsert = await client.PutAsJsonAsync($"/api/projects/{projectId}/databases/{dbId}/dictionary",
                new { tableName = "cities", columnName = "population", description = "resmi nüfus sayımı sonucu" });
            Assert.Equal(HttpStatusCode.OK, upsert.StatusCode);

            var list = await client.GetAsync($"/api/projects/{projectId}/databases/{dbId}/dictionary");
            var items = (await list.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");
            Assert.Equal(1, items.GetArrayLength());

            // Sorgu çalıştır → sözlük prompt'a girmiş olmalı
            _factory.Llm.ResetGlossary();
            var exec = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "En kalabalık şehir?" });
            Assert.Equal(HttpStatusCode.OK, exec.StatusCode);

            Assert.NotNull(_factory.Llm.LastGlossary);
            Assert.Contains("resmi nüfus sayımı sonucu", _factory.Llm.LastGlossary!);
            Assert.Contains("BUSINESS GLOSSARY", _factory.Llm.LastGlossary!);
        }

        [Fact]
        public async Task AyniSoru_IkinciCagridaLLMeGitmez_TokenTuketmez()
        {
            var (client, projectId, dbId) = await OrtamKurAsync("Cache Co");

            var oncekiCagriSayisi = _factory.Llm.CallCount;

            var ilk = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });
            Assert.Equal(HttpStatusCode.OK, ilk.StatusCode);
            var ilkCagri = _factory.Llm.CallCount;
            Assert.True(ilkCagri > oncekiCagriSayisi, "İlk sorgu LLM'e gitmeliydi.");

            // Aynı soru → cache isabeti, LLM çağrılmamalı
            var ikinci = await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });
            Assert.Equal(HttpStatusCode.OK, ikinci.StatusCode);
            Assert.Equal(ilkCagri, _factory.Llm.CallCount);

            // Ölçüm: ikinci sorgu için token yazılmamalı (gerçeği yansıtmalı)
            var usage = await client.GetAsync("/api/company/usage");
            var data = (await usage.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");
            Assert.Equal(2, data.GetProperty("queryCount").GetInt32());

            // Yalnızca BİR LLM çağrısının token'ı kaydedilmiş olmalı
            Assert.Equal(FakeSqlGenerator.FakePromptTokens,
                         data.GetProperty("promptTokens").GetInt64());
        }

        [Fact]
        public async Task SozlukDegisince_CacheKendiliğindenGecersizlesir()
        {
            var (client, projectId, dbId) = await OrtamKurAsync("Invalidate Co");

            await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });
            var cagriSayisi = _factory.Llm.CallCount;

            // Sözlük değişti → cache anahtarı değişmeli
            await client.PutAsJsonAsync($"/api/projects/{projectId}/databases/{dbId}/dictionary",
                new { tableName = "cities", description = "şehir ana tablosu" });

            await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });

            Assert.True(_factory.Llm.CallCount > cagriSayisi,
                "Sözlük değiştiğinde SQL cache geçersizleşip LLM yeniden çağrılmalıydı.");
        }

        [Fact]
        public async Task GeriBildirim_KaydedilirVeDogrulukOraniHesaplanir()
        {
            var (client, projectId, dbId) = await OrtamKurAsync("Feedback Co");

            await client.PostAsJsonAsync($"/api/projects/{projectId}/queries/execute",
                new { databaseId = dbId, question = "Kaç şehir var?" });

            var history = await client.GetAsync($"/api/projects/{projectId}/queries/history");
            var items = await history.Content.ReadFromJsonAsync<JsonElement>(J);
            Assert.True(items.GetArrayLength() > 0);

            // Geçmiş DTO'sunda Id yok; DB'den son kaydı al
            int queryHistoryId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                queryHistoryId = (await db.QueryHistories
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .OrderByDescending(h => h.Id)
                    .FirstAsync()).Id;
            }

            var gonder = await client.PostAsJsonAsync(
                $"/api/projects/{projectId}/queries/{queryHistoryId}/feedback",
                new { isHelpful = true, comment = "doğru sonuç" });
            Assert.Equal(HttpStatusCode.OK, gonder.StatusCode);

            var summary = await client.GetAsync($"/api/projects/{projectId}/queries/feedback-summary");
            var data = (await summary.Content.ReadFromJsonAsync<JsonElement>(J)).GetProperty("data");

            Assert.Equal(1, data.GetProperty("totalFeedback").GetInt32());
            Assert.Equal(1, data.GetProperty("helpfulCount").GetInt32());
            Assert.Equal(100.0, data.GetProperty("accuracyRate").GetDouble());
        }
    }
}
