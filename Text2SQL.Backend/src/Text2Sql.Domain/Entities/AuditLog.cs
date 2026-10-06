using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// SaaS-3: Append-only denetim kaydı. "Kim, ne zaman, neyi değiştirdi?"
    ///
    /// Neden gerekli: kurumsal satışta ve olay müdahalesinde vazgeçilmez.
    /// "Bu veri kaynağını kim sildi?" sorusu bugüne kadar cevapsızdı.
    ///
    /// Tasarım kuralları:
    /// - SADECE INSERT. Güncelleme/silme yolu yok (setter'lar init-only değil ama
    ///   hiçbir servis bu entity'yi güncellemez; saklama süresi işi ayrı çalışır).
    /// - CompanyId = 0 → platform düzeyi olay (henüz kiracı bağlamı yok, ör. başarısız
    ///   giriş denemesi). Global filtre sayesinde bu satırlar kiracılara görünmez.
    /// </summary>
    public class AuditLog : ITenantScoped
    {
        public long Id { get; set; }

        public int CompanyId { get; set; }

        public int? ActorUserId { get; set; }
        public string? ActorEmail { get; set; }

        /// <summary>Nokta ile ayrılmış olay adı — bkz. AuditActions.</summary>
        public string Action { get; set; } = string.Empty;

        public string? TargetType { get; set; }   // "Membership", "DataSource", "Project"...
        public string? TargetId { get; set; }

        /// <summary>İnsan tarafından okunabilir özet (hassas veri İÇERMEZ).</summary>
        public string? Summary { get; set; }

        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }

        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    }
}
