namespace Text2Sql.Application.DTOs.Billing
{
    public class PlanDto
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long MonthlyTokenLimit { get; set; }
        public int MaxMembers { get; set; }
        public int MaxProjects { get; set; }
        public int MaxDataSources { get; set; }
        public int MaxApiKeys { get; set; }
        public decimal MonthlyPriceUsd { get; set; }
        public List<string> Features { get; set; } = new();
        public bool IsCurrent { get; set; }
    }

    public class SubscriptionDto
    {
        public string PlanCode { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CurrentPeriodStart { get; set; }
        public DateTime? CurrentPeriodEnd { get; set; }
        public DateTime? TrialEndsAt { get; set; }
        public bool IsServiceable { get; set; }
        public List<string> Features { get; set; } = new();
        public PlanLimitsDto Limits { get; set; } = new();
    }

    public class PlanLimitsDto
    {
        public long MonthlyTokenLimit { get; set; }
        public int MaxMembers { get; set; }
        public int MaxProjects { get; set; }
        public int MaxDataSources { get; set; }
        public int MaxApiKeys { get; set; }

        // Mevcut kullanım — istemcinin "3/5 proje" gösterebilmesi için
        public int CurrentMembers { get; set; }
        public int CurrentProjects { get; set; }
        public int CurrentDataSources { get; set; }
        public int CurrentApiKeys { get; set; }
    }

    public class ChangePlanRequest
    {
        public string PlanCode { get; set; } = string.Empty;
    }
}
