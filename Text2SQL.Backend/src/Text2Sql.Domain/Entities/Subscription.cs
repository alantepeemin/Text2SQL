using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// SaaS-5: Organizasyonun aboneliği.
    ///
    /// Kiracıya aittir (ITenantScoped) → global filtre otomatik uygulanır:
    /// bir organizasyon başkasının aboneliğini göremez.
    ///
    /// Bir organizasyonun tek bir AKTİF aboneliği olur; geçmiş kayıtlar
    /// (plan yükseltme/düşürme) Status ile korunur — faturalama denetimi
    /// için tarihsel iz gerekir.
    /// </summary>
    public class Subscription : ITenantScoped
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        public int PlanId { get; set; }
        public Plan Plan { get; set; } = null!;

        /// <summary>active | trialing | past_due | canceled</summary>
        public string Status { get; set; } = SubscriptionStatuses.Active;

        public DateTime CurrentPeriodStart { get; set; } = DateTime.UtcNow;
        public DateTime? CurrentPeriodEnd { get; set; }
        public DateTime? TrialEndsAt { get; set; }
        public DateTime? CanceledAt { get; set; }

        /// <summary>Ödeme sağlayıcısındaki abonelik kimliği (ileride Stripe sub_...).</summary>
        public string? ExternalSubscriptionId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // ── Davranış ─────────────────────────────────────────────────────────

        public bool IsTrialing => Status == SubscriptionStatuses.Trialing
                                  && (TrialEndsAt == null || TrialEndsAt > DateTime.UtcNow);

        /// <summary>
        /// Abonelik hizmet vermeye devam ediyor mu?
        /// past_due bilinçli olarak ERİŞİMİ KESMEZ — ödeme gecikmesinde müşteriyi
        /// anında kilitlemek SaaS'ta kötü pratiktir (kart yenileme süresi tanınır).
        /// canceled ise erişimi durdurur.
        /// </summary>
        public bool IsServiceable =>
            Status is SubscriptionStatuses.Active
                   or SubscriptionStatuses.Trialing
                   or SubscriptionStatuses.PastDue;

        public void ChangePlan(int newPlanId)
        {
            PlanId = newPlanId;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Cancel()
        {
            Status = SubscriptionStatuses.Canceled;
            CanceledAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public static class SubscriptionStatuses
    {
        public const string Active   = "active";
        public const string Trialing = "trialing";
        public const string PastDue  = "past_due";
        public const string Canceled = "canceled";
    }
}
