using System.ComponentModel.DataAnnotations;

namespace Text2Sql.Application.DTOs.Schema
{
    public class SchemaAnnotationDto
    {
        public int Id { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string? ColumnName { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; }
    }

    public class UpsertAnnotationRequest
    {
        [Required, MaxLength(128)]
        public string TableName { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? ColumnName { get; set; }

        [Required, MinLength(3), MaxLength(500)]
        public string Description { get; set; } = string.Empty;
    }

    public class SubmitFeedbackRequest
    {
        [Required]
        public bool IsHelpful { get; set; }

        [MaxLength(4000)]
        public string? CorrectedSql { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }
    }

    public class FeedbackSummaryDto
    {
        public int TotalFeedback { get; set; }
        public int HelpfulCount { get; set; }
        public int NotHelpfulCount { get; set; }
        public double AccuracyRate { get; set; }
        public int CorrectedSqlCount { get; set; }
    }
}
