using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Text2Sql.Application.Contracts;
using Text2Sql.Infrastructure.Persistence;

namespace Text2Sql.Infrastructure.BackgroundServices
{
    /// <summary>
    /// SaaS-8: Veri yaşam döngüsü — saklama süresi dolan kayıtları temizler.
    ///
    /// Neden gerekli: sorgu geçmişi, ölçüm kayıtları ve sonuç dosyaları
    /// SINIRSIZ büyüyordu. Bu hem maliyet hem KVKK/GDPR sorunudur
    /// ("gerektiğinden uzun süre saklama" ihlali).
    ///
    /// Tasarım kararları:
    /// - Saklama süreleri config'ten, farklı veri tipleri için AYRI:
    ///   sorgu geçmişi kısa, denetim kaydı uzun (uyum gereksinimi), ölçüm orta.
    /// - Denetim kaydı için varsayılan 365 gün — audit'i erken silmek uyum
    ///   açısından risklidir; bilinçli olarak en uzun süre verildi.
    /// - Silme PARTİLİ (batch) yapılır: tek dev DELETE tablo kilitleyebilir.
    /// - Kiracı filtresi devre dışıdır (arka plan işi tüm kiracılar için çalışır),
    ///   bu yüzden HTTP bağlamı olmayan kendi scope'unu kullanır.
    /// </summary>
    public class DataRetentionBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<DataRetentionBackgroundService> _logger;

        public DataRetentionBackgroundService(
            IServiceScopeFactory scopeFactory,
            IConfiguration config,
            ILogger<DataRetentionBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.GetValue("Retention:Enabled", true))
            {
                _logger.LogInformation("Veri saklama temizliği devre dışı (Retention:Enabled=false).");
                return;
            }

            var interval = TimeSpan.FromHours(_config.GetValue("Retention:RunEveryHours", 24));

            // İlk çalıştırma açılıştan hemen sonra değil — uygulama ısınsın.
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

            using var timer = new PeriodicTimer(interval);
            do
            {
                try
                {
                    await TemizlikYapAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // Arka plan işi hatası uygulamayı düşürmemeli
                    _logger.LogError(ex, "Veri saklama temizliği sırasında hata oluştu.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task TemizlikYapAsync(CancellationToken ct)
        {
            var queryHistoryDays = _config.GetValue("Retention:QueryHistoryDays", 90);
            var usageDays        = _config.GetValue("Retention:UsageRecordDays", 400);
            var auditDays        = _config.GetValue("Retention:AuditLogDays", 365);
            var batchSize        = _config.GetValue("Retention:BatchSize", 5000);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

            // ── 1. Sorgu geçmişi + sonuç dosyaları ──
            if (queryHistoryDays > 0)
            {
                var esik = DateTime.UtcNow.AddDays(-queryHistoryDays);

                // Dosyalar önce silinir: DB kaydı gidince yol bilgisi kaybolur
                // ve dosyalar "yetim" kalırdı.
                var yollar = await db.QueryHistories
                    .IgnoreQueryFilters()
                    .Where(h => h.CreatedAt < esik && h.ResultPath != null)
                    .Select(h => h.ResultPath!)
                    .Take(batchSize)
                    .ToListAsync(ct);

                foreach (var yol in yollar)
                {
                    try { await storage.DeleteAsync(yol, ct); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Sonuç dosyası silinemedi: {Path}", yol); }
                }

                var silinen = await db.QueryHistories
                    .IgnoreQueryFilters()
                    .Where(h => h.CreatedAt < esik)
                    .Take(batchSize)
                    .ExecuteDeleteAsync(ct);

                if (silinen > 0)
                    _logger.LogInformation(
                        "Saklama temizliği: {Count} sorgu geçmişi kaydı ve {Files} sonuç dosyası silindi ({Days} günden eski).",
                        silinen, yollar.Count, queryHistoryDays);
            }

            // ── 2. Ölçüm kayıtları (faturalama itirazı süresi için daha uzun) ──
            if (usageDays > 0)
            {
                var esik = DateTime.UtcNow.AddDays(-usageDays);
                var silinen = await db.UsageRecords
                    .IgnoreQueryFilters()
                    .Where(u => u.OccurredAt < esik)
                    .Take(batchSize)
                    .ExecuteDeleteAsync(ct);

                if (silinen > 0)
                    _logger.LogInformation("Saklama temizliği: {Count} ölçüm kaydı silindi.", silinen);
            }

            // ── 3. Denetim kaydı (en uzun süre — uyum gereksinimi) ──
            if (auditDays > 0)
            {
                var esik = DateTime.UtcNow.AddDays(-auditDays);
                var silinen = await db.AuditLogs
                    .IgnoreQueryFilters()
                    .Where(a => a.OccurredAt < esik)
                    .Take(batchSize)
                    .ExecuteDeleteAsync(ct);

                if (silinen > 0)
                    _logger.LogInformation("Saklama temizliği: {Count} denetim kaydı silindi.", silinen);
            }

            // ── 4. Süresi dolmuş refresh token'lar ve bekleyen kayıtlar ──
            var tokenSilinen = await db.RefreshTokens
                .Where(r => r.ExpiresAt < DateTime.UtcNow.AddDays(-30))
                .Take(batchSize)
                .ExecuteDeleteAsync(ct);

            var pendingSilinen = await db.PendingRegistrations
                .Where(p => p.ExpiresAt < DateTime.UtcNow)
                .ExecuteDeleteAsync(ct);

            if (tokenSilinen > 0 || pendingSilinen > 0)
                _logger.LogInformation(
                    "Saklama temizliği: {Tokens} refresh token, {Pending} bekleyen kayıt silindi.",
                    tokenSilinen, pendingSilinen);
        }
    }
}
