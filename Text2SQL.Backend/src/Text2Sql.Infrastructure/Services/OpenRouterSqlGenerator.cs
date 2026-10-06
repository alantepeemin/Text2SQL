using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Infrastructure.Services
{
    /// <summary>
    /// FAZ 3 güncellemeleri:
    /// - Dialect parametresi: prompt artık hedef veritabanı lehçesine göre üretilir.
    /// - CancellationToken: 60 sn'ye varan LLM çağrısı istemci koptuğunda iptal edilir.
    /// - Güvenlik doğrulaması buradan KALDIRILDI — tek yetkili nokta
    ///   ISqlStatementValidator'dır (çift/tutarsız doğrulama yerine tek sözleşme).
    /// - Dayanıklılık (retry/timeout/circuit breaker) HttpClient kaydında (Polly).
    /// </summary>
    public class OpenRouterSqlGenerator : ISqlGeneratorService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        /// <summary>
        /// Yapılandırma yoksa kullanılan model. Sağlayıcı kataloğundaki TAM slug
        /// olmalıdır; model emekliye ayrıldığında burası güncellenmelidir.
        /// </summary>
        public const string VarsayilanModel = "anthropic/claude-haiku-4.5";

        private readonly string _model;
        private readonly bool _logUserQuestions;
        private readonly ILogger<OpenRouterSqlGenerator> _logger;

        public OpenRouterSqlGenerator(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<OpenRouterSqlGenerator> logger)
        {
            _httpClientFactory = httpClientFactory;
            // Yapılandırma verilmezse güncel bir varsayılan kullanılır.
            // Eski varsayılan (claude-3.5-sonnet) sağlayıcıdan kaldırıldı ve
            // her sorgu 404 ile başarısız oluyordu — sessiz varsayılanlar
            // eskidiğinde bu şekilde patlar, o yüzden geri düşüş loglanır.
            var yapilandirilanModel = config["OpenRouter:Model"];
            if (string.IsNullOrWhiteSpace(yapilandirilanModel))
            {
                _model = VarsayilanModel;
                logger.LogWarning(
                    "OpenRouter:Model yapılandırılmamış — varsayılan {Model} kullanılıyor. " +
                    "Modeli açıkça belirtmeniz önerilir.", _model);
            }
            else
            {
                _model = yapilandirilanModel;
            }

            _logUserQuestions = config.GetValue("Logging:LogUserQuestions", false);
            _logger = logger;
        }

        public async Task<SqlGenerationResult> GenerateSqlAsync(
            string schema, string question, string dialect,
            string? glossary = null, CancellationToken ct = default)
        {
            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = GetSystemPrompt(dialect) },
                        new { role = "user",   content = BuildPrompt(schema, question, dialect, glossary) }
                    },
                    max_tokens = 2048,
                    temperature = 0.1,
                    top_p = 0.9
                };

                var httpClient = _httpClientFactory.CreateClient("OpenRouter");
                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content, ct);
                var responseContent = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("OpenRouter API error: Status={Status}, Model={Model}, Content={Content}",
                        response.StatusCode, _model, responseContent);

                    // 404 neredeyse her zaman "model slug'ı yanlış/emekli" demektir.
                    // Genel bir "404" mesajı kullanıcıyı da geliştiriciyi de
                    // yanlış yere bakmaya iter; sebebi açıkça söylüyoruz.
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new ApplicationException(
                            $"AI modeli bulunamadı: '{_model}'. OpenRouter:Model ayarını " +
                            "sağlayıcı kataloğundaki güncel bir slug ile güncelleyin.");

                    throw new ApplicationException($"AI servisi hata döndürdü: {(int)response.StatusCode}");
                }

                var result = JsonDocument.Parse(responseContent);
                var sqlResponse = result.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
                    ?.Trim();

                var extractedSql = ExtractSqlFromResponse(sqlResponse ?? "");

                if (string.IsNullOrWhiteSpace(extractedSql))
                    throw new InvalidOperationException("Geçerli SQL sorgusu çıkarılamadı.");

                // SaaS-3: Sağlayıcının bildirdiği gerçek token kullanımı.
                // Yerel tahmin yerine sağlayıcı verisi kullanılır — faturalama
                // ile birebir tutarlı olması için.
                int promptTokens = 0, completionTokens = 0;
                if (result.RootElement.TryGetProperty("usage", out var usage))
                {
                    if (usage.TryGetProperty("prompt_tokens", out var pt))     promptTokens = pt.GetInt32();
                    if (usage.TryGetProperty("completion_tokens", out var ctk)) completionTokens = ctk.GetInt32();
                }

                var usedModel = result.RootElement.TryGetProperty("model", out var m)
                    ? (m.GetString() ?? _model)
                    : _model;

                // SaaS-8: Soru metni PII içerebilir → varsayılan olarak loglanmaz.
                // Hata ayıklama gerektiğinde Logging:LogUserQuestions=true ile açılır
                // (bilinçli, geçici ve belgelenmiş bir karar olmalıdır).
                if (_logUserQuestions)
                    _logger.LogDebug("Question: {Question}", question);
                _logger.LogDebug("Generated SQL: {Sql}", extractedSql);
                _logger.LogDebug("Token kullanımı: prompt={P}, completion={C}, model={M}",
                    promptTokens, completionTokens, usedModel);

                return new SqlGenerationResult(extractedSql, usedModel, promptTokens, completionTokens);
            }
            catch (OperationCanceledException)
            {
                throw; // iptal — sarmalamadan yukarı
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error in OpenRouter response");
                throw new ApplicationException("AI yanıtı işlenemedi.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling OpenRouter API");
                throw new ApplicationException("AI servisi ile bağlantı kurulamadı.");
            }
        }

        private static string GetSystemPrompt(string dialect) => $@"You are an expert {dialect} database analyst and query generator. Your task is to convert natural language questions into precise, efficient SQL queries.

KEY PRINCIPLES:
- Generate syntactically correct {dialect} queries
- Prioritize query clarity and performance
- Handle ambiguous requests intelligently
- Use meaningful column aliases for better readability

SAFETY RULES:
- ONLY generate SELECT and WITH statements
- Never use DROP, DELETE, INSERT, UPDATE, ALTER, CREATE, TRUNCATE
- Add LIMIT clause if not specified to prevent large result sets
- Validate all table and column references against schema";

        private static string BuildPrompt(string schema, string question, string dialect, string? glossary)
        {
            // SaaS-9: Şema sözlüğü ŞEMADAN SONRA, sorudan ÖNCE eklenir.
            // Sıra bilinçli: model önce yapıyı görür, sonra iş anlamlarını okur;
            // böylece terimleri doğru kolonlara bağlar.
            var glossaryBlock = string.IsNullOrWhiteSpace(glossary)
                ? string.Empty
                : Environment.NewLine + glossary;

            return $@"DATABASE SCHEMA ({dialect}):
{schema}{glossaryBlock}

USER QUESTION: ""{question}""

ANALYSIS STEPS:
1. Identify the main entities (tables) involved
2. Determine what metrics or data points are needed
3. Identify any filtering, grouping, or sorting requirements
4. Consider potential ambiguities and make reasonable assumptions

QUERY REQUIREMENTS:
- Use proper JOINs when accessing multiple tables
- Include appropriate WHERE clauses for filtering
- Add GROUP BY when aggregating data
- Use ORDER BY for meaningful sorting
- Include LIMIT 500 unless user specifies otherwise
- Use descriptive column aliases (e.g., 'total_sales', 'customer_count')
- Handle NULL values appropriately with COALESCE when needed
- Use only {dialect}-compatible syntax and functions
- If a BUSINESS GLOSSARY is provided, prefer its interpretations over your own guesses

Now generate the SQL query for the given question. Return ONLY the SQL query, no explanations.

SQL Query:";
        }

        private static string ExtractSqlFromResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return "";

            var cleaned = response.Trim();
            if (cleaned.StartsWith("```sql"))
                cleaned = cleaned[6..];
            else if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                if (firstNewline > 0) cleaned = cleaned[(firstNewline + 1)..];
            }

            if (cleaned.EndsWith("```"))
                cleaned = cleaned[..^3];

            var sqlBuilder = new StringBuilder();
            bool foundSqlStart = false;

            foreach (var line in cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--")) continue;

                if (!foundSqlStart &&
                    (trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase)))
                {
                    foundSqlStart = true;
                }

                if (foundSqlStart)
                {
                    sqlBuilder.AppendLine(trimmed);
                    if (trimmed.EndsWith(";")) break;
                }
            }

            var sql = sqlBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(sql))
            {
                sql = response.Trim().TrimEnd('`').Trim();
                if (!sql.EndsWith(";")) sql += ";";
            }

            return sql;
        }
    }
}
