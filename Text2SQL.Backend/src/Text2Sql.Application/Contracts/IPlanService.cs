using Text2Sql.Application.DTOs.Billing;

namespace Text2Sql.Application.Contracts
{
    /// <summary>
    /// SaaS-5: Özellik kapısı — "bu organizasyonun paketi bu özelliği içeriyor mu?"
    ///
    /// İzin kontrolünden (SaaS-4) bağımsız ve ONA EK bir katmandır:
    /// yetkili bir admin, planı kapsamıyorsa uzak veri kaynağı ekleyemez.
    /// Sonuç kod içinde 60 sn cache'lenir (her istekte plan sorgusu atmamak için).
    /// </summary>
    public interface IFeatureGate
    {
        Task<bool> IsEnabledAsync(string featureKey, CancellationToken ct = default);

        /// <summary>Kapalıysa FeatureNotAvailableException fırlatır (HTTP 402).</summary>
        Task EnsureEnabledAsync(string featureKey, CancellationToken ct = default);
    }

    /// <summary>SaaS-5: Plan katalogu ve abonelik yönetimi.</summary>
    public interface IPlanService
    {
        Task<List<PlanDto>> GetPlanCatalogAsync(CancellationToken ct = default);
        Task<SubscriptionDto> GetCurrentSubscriptionAsync(CancellationToken ct = default);
        Task<SubscriptionDto> ChangePlanAsync(string planCode, CancellationToken ct = default);

        /// <summary>Yeni organizasyon için varsayılan (ücretsiz) abonelik oluşturur.</summary>
        Task EnsureDefaultSubscriptionAsync(int companyId, CancellationToken ct = default);
    }

    /// <summary>
    /// SaaS-5: Plan limitlerinin uygulanması (üye/proje/veri kaynağı/anahtar sayısı).
    /// Token limiti UsageQuotaService'te — o zaten kullanım verisine bakıyor.
    /// </summary>
    public interface IPlanLimitService
    {
        Task EnsureCanAddMemberAsync(CancellationToken ct = default);
        Task EnsureCanAddProjectAsync(CancellationToken ct = default);
        Task EnsureCanAddDataSourceAsync(CancellationToken ct = default);
        Task EnsureCanAddApiKeyAsync(CancellationToken ct = default);
    }
}
