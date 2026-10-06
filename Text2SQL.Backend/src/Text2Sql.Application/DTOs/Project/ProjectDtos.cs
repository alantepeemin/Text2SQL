using Text2Sql.Domain.Enums;

namespace Text2Sql.Application.DTOs.Project
{
    public class CreateProjectRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class UpdateProjectRequest
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    public class ProjectDto
    {
        public int ProjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Permission { get; set; } = string.Empty;
    }

    public class ProjectDetailsDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string MyPermission { get; set; } = string.Empty;
        public int DatabaseCount { get; set; }
        public int UserCount { get; set; }
        public int QueryCount { get; set; }
        public DateTime? LastQueryAt { get; set; }
    }

    public class ProjectSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsActive { get; set; }
        public int UserCount { get; set; }
        public int DatabaseCount { get; set; }
        public int QueryCount { get; set; }
        public DateTime? LastActivity { get; set; }
        public string? MostActiveUser { get; set; }
    }

    public class GrantAccessRequest
    {
        public int UserId { get; set; }
        public ProjectPermission Permission { get; set; }
    }

    public class UpdateAccessRequest
    {
        public ProjectPermission Permission { get; set; }
    }

    public class ProjectAccessDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Permission { get; set; } = string.Empty;
        public DateTime GrantedAt { get; set; }
        public string? GrantedBy { get; set; }
    }

    public class ProjectActivityDto
    {
        public int ProjectId { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public int TotalQueries { get; set; }
        public int SuccessfulQueries { get; set; }
        public int FailedQueries { get; set; }
        public List<UserActivityDto> UserActivities { get; set; } = new();
        public List<DailyActivityDto> DailyActivities { get; set; } = new();
    }

    public class UserActivityDto
    {
        public string Username { get; set; } = string.Empty;
        public int QueryCount { get; set; }
        public DateTime LastQueryAt { get; set; }
    }

    public class DailyActivityDto
    {
        public DateTime Date { get; set; }
        public int QueryCount { get; set; }
        public int UniqueUsers { get; set; }
    }
}
