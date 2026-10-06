using Microsoft.EntityFrameworkCore;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Usage;

namespace Text2Sql.Application.Services
{
    /// <summary>
    /// SaaS-3: Kullanım ve denetim OKUMA modeli.
    /// Yazma yolundan ayrı tutuldu (CQRS pragmatizmi): projeksiyon + AsNoTracking,
    /// ileride materyalize görünüme geçmek yalnızca bu sınıfı etkiler.
    /// Global kiracı filtresi sayesinde tüm sorgular otomatik olarak
    /// aktif organizasyona sınırlıdır.
    /// </summary>
    public class UsageService : IUsageService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IUsageQuotaService _quota;

        public UsageService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IUsageQuotaService quota)
        {
            _context = context;
            _currentUser = currentUser;
            _quota = quota;
        }

        public async Task<UsageSummaryDto> GetCurrentPeriodUsageAsync(CancellationToken ct = default)
        {
            var now         = DateTime.UtcNow;
            var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var periodEnd   = periodStart.AddMonths(1);

            var kayitlar = await _context.UsageRecords
                .AsNoTracking()
                .Where(u => u.OccurredAt >= periodStart && u.OccurredAt < periodEnd)
                .Select(u => new
                {
                    u.UserId, u.Model, u.PromptTokens, u.CompletionTokens,
                    u.EstimatedCostUsd, u.IsSuccessful
                })
                .ToListAsync(ct);

            var kullaniciAdlari = await _context.Users
                .AsNoTracking()
                .Where(u => kayitlar.Select(k => k.UserId).Contains(u.Id))
                .Select(u => new { u.Id, u.Username })
                .ToDictionaryAsync(u => u.Id, u => u.Username, ct);

            long promptTokens     = kayitlar.Sum(k => (long)k.PromptTokens);
            long completionTokens = kayitlar.Sum(k => (long)k.CompletionTokens);
            long toplamToken      = promptTokens + completionTokens;

            var limit = _currentUser.HasTenant
                ? await GetLimitAsync(ct)
                : 0;

            return new UsageSummaryDto
            {
                OrganizationId       = _currentUser.TenantId,
                PeriodStart          = periodStart,
                PeriodEnd            = periodEnd,
                QueryCount           = kayitlar.Count,
                SuccessfulQueryCount = kayitlar.Count(k => k.IsSuccessful),
                PromptTokens         = promptTokens,
                CompletionTokens     = completionTokens,
                TotalTokens          = toplamToken,
                EstimatedCostUsd     = kayitlar.Sum(k => k.EstimatedCostUsd),
                MonthlyTokenLimit    = limit,
                RemainingTokens      = limit > 0 ? Math.Max(0, limit - toplamToken) : 0,
                ByUser = kayitlar
                    .GroupBy(k => k.UserId)
                    .Select(g => new UsageByUserDto
                    {
                        UserId           = g.Key,
                        Username         = kullaniciAdlari.TryGetValue(g.Key, out var ad) ? ad : "?",
                        QueryCount       = g.Count(),
                        TotalTokens      = g.Sum(x => (long)x.PromptTokens + x.CompletionTokens),
                        EstimatedCostUsd = g.Sum(x => x.EstimatedCostUsd)
                    })
                    .OrderByDescending(x => x.TotalTokens)
                    .ToList(),
                ByModel = kayitlar
                    .GroupBy(k => k.Model ?? "bilinmiyor")
                    .Select(g => new UsageByModelDto
                    {
                        Model            = g.Key,
                        QueryCount       = g.Count(),
                        TotalTokens      = g.Sum(x => (long)x.PromptTokens + x.CompletionTokens),
                        EstimatedCostUsd = g.Sum(x => x.EstimatedCostUsd)
                    })
                    .OrderByDescending(x => x.TotalTokens)
                    .ToList()
            };
        }

        public async Task<List<AuditLogDto>> GetAuditLogsAsync(
            int page = 1, int pageSize = 50, CancellationToken ct = default)
        {
            page     = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            return await _context.AuditLogs
                .AsNoTracking()
                .OrderByDescending(a => a.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogDto
                {
                    Id         = a.Id,
                    Action     = a.Action,
                    ActorEmail = a.ActorEmail,
                    TargetType = a.TargetType,
                    TargetId   = a.TargetId,
                    Summary    = a.Summary,
                    IpAddress  = a.IpAddress,
                    OccurredAt = a.OccurredAt
                })
                .ToListAsync(ct);
        }

        private Task<long> GetLimitAsync(CancellationToken ct)
            => _quota.GetMonthlyTokenLimitAsync(_currentUser.TenantId, ct);
    }
}
