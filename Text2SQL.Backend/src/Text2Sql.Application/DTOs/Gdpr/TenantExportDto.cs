namespace Text2Sql.Application.DTOs.Gdpr
{
    /// <summary>
    /// SaaS-8: Kiracı veri ihracı. Hassas alanlar BİLİNÇLİ OLARAK dışarıda:
    /// parola özetleri, API anahtarı özetleri, şifreli bağlantı dizeleri.
    /// (Bunları ihraç etmek yeni bir sızıntı yüzeyi yaratırdı; kullanıcının
    /// kendi verisi değil sistemin güvenlik malzemesidir.)
    /// </summary>
    public class TenantExportDto
    {
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        public string Format { get; set; } = "text2sql.tenant-export.v1";

        public OrganizationExport Organization { get; set; } = new();
        public List<MemberExport> Members { get; set; } = new();
        public List<ProjectExport> Projects { get; set; } = new();
        public List<DataSourceExport> DataSources { get; set; } = new();
        public List<QueryExport> Queries { get; set; } = new();
        public List<AuditExport> AuditLogs { get; set; } = new();
        public UsageExport Usage { get; set; } = new();
    }

    public class OrganizationExport
    {
        public string Name { get; set; } = string.Empty;
        public string? Domain { get; set; }
        public string? PlanCode { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class MemberExport
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime JoinedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    public class ProjectExport
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public List<string> Members { get; set; } = new();
    }

    public class DataSourceExport
    {
        public string Name { get; set; } = string.Empty;
        public string DbType { get; set; } = string.Empty;
        public string? Host { get; set; }
        public string? DatabaseName { get; set; }
        public DateTime CreatedAt { get; set; }
        // Bağlantı dizesi / parola ihraç EDİLMEZ
    }

    public class QueryExport
    {
        public string Question { get; set; } = string.Empty;
        public string? Sql { get; set; }
        public bool IsSuccessful { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? User { get; set; }
    }

    public class AuditExport
    {
        public string Action { get; set; } = string.Empty;
        public string? Actor { get; set; }
        public string? Summary { get; set; }
        public DateTime OccurredAt { get; set; }
    }

    public class UsageExport
    {
        public long TotalTokens { get; set; }
        public decimal TotalEstimatedCostUsd { get; set; }
        public int TotalQueries { get; set; }
    }

    public class DeleteTenantRequest
    {
        /// <summary>Yanlışlıkla silmeye karşı: organizasyon adı birebir yazılmalı.</summary>
        public string ConfirmationText { get; set; } = string.Empty;
    }
}
