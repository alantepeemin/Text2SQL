using Text2Sql.Application.DTOs.Schema;

namespace Text2Sql.Application.Contracts
{
    /// <summary>SaaS-9: Şema sözlüğü yönetimi ve prompt'a enjeksiyonu.</summary>
    public interface ISchemaDictionaryService
    {
        Task<List<SchemaAnnotationDto>> GetAsync(int dataSourceId, CancellationToken ct = default);
        Task<SchemaAnnotationDto> UpsertAsync(int dataSourceId, UpsertAnnotationRequest request, CancellationToken ct = default);
        Task DeleteAsync(int annotationId, CancellationToken ct = default);

        /// <summary>
        /// Prompt'a eklenecek sözlük metnini üretir (yalnızca şemada geçen
        /// tablolara ait açıklamalar — gereksiz token harcanmaz).
        /// </summary>
        Task<string?> BuildGlossaryAsync(int dataSourceId, string schemaText, CancellationToken ct = default);
    }

    /// <summary>SaaS-9: Sorgu geri bildirimi.</summary>
    public interface IQueryFeedbackService
    {
        Task SubmitAsync(int queryHistoryId, SubmitFeedbackRequest request, CancellationToken ct = default);
        Task<FeedbackSummaryDto> GetSummaryAsync(CancellationToken ct = default);
    }
}
