namespace Text2Sql.Domain.Entities
{
    public class Company
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Domain { get; set; }
        public string? CompanyCode { get; set; }
        public bool AllowDomainAutoJoin { get; set; } = false;

        public int MaxUsers { get; set; } = 10;
        public int MonthlyQueryLimit { get; set; } = 1000;
        public string SubscriptionStatus { get; set; } = "active";

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<User> Users { get; set; } = new List<User>();          // legacy (SaaS-2b'de kalkacak)
        public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
        public ICollection<Project> Projects { get; set; } = new List<Project>();
        public ICollection<CompanyInvitation> Invitations { get; set; } = new List<CompanyInvitation>();
    }
}
