using Text2Sql.Application.DTOs.Usage;

namespace Text2Sql.Application.Contracts
{
    /// <summary>SaaS-3: Kullanım ve denetim okuma modeli (read model — CQRS pragmatizmi).</summary>
    public interface IUsageService
    {
        Task<UsageSummaryDto> GetCurrentPeriodUsageAsync(CancellationToken ct = default);
        Task<List<AuditLogDto>> GetAuditLogsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);
    }

    /// <summary>
    /// SaaS-3: LLM maliyet hesaplayıcı. Fiyatlar config'ten okunur
    /// (Usage:Pricing:{model}:{InputPer1M|OutputPer1M}) — sağlayıcı fiyat
    /// değiştirdiğinde kod değişmez.
    /// </summary>
    public interface ILlmCostCalculator
    {
        decimal Estimate(string? model, int promptTokens, int completionTokens);
    }

    /// <summary>
    /// SaaS-3: Organizasyon düzeyi token kotası.
    /// Company.MonthlyQueryLimit bugüne kadar HİÇ uygulanmıyordu (ADR-005);
    /// burada gerçek bir limite dönüşüyor.
    /// </summary>
    public interface IUsageQuotaService
    {
        /// <summary>Limit aşıldıysa QuotaExceededException fırlatır.</summary>
        Task EnsureOrganizationQuotaAsync(int companyId, CancellationToken ct = default);

        /// <summary>Aylık token limiti (0 = sınırsız). Okuma modeli de bunu kullanır.</summary>
        Task<long> GetMonthlyTokenLimitAsync(int companyId, CancellationToken ct = default);
    }
}
