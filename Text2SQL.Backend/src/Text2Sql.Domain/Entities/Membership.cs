using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// SaaS-2: Kullanıcı ↔ Organizasyon üyeliği (N:N).
    ///
    /// Rol ve durum artık KULLANICIDA değil ÜYELİKTE yaşar: aynı kişi A
    /// organizasyonunda admin, B'de viewer olabilir. Bu, danışman/ajans
    /// müşterileri için zorunlu bir modeldir.
    ///
    /// Bu entity ITenantScoped'dır — global filtre sayesinde bir organizasyonun
    /// üye listesi başka organizasyona asla görünmez. (İstisna: kullanıcının
    /// KENDİ üyeliklerini listelemesi ve organizasyon değiştirmesi; bunlar
    /// IgnoreQueryFilters + UserId kısıtıyla yapılır — AuthService'te yorumlandı.)
    ///
    /// İş kuralları entity'ye taşındı (anemik domain düzeltmesi).
    /// </summary>
    public class Membership : ITenantScoped
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public User User { get; set; } = null!;

        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        public string Role { get; set; } = "user";       // admin | manager | user
        public string Status { get; set; } = "pending";  // pending | approved | rejected | suspended
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Kullanıcının giriş sonrası varsayılan organizasyonu.
        /// SaaS-2a'da ayrıca User'daki legacy alanların aynasıdır.
        /// </summary>
        public bool IsPrimary { get; set; }

        public string? InvitationType { get; set; }      // invitation | code | domain
        public int? InvitedBy { get; set; }
        public DateTime? InvitedAt { get; set; }

        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ApprovalNotes { get; set; }

        public DateTime? DeactivatedAt { get; set; }
        public int? DeactivatedBy { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ── Davranış (domain kuralları) ──────────────────────────────────────

        public bool IsPending   => Status == "pending";
        public bool IsApproved  => Status == "approved";
        public bool IsSuspended => Status == "suspended";

        /// <summary>Bu üyelikle organizasyona erişilebilir mi?</summary>
        public bool CanAccess => IsActive && IsApproved;

        public void Approve(int approverId, string role, string? notes = null)
        {
            if (!IsPending)
                throw new InvalidOperationException("Bu üyelik zaten işlem görmüş.");

            Status        = "approved";
            Role          = role;
            ApprovedBy    = approverId;
            ApprovedAt    = DateTime.UtcNow;
            ApprovalNotes = notes;
            IsActive      = true;
            UpdatedAt     = DateTime.UtcNow;
        }

        public void Reject(int approverId, string? notes = null)
        {
            if (!IsPending)
                throw new InvalidOperationException("Bu üyelik zaten işlem görmüş.");

            Status        = "rejected";
            ApprovedBy    = approverId;
            ApprovedAt    = DateTime.UtcNow;
            ApprovalNotes = notes;
            IsActive      = false;
            UpdatedAt     = DateTime.UtcNow;
        }

        public void Suspend(int byUserId, string? notes = null)
        {
            Status        = "suspended";
            ApprovedBy    = byUserId;
            ApprovedAt    = DateTime.UtcNow;
            ApprovalNotes = notes;
            UpdatedAt     = DateTime.UtcNow;
        }

        public void ChangeRole(string newRole)
        {
            if (!IsApproved)
                throw new InvalidOperationException("Sadece onaylı üyeliklerin rolü değiştirilebilir.");

            Role      = newRole;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Deactivate(int byUserId)
        {
            IsActive       = false;
            Status         = "suspended";
            DeactivatedAt  = DateTime.UtcNow;
            DeactivatedBy  = byUserId;
            UpdatedAt      = DateTime.UtcNow;
        }
    }
}
