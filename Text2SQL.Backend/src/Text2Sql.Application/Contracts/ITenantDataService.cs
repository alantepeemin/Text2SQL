using Text2Sql.Application.DTOs.Gdpr;

namespace Text2Sql.Application.Contracts
{
    /// <summary>
    /// SaaS-8: KVKK/GDPR yükümlülükleri.
    ///
    /// "Veri taşınabilirliği" (Art. 20) ve "silinme hakkı" (Art. 17) SaaS
    /// sözleşmelerinde zorunlu hale gelir. Bugüne kadar karşılığı yoktu:
    /// bir müşteri "verimi ver" veya "hesabımı tamamen sil" dediğinde
    /// elle SQL yazmak gerekiyordu.
    /// </summary>
    public interface ITenantDataService
    {
        /// <summary>Organizasyonun tüm verisini makine-okunur biçimde ihraç eder.</summary>
        Task<TenantExportDto> ExportAsync(CancellationToken ct = default);

        /// <summary>
        /// Organizasyonu ve TÜM verisini kalıcı olarak siler (geri alınamaz).
        /// Onay metni doğrulanır — yanlışlıkla çağrılmaya karşı koruma.
        /// </summary>
        Task DeleteTenantAsync(string confirmationText, CancellationToken ct = default);
    }
}
