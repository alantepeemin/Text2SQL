using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Text2Sql.Application.Common;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Billing;
using Text2Sql.Domain.Billing;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Application.Services
{
    /// <summary>SaaS-5: Etkin plan bilgisi — cache'lenen hafif görünüm.</summary>
    public sealed record EffectivePlan(
        int PlanId,
        string PlanCode,
        string PlanName,
        string SubscriptionStatus,
        bool IsServiceable,
        long MonthlyTokenLimit,
        int MaxMembers,
        int MaxProjects,
        int MaxDataSources,
        int MaxApiKeys,
        IReadOnlySet<string> Features);

    public class PlanService : IPlanService, IFeatureGate, IPlanLimitService
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IAuditWriter _audit;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PlanService> _logger;

        public PlanService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IAuditWriter audit,
            IMemoryCache cache,
            ILogger<PlanService> logger)
        {
            _context = context;
            _currentUser = currentUser;
            _audit = audit;
            _cache = cache;
            _logger = logger;
        }

        // ── IFeatureGate ─────────────────────────────────────────────────────

        public async Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default)
        {
            var plan = await GetEffectivePlanAsync(ct);
            return plan != null && plan.IsServiceable && plan.Features.Contains(featureKey);
        }

        public async Task EnsureEnabledAsync(string featureKey, CancellationToken ct = default)
        {
            var plan = await GetEffectivePlanAsync(ct);

            if (plan == null)
                throw new FeatureNotAvailableException(
                    "Organizasyonunuzun aktif bir aboneliği bulunmuyor.");

            if (!plan.IsServiceable)
                throw new FeatureNotAvailableException(
                    $"Aboneliğiniz '{plan.SubscriptionStatus}' durumunda; bu işlem kullanılamıyor.");

            if (!plan.Features.Contains(featureKey))
                throw new FeatureNotAvailableException(
                    $"Bu özellik '{plan.PlanName}' paketinde bulunmuyor. Paketinizi yükseltin.");
        }

        // ── IPlanLimitService ────────────────────────────────────────────────

        public Task EnsureCanAddMemberAsync(CancellationToken ct = default)
            => LimitKontrolAsync(
                p => p.MaxMembers,
                () => _context.Memberships.CountAsync(m => m.IsActive, ct),
                "üye", ct);

        public Task EnsureCanAddProjectAsync(CancellationToken ct = default)
            => LimitKontrolAsync(
                p => p.MaxProjects,
                () => _context.Projects.CountAsync(p => p.IsActive, ct),
                "proje", ct);

        public Task EnsureCanAddDataSourceAsync(CancellationToken ct = default)
            => LimitKontrolAsync(
                p => p.MaxDataSources,
                () => _context.ProjectDatabases.CountAsync(d => d.IsActive, ct),
                "veri kaynağı", ct);

        public Task EnsureCanAddApiKeyAsync(CancellationToken ct = default)
            => LimitKontrolAsync(
                p => p.MaxApiKeys,
                () => _context.ApiKeys.CountAsync(k => k.RevokedAt == null, ct),
                "API anahtarı", ct);

        private async Task LimitKontrolAsync(
            Func<EffectivePlan, int> limitSecici,
            Func<Task<int>> mevcutSayimi,
            string kaynakAdi,
            CancellationToken ct)
        {
            var plan = await GetEffectivePlanAsync(ct);
            if (plan == null) return; // abonelik yoksa limit uygulanmaz (geriye uyumluluk)

            var limit = limitSecici(plan);
            if (limit <= 0) return;   // 0 = sınırsız

            var mevcut = await mevcutSayimi();
            if (mevcut >= limit)
                throw new PlanLimitExceededException(
                    $"'{plan.PlanName}' paketinizin {kaynakAdi} limiti ({limit}) dolmuş. Paketinizi yükseltin.");
        }

        // ── IPlanService ─────────────────────────────────────────────────────

        public async Task<List<PlanDto>> GetPlanCatalogAsync(CancellationToken ct = default)
        {
            var mevcutPlan = await GetEffectivePlanAsync(ct);

            var planlar = await _context.Plans
                .AsNoTracking()
                .Include(p => p.Features)
                .Where(p => p.IsActive)
                .OrderBy(p => p.SortOrder)
                .ToListAsync(ct);

            return planlar.Select(p => new PlanDto
            {
                Code              = p.Code,
                Name              = p.Name,
                MonthlyTokenLimit = p.MonthlyTokenLimit,
                MaxMembers        = p.MaxMembers,
                MaxProjects       = p.MaxProjects,
                MaxDataSources    = p.MaxDataSources,
                MaxApiKeys        = p.MaxApiKeys,
                MonthlyPriceUsd   = p.MonthlyPriceUsd,
                Features          = p.Features.Where(f => f.IsEnabled).Select(f => f.FeatureKey).ToList(),
                IsCurrent         = mevcutPlan != null && mevcutPlan.PlanCode == p.Code
            }).ToList();
        }

        public async Task<SubscriptionDto> GetCurrentSubscriptionAsync(CancellationToken ct = default)
        {
            var subscription = await AktifAbonelikGetirAsync(ct)
                ?? throw new NotFoundException("Organizasyonunuzun aboneliği bulunamadı.");

            return new SubscriptionDto
            {
                PlanCode           = subscription.Plan.Code,
                PlanName           = subscription.Plan.Name,
                Status             = subscription.Status,
                CurrentPeriodStart = subscription.CurrentPeriodStart,
                CurrentPeriodEnd   = subscription.CurrentPeriodEnd,
                TrialEndsAt        = subscription.TrialEndsAt,
                IsServiceable      = subscription.IsServiceable,
                Features           = subscription.Plan.Features
                                        .Where(f => f.IsEnabled)
                                        .Select(f => f.FeatureKey).ToList(),
                Limits = new PlanLimitsDto
                {
                    MonthlyTokenLimit  = subscription.Plan.MonthlyTokenLimit,
                    MaxMembers         = subscription.Plan.MaxMembers,
                    MaxProjects        = subscription.Plan.MaxProjects,
                    MaxDataSources     = subscription.Plan.MaxDataSources,
                    MaxApiKeys         = subscription.Plan.MaxApiKeys,
                    CurrentMembers     = await _context.Memberships.CountAsync(m => m.IsActive, ct),
                    CurrentProjects    = await _context.Projects.CountAsync(p => p.IsActive, ct),
                    CurrentDataSources = await _context.ProjectDatabases.CountAsync(d => d.IsActive, ct),
                    CurrentApiKeys     = await _context.ApiKeys.CountAsync(k => k.RevokedAt == null, ct)
                }
            };
        }

        public async Task<SubscriptionDto> ChangePlanAsync(string planCode, CancellationToken ct = default)
        {
            var yeniPlan = await _context.Plans
                .FirstOrDefaultAsync(p => p.Code == planCode && p.IsActive, ct)
                ?? throw new NotFoundException($"'{planCode}' planı bulunamadı.");

            var subscription = await AktifAbonelikGetirAsync(ct)
                ?? throw new NotFoundException("Organizasyonunuzun aboneliği bulunamadı.");

            var eskiPlanKodu = subscription.Plan.Code;
            if (eskiPlanKodu == planCode)
                return await GetCurrentSubscriptionAsync(ct);

            // NOT: Plan düşürmede mevcut kullanım yeni limitin üstünde olabilir.
            // Bilinçli karar: mevcut kaynaklar SİLİNMEZ (veri kaybı olmaz),
            // ancak YENİ ekleme limitle engellenir. Müşteriyi cezalandırmadan
            // doğru davranışa yönlendiren yaklaşım budur.
            subscription.ChangePlan(yeniPlan.Id);

            _audit.Write(new AuditEvent(AuditActions.PlanChanged,
                TargetType: "Subscription", TargetId: subscription.Id.ToString(),
                Summary: $"Plan değişti: {eskiPlanKodu} → {planCode}"));

            await _context.SaveChangesAsync(ct);
            _cache.Remove(CacheKeys.Plan(_currentUser.TenantId)); // limitler anında etkili

            _logger.LogInformation("Plan değişti: Tenant={Tenant}, {Old} → {New}",
                _currentUser.TenantId, eskiPlanKodu, planCode);

            return await GetCurrentSubscriptionAsync(ct);
        }

        public async Task EnsureDefaultSubscriptionAsync(int companyId, CancellationToken ct = default)
        {
            // Kayıt akışında çağrılır — henüz kiracı bağlamı yok, bu yüzden
            // filtreyi atlayıp companyId'yi explicit veriyoruz.
            var varMi = await _context.Subscriptions
                .IgnoreQueryFilters()
                .AnyAsync(s => s.CompanyId == companyId, ct);
            if (varMi) return;

            var freePlan = await _context.Plans
                .FirstOrDefaultAsync(p => p.Code == PlanCodes.Free, ct);

            if (freePlan == null)
            {
                // Plan katalogu seed edilmemişse abonelik oluşturulamaz; sistem
                // yine çalışır (limit/özellik kontrolleri geriye uyumlu davranır).
                _logger.LogWarning("Free plan bulunamadı — organizasyon {Company} aboneliksiz oluşturuldu.", companyId);
                return;
            }

            _context.Subscriptions.Add(new Subscription
            {
                CompanyId          = companyId,
                PlanId             = freePlan.Id,
                Status             = SubscriptionStatuses.Active,
                CurrentPeriodStart = DateTime.UtcNow,
                CurrentPeriodEnd   = DateTime.UtcNow.AddMonths(1),
                CreatedAt          = DateTime.UtcNow,
                UpdatedAt          = DateTime.UtcNow
            });
        }

        // ── Yardımcılar ──────────────────────────────────────────────────────

        private async Task<Subscription?> AktifAbonelikGetirAsync(CancellationToken ct)
            => await _context.Subscriptions
                .Include(s => s.Plan).ThenInclude(p => p.Features)
                .Where(s => s.Status != SubscriptionStatuses.Canceled)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync(ct);

        /// <summary>Etkin plan — 60 sn cache'lenir (her istekte JOIN'li sorgu atmamak için).</summary>
        public async Task<EffectivePlan?> GetEffectivePlanAsync(CancellationToken ct = default)
        {
            if (!_currentUser.HasTenant) return null;

            var cacheKey = CacheKeys.Plan(_currentUser.TenantId);
            if (_cache.TryGetValue(cacheKey, out EffectivePlan? cached))
                return cached;

            var subscription = await AktifAbonelikGetirAsync(ct);
            if (subscription == null) return null;

            var effective = new EffectivePlan(
                subscription.PlanId,
                subscription.Plan.Code,
                subscription.Plan.Name,
                subscription.Status,
                subscription.IsServiceable,
                subscription.Plan.MonthlyTokenLimit,
                subscription.Plan.MaxMembers,
                subscription.Plan.MaxProjects,
                subscription.Plan.MaxDataSources,
                subscription.Plan.MaxApiKeys,
                subscription.Plan.Features.Where(f => f.IsEnabled)
                    .Select(f => f.FeatureKey).ToHashSet(StringComparer.OrdinalIgnoreCase));

            _cache.Set(cacheKey, effective, CacheTtl);
            return effective;
        }
    }
}
