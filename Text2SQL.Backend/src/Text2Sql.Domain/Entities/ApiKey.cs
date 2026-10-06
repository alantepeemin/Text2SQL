using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// SaaS-6: Programatik erişim anahtarı.
    ///
    /// Neden gerekli: bugüne kadar entegrasyon yapmak isteyen müşteri kendi
    /// KULLANICI JWT'sini script'e gömmek zorundaydı — iptal edilemez, izlenemez,
    /// kişiye bağlı bir kimlik paylaşımı. Güvenlik incelemesinde 🔴 kritik (S2).
    ///
    /// Güvenlik tasarımı:
    /// - Anahtarın kendisi ASLA saklanmaz; yalnızca SHA-256 özeti tutulur.
    /// - Görüntüleme için ilk 12 karakter (Prefix) ayrıca saklanır — kullanıcı
    ///   listede hangi anahtar olduğunu tanır, tam değer bir daha gösterilmez.
    /// - Yetki, kullanıcı rolünden DEĞİL anahtarın kendi scope'larından gelir
    ///   (en-az-yetki: sadece sorgu çalıştıran bir anahtar üye yönetemez).
    /// </summary>
    public class ApiKey : ITenantScoped
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        public string Name { get; set; } = string.Empty;

        /// <summary>SHA-256(hex) — tam anahtar hiçbir yerde saklanmaz.</summary>
        public string KeyHash { get; set; } = string.Empty;

        /// <summary>Görüntüleme öneki (ör. "t2s_live_ab1"). Aramada da kullanılır.</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Virgülle ayrılmış izin adları (bkz. Permissions).</summary>
        public string Scopes { get; set; } = string.Empty;

        public int CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastUsedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }

        public DateTime? RevokedAt { get; set; }
        public int? RevokedByUserId { get; set; }

        // ── Davranış ─────────────────────────────────────────────────────────

        public bool IsRevoked => RevokedAt != null;
        public bool IsExpired => ExpiresAt != null && ExpiresAt <= DateTime.UtcNow;
        public bool IsActive  => !IsRevoked && !IsExpired;

        public IReadOnlyList<string> ScopeList =>
            string.IsNullOrWhiteSpace(Scopes)
                ? Array.Empty<string>()
                : Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public bool HasScope(string permission) =>
            ScopeList.Contains(permission, StringComparer.OrdinalIgnoreCase);

        public void Revoke(int byUserId)
        {
            if (IsRevoked) return;
            RevokedAt = DateTime.UtcNow;
            RevokedByUserId = byUserId;
        }
    }
}
