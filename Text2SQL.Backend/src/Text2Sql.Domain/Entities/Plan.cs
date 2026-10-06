namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// SaaS-5: Ticari paket tanımı.
    ///
    /// Plan KİRACIYA AİT DEĞİLDİR (ITenantScoped değil) — platform genelinde
    /// paylaşılan bir katalogdur. Bu yüzden global kiracı filtresi uygulanmaz.
    ///
    /// Limitler burada; hangi özelliklerin açık olduğu PlanFeature'da.
    /// Ödeme sağlayıcısı (Stripe vb.) entegrasyonu KAPSAM DIŞI — bu model
    /// entegrasyona hazır olacak şekilde tasarlandı (ExternalPriceId alanı).
    /// </summary>
    public class Plan
    {
        public int Id { get; set; }

        /// <summary>Kısa kod (free/pro/enterprise) — kodda referans için.</summary>
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        // ── Limitler (0 = sınırsız) ──────────────────────────────────────────
        public long MonthlyTokenLimit { get; set; }
        public int MaxMembers { get; set; }
        public int MaxProjects { get; set; }
        public int MaxDataSources { get; set; }
        public int MaxApiKeys { get; set; }

        /// <summary>Aylık fiyat (bilgi amaçlı; tahsilat yapılmaz).</summary>
        public decimal MonthlyPriceUsd { get; set; }

        /// <summary>Ödeme sağlayıcısındaki fiyat kimliği (ileride Stripe price_...).</summary>
        public string? ExternalPriceId { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public ICollection<PlanFeature> Features { get; set; } = new List<PlanFeature>();

        public bool IsUnlimited(long limit) => limit <= 0;
    }

    /// <summary>Plan ↔ özellik eşlemesi (hangi paket neyi içeriyor).</summary>
    public class PlanFeature
    {
        public int Id { get; set; }

        public int PlanId { get; set; }
        public Plan Plan { get; set; } = null!;

        public string FeatureKey { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
    }
}
