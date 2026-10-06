using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    public class Project : ITenantScoped
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company Company { get; set; } = null!;

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ProjectAccess> AccessList { get; set; } = new List<ProjectAccess>();
        public ICollection<QueryHistory> QueryHistories { get; set; } = new List<QueryHistory>();
        public ICollection<ProjectDatabase> Databases { get; set; } = new List<ProjectDatabase>();
    }
}
