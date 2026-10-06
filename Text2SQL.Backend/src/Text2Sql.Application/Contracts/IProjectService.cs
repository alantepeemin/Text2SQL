using Text2Sql.Domain.Enums;
using Text2Sql.Application.DTOs.Project;

namespace Text2Sql.Application.Contracts
{
    public interface IProjectService
    {
        Task<int> CreateProjectAsync(string name, string? description);
        Task<List<ProjectDto>> GetMyProjectsAsync();
        Task<bool> GrantAccessAsync(int projectId, int userId, ProjectPermission permission);

        Task<List<ProjectAccessDto>> GetProjectAccessListAsync(int projectId);
        Task<bool> RemoveAccessAsync(int projectId, int userId);
        Task<bool> UpdateAccessAsync(int projectId, int userId, ProjectPermission permission);

        Task<bool> UpdateProjectAsync(int projectId, string name, string? description);
        Task<bool> DeleteProjectAsync(int projectId);
        Task<ProjectDetailsDto> GetProjectDetailsAsync(int projectId);

        Task<List<ProjectSummaryDto>> GetAllCompanyProjectsAsync();
        Task<ProjectActivityDto> GetProjectActivityAsync(int projectId, int days);

        Task<bool> HasProjectAccessAsync(int projectId, int userId, ProjectPermission minPermission);
        Task<ProjectPermission?> GetUserPermissionAsync(int projectId, int userId);
    }
}
