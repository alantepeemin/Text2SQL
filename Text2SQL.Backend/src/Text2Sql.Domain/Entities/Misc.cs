using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    public class QueryHistory : ITenantScoped
    {
        public int Id { get; set; }

        // SaaS-1: Kiracı filtresi + kullanım raporlaması (SaaS-3) için denormalize
        public int CompanyId { get; set; }

        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        // Hangi veritabanında çalıştırıldığı
        public int? DatabaseId { get; set; }
        public ProjectDatabase? Database { get; set; }

        public string Question { get; set; } = string.Empty;
        public string? SqlQuery { get; set; }

        // Sonuç satırları JSON formatında (Faz 4 öncesi kayıtlar + dosya yazımı
        // başarısız olursa fallback). Yeni kayıtlar ResultPath kullanır.
        public string? Result { get; set; }

        // Faz 4: sonuç gövdesi dosya deposunda — DB şişmez (teknik borç #9)
        public string? ResultPath { get; set; }

        // Sütun adları JSON formatında — geçmiş görüntülemede kullanılır
        public string? Columns { get; set; }

        public int ExecutionTimeMs { get; set; }
        public int TokensConsumed { get; set; } = 1;

        public bool IsSuccessful { get; set; } = false;
        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class UserToken
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int RemainingTokens { get; set; } = 100;
        public int MonthlyLimit { get; set; } = 100;
        public DateTime LastResetDate { get; set; } = DateTime.UtcNow;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// JWT refresh token — access token süresi dolduğunda yeni token almak için kullanılır.
    /// </summary>
    public class RefreshToken
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public string Token { get; set; } = string.Empty;

        // SaaS-2: Token'ın verildiği aktif organizasyon. Yenilemede aynı
        // organizasyon bağlamı korunur (null = birincil üyelik seçilir).
        public int? CompanyId { get; set; }

        public DateTime ExpiresAt { get; set; }
        public bool IsRevoked { get; set; } = false;
        public DateTime? RevokedAt { get; set; }
        public string? RevokedReason { get; set; }

        public string? CreatedByIp { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // ── SaaS-7: Token ailesi (yeniden kullanım tespiti) ──────────────────
        // Rotasyonla üretilen her token aynı "aile" kimliğini taşır. İptal
        // edilmiş bir token TEKRAR kullanılırsa, saldırgan eski token'ı ele
        // geçirmiş demektir → TÜM AİLE iptal edilir (oturum sonlandırılır).
        // Bu, OAuth 2.0 BCP'de önerilen refresh token reuse detection desenidir.
        public Guid FamilyId { get; set; } = Guid.NewGuid();

        /// <summary>Bu token'ın yerine geçen token (izleme ve teşhis için).</summary>
        public int? ReplacedByTokenId { get; set; }

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public bool IsActive => !IsRevoked && !IsExpired;
    }
}
