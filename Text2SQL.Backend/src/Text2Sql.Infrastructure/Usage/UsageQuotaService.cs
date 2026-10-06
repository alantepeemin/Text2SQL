using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Text2Sql.Application.Services;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Infrastructure.Usage
{
    /// <summary>
    /// SaaS-3: Organizasyon düzeyi TOKEN kotası.
    ///
    /// Kullanıcı düzeyi kota (UserToken) korunuyor; bu servis onun üstüne
    /// organizasyon havuzunu ekler. Limit kaynağı bugün Company.MonthlyQueryLimit
    /// (× TokensPerQuery katsayısı); SaaS-5'te Plan'dan gelecek.
    ///
    /// Not: Toplam, indeksli (CompanyId, OccurredAt) sorgusuyla anlık hesaplanıyor.
    /// Hacim büyürse UsageRollup tablosu eklenecek — okuma modeli hazır.
    /// </summary>
    public sealed class UsageQuotaService : IUsageQuotaService
    {
        private readonly IAppDbContext _context;
        private readonly IConfiguration _config;
        private readonly PlanService _planService;

        public UsageQuotaService(IAppDbContext context, IConfiguration config, PlanService planService)
        {
            _context = context;
            _config = config;
            _planService = planService;
        }

        public async Task EnsureOrganizationQuotaAsync(int companyId, CancellationToken ct = default)
        {
            var limit = await GetMonthlyTokenLimitAsync(companyId, ct);
            if (limit <= 0) return; // 0 = sınırsız

            var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            var used = await _context.UsageRecords
                .Where(u => u.CompanyId == companyId && u.OccurredAt >= periodStart)
                .SumAsync(u => (long)u.PromptTokens + u.CompletionTokens, ct);

            if (used >= limit)
                throw new QuotaExceededException(
                    "Organizasyonunuzun aylık token limiti dolmuştur. Limitinizi yükseltin veya ay başını bekleyin.");
        }

        /// <summary>
        /// SaaS-5: Token limiti artık PLANDAN gelir.
        ///
        /// Öncelik sırası:
        /// 1) Aktif abonelik planının MonthlyTokenLimit'i (0 = sınırsız),
        /// 2) Abonelik yoksa geriye uyumlu hesap:
        ///    Company.MonthlyQueryLimit × Usage:TokensPerQueryAllowance.
        ///
        /// (2) bilinçli olarak korunuyor: abonelik seed edilmemiş eski
        /// organizasyonlar aniden sınırsız hale gelmesin.
        /// </summary>
        public async Task<long> GetMonthlyTokenLimitAsync(int companyId, CancellationToken ct = default)
        {
            var plan = await _planService.GetEffectivePlanAsync(ct);
            if (plan != null)
                return plan.MonthlyTokenLimit;

            var tokensPerQuery = _config.GetValue("Usage:TokensPerQueryAllowance", 3000);

            var queryLimit = await _context.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Id == companyId)
                .Select(c => c.MonthlyQueryLimit)
                .FirstOrDefaultAsync(ct);

            return (long)queryLimit * tokensPerQuery;
        }
    }
}
