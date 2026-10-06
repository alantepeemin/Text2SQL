namespace Text2Sql.Application.DTOs.Query
{
    public class ExecuteQueryRequest
    {
        public int DatabaseId { get; set; }
        public string Question { get; set; } = string.Empty;
    }

    public class QueryResultDto
    {
        public string Question { get; set; } = string.Empty;
        public string Sql { get; set; } = string.Empty;
        public List<string> Columns { get; set; } = new();
        public List<List<string>> Rows { get; set; } = new();
        public bool Success { get; set; } = true;
        public string? ErrorMessage { get; set; }
        public DateTime? CreatedAt { get; set; }
    }
}
