using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Entities;
using Text2Sql.Domain.Enums;
using Text2Sql.Application.DTOs.Project;

namespace Text2Sql.Application.Services
{
    public class ProjectService : IProjectService
    {
        private readonly IAppDbContext _context;
        private readonly ICurrentUserContext _currentUser;
        private readonly IPlanLimitService _planLimits;
        private readonly ILogger<ProjectService> _logger;

        public ProjectService(
            IAppDbContext context,
            ICurrentUserContext currentUser,
            IPlanLimitService planLimits,
            ILogger<ProjectService> logger)
        {
            _context = context;
            _currentUser = currentUser;
            _planLimits = planLimits;
            _logger = logger;
        }

        public async Task<int> CreateProjectAsync(string name, string? description)
        {
            // SaaS-5: Plan proje limiti (0 = sınırsız)
            await _planLimits.EnsureCanAddProjectAsync();

            var project = new Project
            {
                CompanyId = _currentUser.TenantId,
                Name = name,
                Description = description,
                CreatedAt = DateTime.UtcNow
            };

            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            _context.ProjectAccesses.Add(new ProjectAccess
            {
                CompanyId = _currentUser.TenantId, // SaaS-1
                ProjectId = project.Id,
                UserId = _currentUser.UserId,
                Permission = ProjectPermission.Owner,
                GrantedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            _logger.LogInformation("Project created: {ProjectName}, Id={ProjectId}, Creator={UserId}",
                name, project.Id, _currentUser.UserId);

            return project.Id;
        }

        public async Task<List<ProjectDto>> GetMyProjectsAsync()
        {
            return await _context.ProjectAccesses
                .Include(pa => pa.Project)
                .Where(pa => pa.UserId == _currentUser.UserId && pa.Project.IsActive)
                .Select(pa => new ProjectDto
                {
                    ProjectId = pa.Project.Id,
                    Name = pa.Project.Name,
                    Description = pa.Project.Description,
                    Permission = pa.Permission.ToString()
                })
                .ToListAsync();
        }

        public async Task<bool> GrantAccessAsync(int projectId, int targetUserId, ProjectPermission permission)
        {
            var project = await _context.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == projectId)
                ?? throw new Exception("Proje bulunamadı.");

            // SaaS-2: Yetki verilebilmesi için hedef kişinin BU organizasyonda
            // onaylı ve aktif ÜYELİĞİ olmalı. (Kullanıcı hesabı artık organizasyondan
            // bağımsız; legacy User.CompanyId karşılaştırması geçersiz hale geldi.)
            var uyelikVar = await _context.Memberships.AsNoTracking()
                .AnyAsync(m => m.UserId == targetUserId &&
                               m.CompanyId == project.CompanyId &&
                               m.Status == "approved" && m.IsActive);

            if (!uyelikVar)
                throw new Exception("Kullanıcı bu organizasyonda onaylı üye değil.");

            var existing = await _context.ProjectAccesses
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.UserId == targetUserId);

            if (existing != null)
            {
                existing.Permission = permission;
                existing.GrantedAt = DateTime.UtcNow;
            }
            else
            {
                _context.ProjectAccesses.Add(new ProjectAccess
                {
                    CompanyId = project.CompanyId, // SaaS-1
                    ProjectId = projectId,
                    UserId = targetUserId,
                    Permission = permission,
                    GrantedById = _currentUser.UserId,
                    GrantedAt = DateTime.UtcNow
                });
            }

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Project access granted: ProjectId={ProjectId}, UserId={UserId}, Permission={Permission}",
                    projectId, targetUserId, permission);
                return true;
            }
            catch (DbUpdateException ex)
            {
                throw new Exception($"Erişim verilirken DB hatası: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        public async Task<List<ProjectAccessDto>> GetProjectAccessListAsync(int projectId)
        {
            return await _context.ProjectAccesses
                .Include(pa => pa.User)
                .Where(pa => pa.ProjectId == projectId)
                .Select(pa => new ProjectAccessDto
                {
                    UserId = pa.UserId,
                    Username = pa.User.Username,
                    Email = pa.User.Email,
                    Permission = pa.Permission.ToString(),
                    GrantedAt = pa.GrantedAt,
                    GrantedBy = pa.GrantedBy != null ? pa.GrantedBy.Username : null
                })
                .ToListAsync();
        }

        public async Task<bool> RemoveAccessAsync(int projectId, int userId)
        {
            if (userId == _currentUser.UserId)
                throw new Exception("Kendi erişiminizi kaldıramazsınız.");

            var access = await _context.ProjectAccesses
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.UserId == userId);

            if (access == null) return false;

            if (access.Permission == ProjectPermission.Owner)
            {
                var ownerCount = await _context.ProjectAccesses
                    .CountAsync(a => a.ProjectId == projectId && a.Permission == ProjectPermission.Owner);

                if (ownerCount <= 1)
                    throw new Exception("Projede en az bir owner bulunmalıdır.");
            }

            _context.ProjectAccesses.Remove(access);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Project access removed: ProjectId={ProjectId}, UserId={UserId}", projectId, userId);
            return true;
        }

        public async Task<bool> UpdateAccessAsync(int projectId, int userId, ProjectPermission permission)
        {
            var access = await _context.ProjectAccesses
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.UserId == userId)
                ?? throw new Exception("Kullanıcının bu projeye erişimi bulunmuyor.");

            if (userId == _currentUser.UserId &&
                access.Permission == ProjectPermission.Owner &&
                permission != ProjectPermission.Owner)
            {
                var ownerCount = await _context.ProjectAccesses
                    .CountAsync(a => a.ProjectId == projectId && a.Permission == ProjectPermission.Owner);

                if (ownerCount <= 1)
                    throw new Exception("Projede en az bir owner bulunmalıdır.");
            }

            access.Permission = permission;
            access.GrantedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Project access updated: ProjectId={ProjectId}, UserId={UserId}, Permission={Permission}",
                projectId, userId, permission);
            return true;
        }

        public async Task<bool> UpdateProjectAsync(int projectId, string name, string? description)
        {
            var project = await _context.Projects.FirstOrDefaultAsync(p => p.Id == projectId)
                ?? throw new Exception("Proje bulunamadı.");

            project.Name = name;
            project.Description = description;
            project.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Project updated: ProjectId={ProjectId}, Name={Name}", projectId, name);
            return true;
        }

        public async Task<bool> DeleteProjectAsync(int projectId)
        {
            var project = await _context.Projects.FirstOrDefaultAsync(p => p.Id == projectId)
                ?? throw new Exception("Proje bulunamadı.");

            project.IsActive = false;
            project.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Project deleted: ProjectId={ProjectId}, Name={Name}", projectId, project.Name);
            return true;
        }

        public async Task<ProjectDetailsDto> GetProjectDetailsAsync(int projectId)
        {
            var project = await _context.Projects
                .Where(p => p.Id == projectId)
                .Select(p => new ProjectDetailsDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    DatabaseCount = p.Databases.Count(d => d.IsActive),
                    UserCount = p.AccessList.Count(),
                    QueryCount = p.QueryHistories.Count(),
                    LastQueryAt = p.QueryHistories.OrderByDescending(q => q.CreatedAt).FirstOrDefault() != null
                        ? p.QueryHistories.OrderByDescending(q => q.CreatedAt).First().CreatedAt : null
                })
                .FirstOrDefaultAsync()
                ?? throw new Exception("Proje bulunamadı.");

            var myAccess = await _context.ProjectAccesses
                .FirstOrDefaultAsync(pa => pa.ProjectId == projectId && pa.UserId == _currentUser.UserId);

            project.MyPermission = myAccess?.Permission.ToString() ?? "None";
            return project;
        }

        public async Task<List<ProjectSummaryDto>> GetAllCompanyProjectsAsync()
        {
            return await _context.Projects
                .Where(p => p.CompanyId == _currentUser.TenantId)
                .Select(p => new ProjectSummaryDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    CreatedAt = p.CreatedAt,
                    IsActive = p.IsActive,
                    UserCount = p.AccessList.Count(),
                    DatabaseCount = p.Databases.Count(d => d.IsActive),
                    QueryCount = p.QueryHistories.Count(),
                    LastActivity = p.QueryHistories.OrderByDescending(q => q.CreatedAt).FirstOrDefault() != null
                        ? p.QueryHistories.OrderByDescending(q => q.CreatedAt).First().CreatedAt : null,
                    MostActiveUser = p.QueryHistories
                        .GroupBy(q => q.User.Username)
                        .OrderByDescending(g => g.Count())
                        .Select(g => g.Key)
                        .FirstOrDefault()
                })
                .OrderByDescending(p => p.LastActivity ?? p.CreatedAt)
                .ToListAsync();
        }

        public async Task<ProjectActivityDto> GetProjectActivityAsync(int projectId, int days)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            var project = await _context.Projects.FirstOrDefaultAsync(p => p.Id == projectId)
                ?? throw new Exception("Proje bulunamadı.");

            var queries = await _context.QueryHistories
                .Include(q => q.User)
                .Where(q => q.ProjectId == projectId && q.CreatedAt >= cutoffDate)
                .ToListAsync();

            return new ProjectActivityDto
            {
                ProjectId = projectId,
                ProjectName = project.Name,
                TotalQueries = queries.Count,
                SuccessfulQueries = queries.Count(q => q.IsSuccessful),
                FailedQueries = queries.Count(q => !q.IsSuccessful),
                UserActivities = queries
                    .GroupBy(q => q.User.Username)
                    .Select(g => new UserActivityDto
                    {
                        Username = g.Key,
                        QueryCount = g.Count(),
                        LastQueryAt = g.Max(q => q.CreatedAt)
                    })
                    .OrderByDescending(u => u.QueryCount)
                    .ToList(),
                DailyActivities = queries
                    .GroupBy(q => q.CreatedAt.Date)
                    .Select(g => new DailyActivityDto
                    {
                        Date = g.Key,
                        QueryCount = g.Count(),
                        UniqueUsers = g.Select(q => q.UserId).Distinct().Count()
                    })
                    .OrderBy(d => d.Date)
                    .ToList()
            };
        }

        public async Task<bool> HasProjectAccessAsync(int projectId, int userId, ProjectPermission minPermission)
        {
            var access = await _context.ProjectAccesses
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.UserId == userId);
            return access != null && (int)access.Permission >= (int)minPermission;
        }

        public async Task<ProjectPermission?> GetUserPermissionAsync(int projectId, int userId)
        {
            var access = await _context.ProjectAccesses
                .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.UserId == userId);
            return access?.Permission;
        }
    }
}
