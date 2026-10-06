using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Enums;
using Text2Sql.Infrastructure.Persistence;

namespace Text2Sql.Api.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class RequireProjectPermissionAttribute : Attribute, IAsyncActionFilter
    {
        private readonly ProjectPermission _minPermission;

        public RequireProjectPermissionAttribute(ProjectPermission minPermission)
        {
            _minPermission = minPermission;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var projectService = context.HttpContext.RequestServices.GetService<IProjectService>();
            var currentUser    = context.HttpContext.RequestServices.GetService<ICurrentUserContext>();
            var dbContext      = context.HttpContext.RequestServices.GetService<ApplicationDbContext>();

            if (projectService == null || currentUser == null || dbContext == null)
            {
                context.Result = new StatusCodeResult(500);
                return;
            }

            // Route'dan projectId al
            if (!context.RouteData.Values.TryGetValue("projectId", out var projectIdObj) ||
                !int.TryParse(projectIdObj?.ToString(), out var projectId))
            {
                context.Result = new BadRequestObjectResult("Geçersiz proje ID.");
                return;
            }

            // GÜVENLİK (Faz 0 — kritik): Proje, isteği yapan kullanıcının tenant'ına
            // ait mi? Eski kodda admin bu kontrol yapılmadan geçiriliyordu; bu da
            // başka bir şirketin admin'inin bu projeye (detay/güncelleme/silme dahil)
            // erişebilmesi anlamına geliyordu (kiracılar arası veri sızıntısı).
            // 404 dönülür (403 değil) — başka tenant'a projenin varlığı dahi sızdırılmaz.
            var belongsToTenant = await dbContext.Projects
                .AsNoTracking()
                .AnyAsync(p => p.Id == projectId && p.CompanyId == currentUser.TenantId);

            if (!belongsToTenant)
            {
                context.Result = new NotFoundObjectResult("Proje bulunamadı.");
                return;
            }

            // Admin, kendi tenant'ındaki her projeye erişebilir
            if (currentUser.IsAdmin)
            {
                await next();
                return;
            }

            var permission = await projectService.GetUserPermissionAsync(projectId, currentUser.UserId);

            if (permission == null || (int)permission < (int)_minPermission)
            {
                context.Result = new ForbidResult();
                return;
            }

            await next();
        }
    }
}
