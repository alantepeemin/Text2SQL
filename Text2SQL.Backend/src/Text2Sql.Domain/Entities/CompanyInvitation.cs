using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    public class CompanyInvitation : ITenantScoped
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        public string Email { get; set; } = string.Empty;
        public string InvitationToken { get; set; } = string.Empty;
        public string Role { get; set; } = "user";

        public int? InvitedBy { get; set; }
        public User? InvitedByUser { get; set; }

        public DateTime ExpiresAt { get; set; }
        public bool IsUsed { get; set; } = false;
        public DateTime? UsedAt { get; set; }
        public int? UsedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
