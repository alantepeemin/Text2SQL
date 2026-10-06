using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Entities;
using Text2Sql.Infrastructure.Persistence;

namespace Text2Sql.Infrastructure.BackgroundServices
{
    /// <summary>
    /// Asenkron sorgu işçisi.
    ///
    /// KRİTİK TASARIM NOKTASI — kiracı bağlamı:
    /// Worker'ın HTTP isteği yoktur, dolayısıyla JWT claim'leri de yoktur.
    /// İşi çalıştırmadan önce IAmbientContext ile kiracı/kullanıcı/rol
    /// ayarlanır; böylece global kiracı filtresi ve yetki kontrolleri
    /// tıpkı HTTP isteğindeki gibi çalışır. Bu olmadan arka plan sorgusu
    /// kiracı izolasyonunu delerdi.
    ///
    /// Açılışta "asılı kalmış" işler temizlenir: uygulama çökerse
    /// running/pending kalan işler failed işaretlenir, istemci sonsuza
    /// kadar beklemez.
    /// </summary>
    public class QueryJobWorker : BackgroundService
    {
        private readonly IQueryJobQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<QueryJobWorker> _logger;

        public QueryJobWorker(
            IQueryJobQueue queue,
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<QueryJobWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Yalnızca bu süreçten ÖNCE oluşturulmuş işler "asılı" sayılır;
            // aksi halde açılışla eş zamanlı gelen yeni bir iş yanlışlıkla iptal edilirdi.
            await AsiliIsleriTemizleAsync(DateTime.UtcNow, stoppingToken);

            await foreach (var jobId in _queue.DequeueAllAsync(stoppingToken))
            {
                try
                {
                    await IsiCalistirAsync(jobId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Tek bir işin hatası worker'ı düşürmemeli
                    _logger.LogError(ex, "Sorgu işi çalıştırılırken beklenmeyen hata: JobId={JobId}", jobId);
                }
            }
        }

        private async Task IsiCalistirAsync(int jobId, CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Kiracı bağlamı henüz yok → filtreyi bilinçli atlatıyoruz
            var job = await db.QueryJobs.IgnoreQueryFilters()
                .FirstOrDefaultAsync(j => j.Id == jobId, ct);

            if (job == null || job.IsTerminal) return;

            // Kullanıcının bu organizasyondaki rolü — yetki kontrolleri için gerekli
            var role = await db.Memberships.IgnoreQueryFilters()
                .Where(m => m.UserId == job.UserId && m.CompanyId == job.CompanyId)
                .Select(m => m.Role)
                .FirstOrDefaultAsync(ct) ?? "user";

            var ambient = scope.ServiceProvider.GetRequiredService<IAmbientContext>();
            using var _ = ambient.Push(job.UserId, job.CompanyId, role);

            job.MarkRunning();
            await db.SaveChangesAsync(ct);

            try
            {
                // Ambient bağlam ayarlandığı için QueryService tıpkı HTTP
                // isteğindeki gibi davranır (kota, izolasyon, ölçüm, denetim).
                var queryService = scope.ServiceProvider.GetRequiredService<IQueryService>();

                var timeout = TimeSpan.FromSeconds(_config.GetValue("Query:JobTimeoutSeconds", 180));
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(timeout);

                var result = await queryService.ExecuteQueryAsync(
                    job.ProjectId, job.DataSourceId, job.Question, timeoutCts.Token);

                // Sonucun kendisi QueryHistory'de; işe yalnızca referans yazılır
                var historyId = await db.QueryHistories.IgnoreQueryFilters()
                    .Where(h => h.ProjectId == job.ProjectId && h.UserId == job.UserId)
                    .OrderByDescending(h => h.Id)
                    .Select(h => h.Id)
                    .FirstOrDefaultAsync(ct);

                if (result.Success)
                    job.MarkSucceeded(historyId);
                else
                    job.MarkFailed(result.ErrorMessage ?? "Sorgu başarısız oldu.");

                await db.SaveChangesAsync(ct);

                _logger.LogInformation("Sorgu işi tamamlandı: JobId={JobId}, Durum={Status}",
                    jobId, job.Status);
            }
            catch (Exception ex)
            {
                job.MarkFailed(ex.Message);
                await db.SaveChangesAsync(CancellationToken.None);
                _logger.LogError(ex, "Sorgu işi başarısız: JobId={JobId}", jobId);
            }
        }

        /// <summary>Uygulama çöktüğünde yarım kalan işleri kapat.</summary>
        private async Task AsiliIsleriTemizleAsync(DateTime esik, CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var temizlenen = await db.QueryJobs.IgnoreQueryFilters()
                    .Where(j => (j.Status == QueryJobStatuses.Pending
                              || j.Status == QueryJobStatuses.Running)
                             && j.CreatedAt < esik)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(j => j.Status, QueryJobStatuses.Failed)
                        .SetProperty(j => j.ErrorMessage, "Uygulama yeniden başlatıldığı için iş iptal edildi.")
                        .SetProperty(j => j.CompletedAt, DateTime.UtcNow), ct);

                if (temizlenen > 0)
                    _logger.LogWarning("Açılışta {Count} asılı sorgu işi iptal edildi.", temizlenen);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Asılı işler temizlenirken hata oluştu.");
            }
        }
    }
}
