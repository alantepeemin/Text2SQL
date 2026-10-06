using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Text2Sql.Application.Common;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Database;
using Text2Sql.Application.DTOs.Query;
using Text2Sql.Domain.Entities;
using Text2Sql.Infrastructure.Persistence;

namespace Text2Sql.Infrastructure.Services
{
    /// <summary>
    /// FAZ 3: Sorgu akışı artık DB tipinden bağımsızdır.
    /// Eski if(sqlite)/else-throw yapısı IDataSourceProvider stratejisiyle
    /// değiştirildi — yeni DB tipi = yeni provider + DI kaydı (OCP).
    /// </summary>
    public class QueryService : IQueryService
    {
        private readonly ApplicationDbContext        _context;
        private readonly ISqlGeneratorService        _sqlGenerator;
        private readonly IDataSourceProviderFactory  _providerFactory;
        private readonly ISqlStatementValidator      _sqlValidator;
        private readonly IFileStorage                 _fileStorage;
        private readonly ISchemaOptimizer              _schemaOptimizer;
        private readonly ISchemaDictionaryService      _schemaDictionary;
        private readonly bool                          _sqlCacheEnabled;
        private readonly IUsageQuotaService            _quota;
        private readonly ILlmCostCalculator            _costCalculator;
        private readonly ICurrentUserContext         _currentUser;
        private readonly IConfiguration              _configuration;
        private readonly IMemoryCache                _cache;
        private readonly ILogger<QueryService>       _logger;

        // Faz 5: Observability — OTel kayıtlıysa dışa aktarılır, değilse maliyeti sıfıra yakındır
        private static readonly ActivitySource Tracer = new("Text2Sql.Query");
        private static readonly Meter Meter = new("Text2Sql");
        private static readonly Counter<long> QueryCounter =
            Meter.CreateCounter<long>("text2sql.queries", description: "Çalıştırılan sorgu sayısı");
        private static readonly Counter<long> QuotaRejectionCounter =
            Meter.CreateCounter<long>("text2sql.quota_rejections", description: "Kota nedeniyle reddedilen istekler");
        private static readonly Counter<long> TokenCounter =
            Meter.CreateCounter<long>("text2sql.llm.tokens", description: "Tüketilen LLM token sayısı");
        private static readonly Histogram<double> QueryDuration =
            Meter.CreateHistogram<double>("text2sql.query.duration", unit: "ms");

        private static readonly Counter<long> SqlCacheHitCounter =
            Meter.CreateCounter<long>("text2sql.sql_cache.hits", description: "SQL cache isabet sayısı");
        private static readonly Histogram<double> PromptReductionHistogram =
            Meter.CreateHistogram<double>("text2sql.prompt.reduction_percent", unit: "%");

        private const string SchemaCachePrefix = "schema_";
        private const string SqlCachePrefix = "sql_";
        private static readonly TimeSpan SqlCacheDuration = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan SchemaCacheDuration = TimeSpan.FromMinutes(30);

        public QueryService(
            ApplicationDbContext context,
            ISqlGeneratorService sqlGenerator,
            IDataSourceProviderFactory providerFactory,
            ISqlStatementValidator sqlValidator,
            IFileStorage fileStorage,
            ISchemaOptimizer schemaOptimizer,
            ISchemaDictionaryService schemaDictionary,
            IUsageQuotaService quota,
            ILlmCostCalculator costCalculator,
            ICurrentUserContext currentUser,
            IConfiguration configuration,
            IMemoryCache cache,
            ILogger<QueryService> logger)
        {
            _context         = context;
            _sqlGenerator    = sqlGenerator;
            _providerFactory = providerFactory;
            _sqlValidator    = sqlValidator;
            _fileStorage     = fileStorage;
            _schemaOptimizer  = schemaOptimizer;
            _schemaDictionary = schemaDictionary;
            _quota            = quota;
            _sqlCacheEnabled  = configuration.GetValue("Query:SqlCacheEnabled", true);
            _costCalculator  = costCalculator;
            _currentUser     = currentUser;
            _configuration   = configuration;
            _cache           = cache;
            _logger          = logger;
        }

        public async Task<QueryResultDto> ExecuteQueryAsync(
            int projectId, int databaseId, string question, CancellationToken ct = default)
        {
            var stopwatch = Stopwatch.StartNew();

            // ── 1. Veri kaynağını yükle (tenant + project çapraz kontrolü) ──
            var database = await _context.ProjectDatabases
                .AsNoTracking()
                .FirstOrDefaultAsync(db =>
                    db.Id        == databaseId &&
                    db.ProjectId == projectId   &&
                    db.CompanyId == _currentUser.TenantId &&
                    db.IsActive, ct)
                ?? throw new NotFoundException("Veritabanı bulunamadı veya erişiminiz yok.");

            // ── 2. Proje erişim kontrolü ──
            if (!_currentUser.IsAdmin)
            {
                var hasAccess = await _context.ProjectAccesses
                    .AnyAsync(a => a.ProjectId == projectId && a.UserId == _currentUser.UserId, ct);
                if (!hasAccess)
                    throw new ForbiddenException("Bu projeye erişiminiz yok.");
            }

            // ── 3a. SaaS-3: ORGANİZASYON token kotası ──
            // Company.MonthlyQueryLimit bugüne kadar hiç uygulanmıyordu (ADR-005).
            // Kullanıcı kotasından önce kontrol edilir ki boşa rezervasyon olmasın.
            await _quota.EnsureOrganizationQuotaAsync(_currentUser.TenantId, ct);

            // ── 3b. Kullanıcı kotası — atomik rezervasyon (başarısızlıkta iade) ──
            var reserved = await _context.UserTokens
                .Where(t => t.UserId == _currentUser.UserId && t.RemainingTokens > 0)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RemainingTokens, t => t.RemainingTokens - 1)
                    .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), CancellationToken.None);

            if (reserved == 0)
            {
                var tokenExists = await _context.UserTokens
                    .AnyAsync(t => t.UserId == _currentUser.UserId, CancellationToken.None);

                if (tokenExists)
                {
                    QuotaRejectionCounter.Add(1);
                    throw new QuotaExceededException("Aylık sorgu limitiniz dolmuştur. Yenilenmesi için ay başını bekleyin.");
                }
                throw new InvalidOperationException("Token bilgisi bulunamadı. Lütfen admin ile iletişime geçin.");
            }

            string generatedSql = string.Empty;
            SqlGenerationResult? llmResult = null;

            try
            {
                // ── 4. Provider seçimi — DB tipine özel tüm mantık provider'da ──
                var provider = _providerFactory.GetProvider(database.DbType);

                // ── 5. Şema (30 dk cache) ──
                string tamSema;
                using (Tracer.StartActivity("schema.extract"))
                    tamSema = await GetCachedSchemaAsync(provider, database, ct);

                // ── 5b. SaaS-9: Soruya göre şema daraltma (token tasarrufu) ──
                var optimizasyon = _schemaOptimizer.Optimize(tamSema, question);
                var schema = optimizasyon.Schema;

                if (optimizasyon.WasReduced)
                {
                    PromptReductionHistogram.Record(optimizasyon.ReductionRatio * 100);
                    _logger.LogDebug(
                        "Şema daraltıldı: {Included}/{Total} tablo, ~%{Ratio:F0} tasarruf",
                        optimizasyon.IncludedTableCount, optimizasyon.TotalTableCount,
                        optimizasyon.ReductionRatio * 100);
                }

                // ── 5c. SaaS-9: İş sözlüğü (semantic layer) ──
                var glossary = await _schemaDictionary.BuildGlossaryAsync(databaseId, schema, ct);

                // ── 6. SaaS-9: SQL cache — aynı soru + aynı şema + aynı sözlük ──
                // Anahtar şema ve sözlük hash'ini içerdiği için şema değiştiğinde
                // eski SQL kendiliğinden geçersizleşir; ayrı invalidasyon gerekmez.
                var sqlCacheKey = SqlCacheKey(databaseId, question, schema, glossary);
                var cachedSql = _sqlCacheEnabled
                    ? _cache.Get<SqlGenerationResult>(sqlCacheKey)
                    : null;

                if (cachedSql != null)
                {
                    // Cache isabetinde LLM ÇAĞRILMADI → token tüketimi 0 kaydedilir
                    // (ölçüm gerçeği yansıtmalı; sahte token yazmak maliyeti şişirir).
                    llmResult = cachedSql with { PromptTokens = 0, CompletionTokens = 0 };
                    generatedSql = cachedSql.Sql;
                    SqlCacheHitCounter.Add(1);
                    _logger.LogDebug("SQL cache isabeti: DbId={DbId}", databaseId);
                }
                else
                {
                    using (Tracer.StartActivity("llm.generate"))
                        llmResult = await _sqlGenerator.GenerateSqlAsync(
                            schema, question, provider.Dialect, glossary, ct);

                    generatedSql = llmResult.Sql;
                }

                if (string.IsNullOrWhiteSpace(generatedSql))
                    throw new InvalidOperationException("SQL sorgusu oluşturulamadı.");

                // ── 6b. Model SQL yerine AÇIKLAMA döndürdüyse ────────────────
                // Daraltılmış şema soruyu cevaplamaya yetmiyorsa model
                // "bu tablolar şemada yok" diye düz metin yazar. Bu metni
                // doğrulayıcıya vermek "Güvenlik: yalnızca SELECT çalıştırılabilir"
                // gibi tamamen yanıltıcı bir hata üretiyordu — asıl sorun
                // güvenlik değil, şema seçimiydi.
                //
                // Daraltma bir SEZGİDİR; yanıldığında sorguyu kaybetmek yerine
                // tam şemayla BİR KEZ daha deniyoruz. Maliyeti ikinci bir LLM
                // çağrısı, kazancı cevapsız kalmayan bir sorudur.
                if (!SqlGorunumundeMi(generatedSql) && optimizasyon.WasReduced)
                {
                    _logger.LogWarning(
                        "Model daraltılmış şemayla SQL üretemedi ({Included}/{Total} tablo) — " +
                        "tam şemayla yeniden deneniyor. DbId={DbId}",
                        optimizasyon.IncludedTableCount, optimizasyon.TotalTableCount, databaseId);

                    var tamSozluk = await _schemaDictionary.BuildGlossaryAsync(databaseId, tamSema, ct);

                    SqlGenerationResult tekrar;
                    using (Tracer.StartActivity("llm.generate.retry"))
                        tekrar = await _sqlGenerator.GenerateSqlAsync(
                            tamSema, question, provider.Dialect, tamSozluk, ct);

                    // Maliyet muhasebesi: iki çağrı da yapıldı, ikisi de ölçülür.
                    llmResult = tekrar with
                    {
                        PromptTokens     = (llmResult?.PromptTokens     ?? 0) + tekrar.PromptTokens,
                        CompletionTokens = (llmResult?.CompletionTokens ?? 0) + tekrar.CompletionTokens
                    };

                    generatedSql = tekrar.Sql;

                    // Cache anahtarı artık TAM şemayı temsil etmeli; aksi halde
                    // sonuç daraltılmış şema anahtarına yazılır ve bir sonraki
                    // seferde yine iki çağrı yapılırdı.
                    schema      = tamSema;
                    sqlCacheKey = SqlCacheKey(databaseId, question, tamSema, tamSozluk);
                }

                if (!SqlGorunumundeMi(generatedSql))
                    throw new BadRequestException(
                        "Model bu soru için SQL üretemedi. Modelin yanıtı: " +
                        Kisalt(generatedSql, 300));

                // ── 7. Güvenlik doğrulaması (literal/yorum farkındalıklı) ──
                _sqlValidator.EnsureSafeSelect(generatedSql);

                // ── 8. Salt-okunur çalıştırma (satır limiti + timeout) ──
                var maxRows        = _configuration.GetValue("Query:MaxRows", 1000);
                var timeoutSeconds = _configuration.GetValue("Query:TimeoutSeconds", 30);

                QueryExecutionResult result;
                using (Tracer.StartActivity("sql.execute"))
                    result = await provider.ExecuteReadOnlyAsync(
                        database, generatedSql, maxRows, timeoutSeconds, ct);

                // SaaS-9: SQL ancak BAŞARIYLA ÇALIŞTIKTAN SONRA cache'lenir.
                // Doğrulamayı geçip çalışırken hata veren SQL'i cache'lemek
                // aynı hatayı 15 dakika boyunca tekrarlatırdı.
                if (_sqlCacheEnabled && cachedSql == null)
                    _cache.Set(sqlCacheKey, llmResult, SqlCacheDuration);

                stopwatch.Stop();
                QueryCounter.Add(1,
                    new KeyValuePair<string, object?>("success", true),
                    new KeyValuePair<string, object?>("dialect", provider.Dialect));
                QueryDuration.Record(stopwatch.ElapsedMilliseconds);

                // ── 9. Geçmiş kaydı — sonuç gövdesi dosya deposunda (Faz 4) ──
                var resultJson = JsonSerializer.Serialize(result.Rows);
                string? resultPath = null;
                try
                {
                    resultPath = await _fileStorage.SaveTextAsync(
                        Path.Combine(_currentUser.TenantId.ToString(), projectId.ToString()),
                        $"{Guid.NewGuid():N}.json", resultJson, CancellationToken.None);
                }
                catch (Exception storeEx)
                {
                    // Dosya deposu hatası sorguyu düşürmez — DB kolonuna geri düş
                    _logger.LogError(storeEx, "Sonuç dosyaya yazılamadı, DB fallback kullanılıyor.");
                }

                _context.QueryHistories.Add(new QueryHistory
                {
                    CompanyId       = _currentUser.TenantId, // SaaS-1
                    ProjectId       = projectId,
                    DatabaseId      = databaseId,
                    UserId          = _currentUser.UserId,
                    Question        = question,
                    SqlQuery        = generatedSql,
                    ResultPath      = resultPath,
                    Result          = resultPath == null ? resultJson : null,
                    Columns         = JsonSerializer.Serialize(result.Columns),
                    IsSuccessful    = true,
                    ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                    TokensConsumed  = 1,
                    CreatedAt       = DateTime.UtcNow
                });

                // SaaS-3: Ölçüm kaydı — faturalama ve gerçek kotanın temeli
                WriteUsageRecord(projectId, databaseId, llmResult, stopwatch.ElapsedMilliseconds, true);

                await _context.SaveChangesAsync(CancellationToken.None);

                // SaaS-8: PII disiplini — soru metni loglara YAZILMAZ.
                // Kullanıcı soruları müşteri verisi içerebilir ("Ahmet Yılmaz'ın
                // siparişleri"). Teşhis için gereken bilgi (proje, kaynak, süre)
                // yeterlidir; soru metni yalnızca DB'deki geçmişte tutulur.
                _logger.LogInformation(
                    "Sorgu başarılı: ProjectId={PId}, DbId={DbId}, Dialect={Dialect}, Süre={Ms}ms",
                    projectId, databaseId, provider.Dialect, stopwatch.ElapsedMilliseconds);

                return new QueryResultDto
                {
                    Question  = question,
                    Sql       = generatedSql,
                    Columns   = result.Columns,
                    Rows      = result.Rows,
                    Success   = true,
                    CreatedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                QueryCounter.Add(1, new KeyValuePair<string, object?>("success", false));
                _logger.LogError(ex,
                    "Sorgu hatası: ProjectId={PId}, DbId={DbId}", projectId, databaseId);

                // ── Token iadesi: başarısız sorgu kota tüketmez ──
                try
                {
                    await _context.UserTokens
                        .Where(t => t.UserId == _currentUser.UserId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.RemainingTokens, t => t.RemainingTokens + 1)
                            .SetProperty(t => t.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
                }
                catch (Exception refundEx)
                {
                    _logger.LogError(refundEx, "Token iadesi başarısız: UserId={UId}", _currentUser.UserId);
                }

                try
                {
                    // SaaS-3: LLM çağrıldıysa token HARCANDI — sorgu başarısız olsa
                    // bile maliyet gerçektir; ölçüm kaydı yazılır (kullanıcı kotası
                    // iade edilir ama maliyet muhasebesi doğru kalır).
                    if (llmResult != null)
                        WriteUsageRecord(projectId, databaseId, llmResult, stopwatch.ElapsedMilliseconds, false);

                    _context.QueryHistories.Add(new QueryHistory
                    {
                        CompanyId       = _currentUser.TenantId, // SaaS-1
                        ProjectId       = projectId,
                        DatabaseId      = databaseId,
                        UserId          = _currentUser.UserId,
                        Question        = question,
                        SqlQuery        = generatedSql,
                        IsSuccessful    = false,
                        ErrorMessage    = ex.Message,
                        ExecutionTimeMs = (int)stopwatch.ElapsedMilliseconds,
                        TokensConsumed  = 0,
                        CreatedAt       = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync(CancellationToken.None);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Hata kaydı sırasında exception oluştu.");
                }

                return new QueryResultDto
                {
                    Question     = question,
                    Sql          = generatedSql,
                    Columns      = new List<string>(),
                    Rows         = new List<List<string>>(),
                    Success      = false,
                    ErrorMessage = ex.Message,
                    CreatedAt    = DateTime.UtcNow
                };
            }
        }

        public async Task<List<DatabaseOption>> GetAvailableDatabasesAsync(int projectId)
        {
            if (!_currentUser.IsAdmin)
            {
                var hasAccess = await _context.ProjectAccesses
                    .AnyAsync(a => a.ProjectId == projectId && a.UserId == _currentUser.UserId);
                if (!hasAccess)
                    throw new ForbiddenException("Bu projeye erişiminiz yok.");
            }

            return await _context.ProjectDatabases
                .Where(db => db.ProjectId == projectId
                          && db.CompanyId == _currentUser.TenantId
                          && db.IsActive)
                .Select(db => new DatabaseOption
                {
                    Id       = db.Id,
                    Name     = db.ConnectionName,
                    DbType   = db.DbType,
                    IsActive = db.IsActive
                })
                .ToListAsync();
        }

        public async Task<PagedResult<QueryResultDto>> GetHistoryAsync(int projectId, int page = 1, int pageSize = 50)
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            bool hasAccess = _currentUser.IsAdmin
                ? await _context.Projects.AnyAsync(p =>
                    p.Id == projectId && p.CompanyId == _currentUser.TenantId)
                : await _context.ProjectAccesses.AnyAsync(a =>
                    a.ProjectId == projectId && a.UserId == _currentUser.UserId);

            if (!hasAccess)
                throw new ForbiddenException("Bu projeye erişiminiz yok.");

            var query = _context.QueryHistories
                .Where(h => h.ProjectId == projectId);

            if (!_currentUser.IsAdmin)
                query = query.Where(h => h.UserId == _currentUser.UserId);

            // API v2: toplam sayı sayfalama başlıkları için gerekli
            var toplamKayit = await query.CountAsync();

            var historyList = await query
                .OrderByDescending(h => h.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var results = new List<QueryResultDto>(historyList.Count);

            foreach (var h in historyList)
            {
                var rows    = new List<List<string>>();
                var columns = new List<string>();

                if (h.IsSuccessful)
                {
                    // Faz 4: önce dosya deposu, yoksa legacy DB kolonu
                    string? resultJson = h.Result;
                    if (!string.IsNullOrWhiteSpace(h.ResultPath))
                    {
                        try { resultJson = await _fileStorage.ReadTextAsync(h.ResultPath) ?? h.Result; }
                        catch { /* okunamadıysa legacy kolonla devam */ }
                    }

                    if (!string.IsNullOrWhiteSpace(resultJson))
                        try { rows    = JsonSerializer.Deserialize<List<List<string>>>(resultJson!) ?? new(); } catch { }

                    if (!string.IsNullOrWhiteSpace(h.Columns))
                        try { columns = JsonSerializer.Deserialize<List<string>>(h.Columns!) ?? new(); } catch { }
                }

                results.Add(new QueryResultDto
                {
                    Question     = h.Question ?? string.Empty,
                    Sql          = h.SqlQuery  ?? string.Empty,
                    Rows         = rows,
                    Columns      = columns,
                    Success      = h.IsSuccessful,
                    ErrorMessage = h.IsSuccessful ? null : (h.ErrorMessage ?? h.Result),
                    CreatedAt    = h.CreatedAt
                });
            }

            return PagedResult<QueryResultDto>.Create(results, page, pageSize, toplamKayit);
        }

        /// <summary>SaaS-3: Ölçüm kaydını context'e ekler (kaydetme çağırana ait).</summary>
        private void WriteUsageRecord(
            int projectId, int databaseId, SqlGenerationResult? llm, long durationMs, bool successful)
        {
            var promptTokens     = llm?.PromptTokens ?? 0;
            var completionTokens = llm?.CompletionTokens ?? 0;

            _context.UsageRecords.Add(new UsageRecord
            {
                CompanyId        = _currentUser.TenantId,
                UserId           = _currentUser.UserId,
                Type             = UsageTypes.Query,
                ProjectId        = projectId,
                DataSourceId     = databaseId,
                Model            = llm?.Model,
                PromptTokens     = promptTokens,
                CompletionTokens = completionTokens,
                EstimatedCostUsd = _costCalculator.Estimate(llm?.Model, promptTokens, completionTokens),
                DurationMs       = (int)durationMs,
                IsSuccessful     = successful,
                OccurredAt       = DateTime.UtcNow
            });

            TokenCounter.Add(promptTokens + completionTokens,
                new KeyValuePair<string, object?>("model", llm?.Model ?? "unknown"));
        }

        /// <summary>
        /// SaaS-9: Cache anahtarı = veri kaynağı + normalize soru + şema + sözlük hash'i.
        /// Şema veya sözlük değişince anahtar değişir → doğal invalidasyon.
        /// </summary>
        /// <summary>
        /// Dönen metin gerçekten SQL mi, yoksa modelin açıklaması mı?
        /// Baştaki yorumları ve kod bloğu işaretlerini atlayıp ilk anlamlı
        /// satırın SELECT/WITH ile başlamasına bakar.
        /// </summary>
        private static bool SqlGorunumundeMi(string metin)
        {
            if (string.IsNullOrWhiteSpace(metin)) return false;

            foreach (var ham in metin.Split('\n'))
            {
                var satir = ham.Trim().Trim('`').Trim();

                if (satir.Length == 0) continue;
                if (satir.StartsWith("--") || satir.StartsWith("/*")) continue;
                if (satir.Equals("sql", StringComparison.OrdinalIgnoreCase)) continue;

                return satir.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                    || satir.StartsWith("WITH",   StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string Kisalt(string metin, int uzunluk)
            => metin.Length <= uzunluk ? metin : metin[..uzunluk] + "…";

        private static string SqlCacheKey(int dataSourceId, string question, string schema, string? glossary)
        {
            var normalized = (question ?? string.Empty).Trim().ToLowerInvariant();
            var payload = normalized + "\n--\n" + schema + "\n--\n" + (glossary ?? string.Empty);
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(payload)));
            return SqlCachePrefix + dataSourceId + "_" + hash[..32];
        }

        private async Task<string> GetCachedSchemaAsync(
            IDataSourceProvider provider, ProjectDatabase database, CancellationToken ct)
        {
            var cacheKey = $"{SchemaCachePrefix}{database.Id}";

            if (_cache.TryGetValue(cacheKey, out string? cachedSchema) && cachedSchema != null)
            {
                _logger.LogDebug("Şema cache'den alındı: DbId={DbId}", database.Id);
                return cachedSchema;
            }

            var schema = await provider.GetSchemaAsync(database, ct);

            _cache.Set(cacheKey, schema, SchemaCacheDuration);
            _logger.LogDebug("Şema cache'e eklendi: DbId={DbId}", database.Id);

            return schema;
        }
    }
}
