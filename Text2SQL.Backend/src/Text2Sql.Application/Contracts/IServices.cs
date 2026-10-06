using Text2Sql.Application.Common;
using Text2Sql.Domain.Enums;
using Text2Sql.Application.DTOs.Database;
using Text2Sql.Application.DTOs.Query;
using Text2Sql.Application.DTOs.User;

namespace Text2Sql.Application.Contracts
{
    public interface IProjectDatabaseService
    {
        Task<int>  AddLocalSqliteAsync(int projectId, CreateDatabaseForm form, CancellationToken ct = default);
        Task<int>  AddRemoteAsync(int projectId, CreateDatabaseForm form, CancellationToken ct = default);
        Task<List<ProjectDatabaseDto>> GetProjectDatabasesAsync(int projectId);
        Task<bool> DeleteDatabaseAsync(int projectId, int dbId); // ← projectId eklendi
    }

    public interface IQueryService
    {
        Task<QueryResultDto>       ExecuteQueryAsync(int projectId, int databaseId, string question, CancellationToken ct = default);
        Task<List<DatabaseOption>> GetAvailableDatabasesAsync(int projectId);
        Task<PagedResult<QueryResultDto>> GetHistoryAsync(int projectId, int page = 1, int pageSize = 50);
    }

    public interface IUserService
    {
        Task<UserDto> GetProfileAsync(int userId);
        Task<bool>    UpdateProfileAsync(int userId, UpdateProfileRequest request);
        Task<bool>    ChangePasswordAsync(int userId, ChangePasswordRequest request);
        Task<bool>    DeactivateAccountAsync(int userId); // ← soft delete
    }

    public interface IConnectionTester
    {
        Task<bool> TestConnectionAsync(DatabaseType dbType, string connectionString,
            CancellationToken ct = default);
    }

    /// <summary>
    /// SaaS-3: LLM çağrısının sonucu + TOKEN KULLANIMI.
    /// Kullanım bilgisi olmadan kiracı maliyeti ölçülemez, dolayısıyla
    /// fiyatlandırma yapılamazdı — bu yüzden imza değişti.
    /// </summary>
    public sealed record SqlGenerationResult(
        string Sql,
        string Model,
        int PromptTokens,
        int CompletionTokens);

    public interface ISqlGeneratorService
    {
        /// <summary>Faz 3: dialect parametresi prompt'a enjekte edilir; ct ile LLM çağrısı iptal edilebilir.</summary>
        Task<SqlGenerationResult> GenerateSqlAsync(
            string schema, string question, string dialect,
            string? glossary = null, CancellationToken ct = default);
    }
}
