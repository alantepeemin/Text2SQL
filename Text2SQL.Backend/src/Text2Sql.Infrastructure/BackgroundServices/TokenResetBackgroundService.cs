using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Text2Sql.Infrastructure.Persistence;

namespace Text2Sql.Infrastructure.BackgroundServices
{
    /// <summary>
    /// Her ayın ilk günü UTC 00:00'da kullanıcı token limitlerini sıfırlar.
    ///
    /// Düzeltilen sorunlar:
    /// 1. DateTime.Now yerine DateTime.UtcNow tutarlı olarak kullanılıyor.
    /// 2. Timer 30 gün sabit periyot yerine, her çalışmada bir sonraki ay başını hesaplıyor.
    /// 3. Uygulama restart'ında doğru zamanlama korunuyor.
    /// </summary>
    public class TokenResetBackgroundService : IHostedService, IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TokenResetBackgroundService> _logger;
        private Timer? _timer;

        public TokenResetBackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<TokenResetBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            ScheduleNextRun();
            return Task.CompletedTask;
        }

        private void ScheduleNextRun()
        {
            var now       = DateTime.UtcNow;                                      // ✅ UTC
            var nextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc)
                                .AddMonths(1);
            var delay = nextMonth - now;

            // Negatif delay olmasın (race condition koruması)
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            // Timer sadece bir kez tetikler; DoWork içinde kendini yeniden planlar
            _timer?.Dispose();
            _timer = new Timer(DoWork, null, delay, Timeout.InfiniteTimeSpan); // ✅ InfiniteTimeSpan

            _logger.LogInformation(
                "Token sıfırlama planlandı. Sonraki çalışma: {NextRun} UTC",
                nextMonth.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private async void DoWork(object? state)
        {
            _logger.LogInformation("Token sıfırlama başladı: {Time} UTC", DateTime.UtcNow);

            try
            {
                using var scope   = _scopeFactory.CreateScope();
                var context       = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Bu ayın başını bul — bir önceki aydan beri sıfırlanmamış token'lar
                var thisMonthStart = new DateTime(
                    DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

                var tokensToReset = await context.UserTokens
                    .Include(t => t.User)
                    .Where(t => t.LastResetDate < thisMonthStart && t.User.IsActive)
                    .ToListAsync();

                if (tokensToReset.Count > 0)
                {
                    foreach (var token in tokensToReset)
                    {
                        token.RemainingTokens = token.MonthlyLimit;
                        token.LastResetDate   = DateTime.UtcNow; // ✅ UTC
                        token.UpdatedAt       = DateTime.UtcNow;
                    }

                    await context.SaveChangesAsync();
                    _logger.LogInformation(
                        "Token sıfırlama tamamlandı: {Count} kullanıcı sıfırlandı.",
                        tokensToReset.Count);
                }
                else
                {
                    _logger.LogInformation("Sıfırlanacak token bulunamadı.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token sıfırlama sırasında hata oluştu.");
            }
            finally
            {
                // ✅ Bir sonraki ay başını yeniden planla
                ScheduleNextRun();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        public void Dispose() => _timer?.Dispose();
    }
}
