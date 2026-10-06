namespace Text2Sql.Application.DTOs.Usage
{
    public class UsageSummaryDto
    {
        public int  OrganizationId { get; set; }
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }

        public int QueryCount { get; set; }
        public int SuccessfulQueryCount { get; set; }
        public long PromptTokens { get; set; }
        public long CompletionTokens { get; set; }
        public long TotalTokens { get; set; }
        public decimal EstimatedCostUsd { get; set; }

        /// <summary>Plan limiti (0 = sınırsız). SaaS-5'te plandan gelecek.</summary>
        public long MonthlyTokenLimit { get; set; }
        public long RemainingTokens { get; set; }

        public List<UsageByUserDto> ByUser { get; set; } = new();
        public List<UsageByModelDto> ByModel { get; set; } = new();
    }

    public class UsageByUserDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public int QueryCount { get; set; }
        public long TotalTokens { get; set; }
        public decimal EstimatedCostUsd { get; set; }
    }

    public class UsageByModelDto
    {
        public string Model { get; set; } = string.Empty;
        public int QueryCount { get; set; }
        public long TotalTokens { get; set; }
        public decimal EstimatedCostUsd { get; set; }
    }

    public class AuditLogDto
    {
        public long Id { get; set; }
        public string Action { get; set; } = string.Empty;
        public string? ActorEmail { get; set; }
        public string? TargetType { get; set; }
        public string? TargetId { get; set; }
        public string? Summary { get; set; }
        public string? IpAddress { get; set; }
        public DateTime OccurredAt { get; set; }
    }
}
