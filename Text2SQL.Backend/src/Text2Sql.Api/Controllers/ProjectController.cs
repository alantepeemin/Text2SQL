using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Text2Sql.Api.Http;
using Text2Sql.Api.Attributes;
using Text2Sql.Api.Auth;
using Text2Sql.Domain.Authorization;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Enums;
using Text2Sql.Application.DTOs.Project;

namespace Text2Sql.Api.Controllers
{
    [ApiController]
    [Route("api/projects")]
    [Route("api/v2/projects")]
    [Route("api/v1/projects")] // Faz 2: v1 alias — mevcut route korunur
    [Authorize]
    public class ProjectController : ControllerBase
    {
        private readonly IProjectService _projectService;

        public ProjectController(IProjectService projectService)
        {
            _projectService = projectService;
        }

        [HttpGet]
        public async Task<IActionResult> GetMyProjects()
        {
            var projects = await _projectService.GetMyProjectsAsync();
            return this.ApiOk(projects);
        }

        [HttpPost]
        [RequirePermission(Permissions.ProjectsCreate)]
        public async Task<IActionResult> CreateProject([FromBody] CreateProjectRequest request)
        {
            var projectId = await _projectService.CreateProjectAsync(request.Name, request.Description);
            return this.ApiOk(projectId, "Proje oluşturuldu");
        }

        [HttpGet("{projectId:int}")]
        [RequireProjectPermission(ProjectPermission.Viewer)]
        public async Task<IActionResult> GetProjectDetails(int projectId)
        {
            var project = await _projectService.GetProjectDetailsAsync(projectId);
            return this.ApiOk(project);
        }

        [HttpPut("{projectId:int}")]
        [RequireProjectPermission(ProjectPermission.Owner)]
        public async Task<IActionResult> UpdateProject(int projectId, [FromBody] UpdateProjectRequest request)
        {
            var result = await _projectService.UpdateProjectAsync(projectId, request.Name, request.Description);
            return this.ApiOk(result, "Proje güncellendi");
        }

        [HttpDelete("{projectId:int}")]
        [RequireProjectPermission(ProjectPermission.Owner)]
        public async Task<IActionResult> DeleteProject(int projectId)
        {
            var result = await _projectService.DeleteProjectAsync(projectId);
            return this.ApiOk(result, "Proje silindi");
        }

        // Erişim yönetimi
        [HttpPost("{projectId:int}/access")]
        [RequireProjectPermission(ProjectPermission.Owner)]
        public async Task<IActionResult> GrantAccess(int projectId, [FromBody] GrantAccessRequest request)
        {
            var result = await _projectService.GrantAccessAsync(projectId, request.UserId, request.Permission);
            return this.ApiOk(result, "Erişim verildi");
        }

        [HttpGet("{projectId:int}/access")]
        [RequireProjectPermission(ProjectPermission.Editor)]
        public async Task<IActionResult> GetProjectAccess(int projectId)
        {
            var accessList = await _projectService.GetProjectAccessListAsync(projectId);
            return this.ApiOk(accessList);
        }

        [HttpPut("{projectId:int}/access/{userId:int}")]
        [RequireProjectPermission(ProjectPermission.Owner)]
        public async Task<IActionResult> UpdateAccess(int projectId, int userId, [FromBody] UpdateAccessRequest request)
        {
            var result = await _projectService.UpdateAccessAsync(projectId, userId, request.Permission);
            return this.ApiOk(result, "Yetki güncellendi");
        }

        [HttpDelete("{projectId:int}/access/{userId:int}")]
        [RequireProjectPermission(ProjectPermission.Owner)]
        public async Task<IActionResult> RemoveAccess(int projectId, int userId)
        {
            var result = await _projectService.RemoveAccessAsync(projectId, userId);
            return this.ApiOk(result, "Kullanıcı projeden çıkarıldı");
        }

        // Admin endpointleri
        [HttpGet("all")]
        [RequirePermission(Permissions.ProjectsManage)]
        public async Task<IActionResult> GetAllProjects()
        {
            var projects = await _projectService.GetAllCompanyProjectsAsync();
            return this.ApiOk(projects);
        }

        [HttpGet("{projectId:int}/activity")]
        [RequireProjectPermission(ProjectPermission.Owner)]
        public async Task<IActionResult> GetProjectActivity(int projectId, [FromQuery] int days = 30)
        {
            var activity = await _projectService.GetProjectActivityAsync(projectId, days);
            return this.ApiOk(activity);
        }
    }
}
