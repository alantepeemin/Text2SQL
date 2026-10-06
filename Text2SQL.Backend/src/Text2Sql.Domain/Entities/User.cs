namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// SaaS-2: Kullanıcı HESABI artık organizasyondan bağımsızdır.
    /// Rol/durum bilgisi Membership'te yaşar (bir kişi A'da admin, B'de viewer olabilir).
    ///
    /// NOT: Bu entity artık ITenantScoped DEĞİLDİR — olsaydı, B organizasyonunda
    /// çalışan bir kullanıcı legacy CompanyId'si A olduğu için kendi kaydını
    /// okuyamazdı. Kiracı izolasyonu Membership üzerinden sağlanır.
    /// </summary>
    public class User
    {
        public int Id { get; set; }

        // ── SaaS-2a LEGACY (geriye uyumluluk) ────────────────────────────────
        // Kaynak-of-truth artık Membership'tir. Bu alanlar "birincil organizasyon"
        // aynası olarak korunuyor ki mevcut kod ve raporlar kırılmasın.
        // SaaS-2b'de (contract fazı) kaldırılacaktır.
        // SaaS-8 (r2): NULLABLE yapıldı. Sebep: Company → User arasındaki zorunlu
        // ilişki CASCADE üretiyordu; bir organizasyon silindiğinde, o organizasyonu
        // "birincil" olarak taşıyan kullanıcı hesapları — BAŞKA organizasyonlarda
        // üyelikleri olsa bile — siliniyordu. Çok kiracılı modelde bu kabul edilemez.
        // Alan nullable + SetNull olunca cascade zinciri kırılır.
        public int? CompanyId { get; set; }
        public Company? Company { get; set; }
        public string Role { get; set; } = "user";
        public string Status { get; set; } = "pending";
        public int? InvitedBy { get; set; }
        public User? InvitedByUser { get; set; }
        public DateTime? InvitedAt { get; set; }
        public string? InvitationType { get; set; }
        public int? ApprovedBy { get; set; }
        public User? ApprovedByUser { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ApprovalNotes { get; set; }
        // ─────────────────────────────────────────────────────────────────────

        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;

        // Oturum geçersiz kılma damgası (hesap düzeyi): parola değişimi, üyelik
        // iptali veya rol değişiminde yenilenir → tüm oturumlar düşer.
        public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime? LastActivityAt { get; set; }
        public string? LastIpAddress { get; set; }

        // ── SaaS-7: E-posta doğrulama ────────────────────────────────────────
        // Kod ile kayıtta (join-by-code) kullanıcı istediği adresi yazabiliyordu;
        // doğrulanmamış adresle sorgu çalıştırmak artık engellenebilir.
        public bool EmailConfirmed { get; set; }
        public string? EmailConfirmationToken { get; set; }
        public DateTime? EmailConfirmationSentAt { get; set; }
        public DateTime? EmailConfirmedAt { get; set; }

        // ── SaaS-7: Hesap kilitleme (yavaş brute-force koruması) ─────────────
        // Rate limit IP bazlıdır; dağıtık/yavaş saldırılarda hesap bazlı sayaç
        // gerekir. Başarılı girişte sıfırlanır.
        public int FailedLoginCount { get; set; }
        public DateTime? LockoutEndsAt { get; set; }

        // Hesap düzeyi soft delete
        public bool IsActive { get; set; } = true;
        public DateTime? DeactivatedAt { get; set; }
        public int? DeactivatedBy { get; set; }

        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
        public UserToken? TokenInfo { get; set; }
        public ICollection<ProjectAccess> ProjectAccesses { get; set; } = new List<ProjectAccess>();
        public ICollection<QueryHistory> QueryHistories { get; set; } = new List<QueryHistory>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public ICollection<CompanyInvitation> SentInvitations { get; set; } = new List<CompanyInvitation>();
        public ICollection<User> InvitedUsers { get; set; } = new List<User>();
        public ICollection<User> ApprovedUsers { get; set; } = new List<User>();

        // ── Davranış ─────────────────────────────────────────────────────────

        /// <summary>Tüm aktif oturumları (SecurityStamp cache TTL'i içinde) geçersiz kılar.</summary>
        public void InvalidateSecurityStamp() => SecurityStamp = Guid.NewGuid().ToString("N");

        /// <summary>Hesap düzeyinde giriş yapabilir mi? (Organizasyon erişimi Membership.CanAccess ile.)</summary>
        public bool CanLogin => IsActive;

        /// <summary>Hesap şu anda kilitli mi?</summary>
        public bool IsLockedOut => LockoutEndsAt != null && LockoutEndsAt > DateTime.UtcNow;

        /// <summary>
        /// Başarısız giriş kaydı. Eşik aşılırsa artan süreyle kilitlenir
        /// (5→1dk, 6→2dk, 7→4dk ... üst sınır 30 dk). Kademeli artış, meşru
        /// kullanıcıyı uzun süre dışarıda bırakmadan saldırıyı ekonomik olarak
        /// anlamsız kılar.
        /// </summary>
        public void RegisterFailedLogin(int threshold = 5, int maxLockoutMinutes = 30)
        {
            FailedLoginCount++;

            if (FailedLoginCount < threshold) return;

            var fazlaDeneme = FailedLoginCount - threshold;          // 0,1,2...
            var dakika = Math.Min(Math.Pow(2, fazlaDeneme), maxLockoutMinutes);
            LockoutEndsAt = DateTime.UtcNow.AddMinutes(dakika);
        }

        public void RegisterSuccessfulLogin(DateTime when, string? ipAddress)
        {
            FailedLoginCount = 0;
            LockoutEndsAt    = null;
            LastLoginAt      = when;
            LastActivityAt   = when;
            if (ipAddress != null) LastIpAddress = ipAddress;
        }

        public void ConfirmEmail()
        {
            EmailConfirmed          = true;
            EmailConfirmedAt        = DateTime.UtcNow;
            EmailConfirmationToken  = null;
        }

        // Legacy yardımcılar (SaaS-2b'de kalkacak)
        public bool IsPending   => Status == "pending";
        public bool IsApproved  => Status == "approved";
        public bool IsRejected  => Status == "rejected";
        public bool IsSuspended => Status == "suspended";
    }
}
