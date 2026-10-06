using Text2Sql.Domain.Common;

namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// Asenkron sorgu işi.
    ///
    /// Neden gerekli: LLM + büyük sorgu 60 saniyeyi bulabiliyor. Senkron HTTP'de
    /// bu, proxy/tarayıcı timeout'u ve kötü "yükleniyor" deneyimi demek.
    /// 202 + durum sorgulama deseni ile istemci işi başlatır, ilerlemeyi çeker.
    ///
    /// Durum DB'de tutulur (bellekte değil): uygulama yeniden başlarsa
    /// yarım kalan işler "failed" olarak işaretlenir ve istemci sonsuza kadar
    /// beklemez.
    /// </summary>
    public class QueryJob : ITenantScoped
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public int ProjectId { get; set; }
        public int DataSourceId { get; set; }
        public int UserId { get; set; }

        public string Question { get; set; } = string.Empty;

        /// <summary>pending | running | succeeded | failed</summary>
        public string Status { get; set; } = QueryJobStatuses.Pending;

        /// <summary>Tamamlandığında sonucun okunacağı geçmiş kaydı.</summary>
        public int? QueryHistoryId { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public bool IsTerminal => Status is QueryJobStatuses.Succeeded or QueryJobStatuses.Failed;

        public void MarkRunning()
        {
            Status = QueryJobStatuses.Running;
            StartedAt = DateTime.UtcNow;
        }

        public void MarkSucceeded(int queryHistoryId)
        {
            Status = QueryJobStatuses.Succeeded;
            QueryHistoryId = queryHistoryId;
            CompletedAt = DateTime.UtcNow;
        }

        public void MarkFailed(string error)
        {
            Status = QueryJobStatuses.Failed;
            ErrorMessage = error.Length > 1000 ? error[..1000] : error;
            CompletedAt = DateTime.UtcNow;
        }
    }

    public static class QueryJobStatuses
    {
        public const string Pending   = "pending";
        public const string Running   = "running";
        public const string Succeeded = "succeeded";
        public const string Failed    = "failed";
    }
}
