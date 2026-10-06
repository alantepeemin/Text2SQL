using Text2Sql.Application.DTOs.Query;

namespace Text2Sql.Application.DTOs.Jobs
{
    public class QueryJobDto
    {
        public int Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>Yalnızca tamamlanmış işlerde dolu.</summary>
        public QueryResultDto? Result { get; set; }
    }

    public class CreateQueryJobResponse
    {
        public int JobId { get; set; }
        public string Status { get; set; } = string.Empty;

        /// <summary>İstemcinin durumu çekeceği adres (RFC 7231 Location deseni).</summary>
        public string StatusUrl { get; set; } = string.Empty;
    }
}
