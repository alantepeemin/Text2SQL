using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    public class ProjectDatabase : ITenantScoped
    {
        public int Id { get; set; }

        public int ProjectId { get; set; }
        public Project Project { get; set; } = null!;
        public int CompanyId { get; set; }

        public string DbType { get; set; } = "sqlite";
        public string ConnectionName { get; set; } = string.Empty;

        public string? SqliteFilePath { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Username { get; set; }
        public string? PasswordEncrypted { get; set; }
        public string? DatabaseName { get; set; }
        public string? SchemaName { get; set; }

        public bool SslEnabled { get; set; } = false;
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
