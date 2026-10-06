using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Text2Sql.Api.Http;
using Text2Sql.Application.Common;
using Text2Sql.Api.Auth;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Authorization;
using Text2Sql.Domain.Billing;
using Text2Sql.Application.DTOs.Company;
using Text2Sql.Application.DTOs.Gdpr;
using Text2Sql.Application.DTOs.Usage;

namespace Text2Sql.Api.Controllers
{
    [ApiController]
    [Route("api/company")]
    [Route("api/v2/company")]
    [Route("api/v1/company")] // Faz 2: v1 alias — mevcut route korunur
    [Authorize] // SaaS-4: yetki artık metod bazında izinlerle veriliyor
    public class CompanyController : ControllerBase
    {
        private readonly ICompanyService _companyService;
        private readonly IUsageService _usageService;
        private readonly ITenantDataService _tenantData;

        public CompanyController(
            ICompanyService companyService,
            IUsageService usageService,
            ITenantDataService tenantData)
        {
            _companyService = companyService;
            _usageService = usageService;
            _tenantData = tenantData;
        }

        // ── SaaS-8: KVKK/GDPR ────────────────────────────────────────────────

        /// <summary>Veri taşınabilirliği (GDPR Art. 20): organizasyonun tüm verisini ihraç eder.</summary>
        [HttpGet("export")]
        [RequirePermission(Permissions.OrganizationExport)]
        public async Task<IActionResult> ExportData(CancellationToken ct)
        {
            var export = await _tenantData.ExportAsync(ct);
            return this.ApiOk(export);
        }

        /// <summary>
        /// Silinme hakkı (GDPR Art. 17): organizasyonu ve TÜM verisini kalıcı siler.
        /// Onay için organizasyon adı birebir yazılmalıdır.
        /// </summary>
        [HttpDelete("organization")]
        [RequirePermission(Permissions.OrganizationDelete)]
        public async Task<IActionResult> DeleteOrganization(
            [FromBody] DeleteTenantRequest request, CancellationToken ct)
        {
            await _tenantData.DeleteTenantAsync(request.ConfirmationText, ct);
            return this.ApiOk(null!,
                "Organizasyon ve tüm verisi kalıcı olarak silindi.");
        }

        // ── SaaS-3: Kullanım ve denetim ──────────────────────────────────────

        /// <summary>Bu ayın kullanım özeti: sorgu/token/tahmini maliyet + kullanıcı ve model dağılımı.</summary>
        [HttpGet("usage")]
        [RequirePermission(Permissions.UsageRead)]
        [RequireFeature(Features.UsageReports)]
        public async Task<IActionResult> GetUsage(CancellationToken ct)
        {
            var usage = await _usageService.GetCurrentPeriodUsageAsync(ct);
            return this.ApiOk(usage);
        }

        /// <summary>Denetim kaydı (append-only): kim, ne zaman, neyi değiştirdi.</summary>
        [HttpGet("audit-logs")]
        [RequirePermission(Permissions.AuditRead)]
        [RequireFeature(Features.AuditLogs)]   // SaaS-5: yalnızca Enterprise
        public async Task<IActionResult> GetAuditLogs(
            [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        {
            var logs = await _usageService.GetAuditLogsAsync(page, pageSize, ct);
            return this.ApiOk(logs);
        }

        // Davet sistemi
        [HttpPost("invite-user")]
        [RequirePermission(Permissions.MembersInvite)]
        public async Task<IActionResult> InviteUser(InviteUserDto dto)
        {
            var result = await _companyService.InviteUserAsync(dto);
            return this.ApiOk(result, "Davetiye gönderildi.");
        }

        [HttpGet("invitations")]
        [RequirePermission(Permissions.MembersInvite)]
        public async Task<IActionResult> GetInvitations()
        {
            var invitations = await _companyService.GetPendingInvitationsAsync();
            return this.ApiOk(invitations);
        }

        // Kullanıcı onay sistemi
        [HttpGet("pending-users")]
        [RequirePermission(Permissions.MembersApprove)]
        public async Task<IActionResult> GetPendingUsers()
        {
            var pendingUsers = await _companyService.GetPendingUsersAsync();
            return this.ApiOk(pendingUsers);
        }

        [HttpPost("approve-user")]
        [RequirePermission(Permissions.MembersApprove)]
        public async Task<IActionResult> ApproveUser(UserApprovalDto dto)
        {
            var result = await _companyService.ApproveUserAsync(dto);
            var message = dto.Action.ToLower() switch
            {
                "approve" => "Kullanıcı onaylandı.",
                "reject"  => "Kullanıcı reddedildi.",
                "suspend" => "Kullanıcı askıya alındı.",
                _         => "İşlem tamamlandı."
            };
            return this.ApiOk(result, message);
        }

        // Kullanıcı yönetimi
        [HttpGet("users")]
        [RequirePermission(Permissions.MembersRead)]
        public async Task<IActionResult> GetCompanyUsers([FromQuery] int page = 1, [FromQuery] int pageSize = 100)
        {
            var users = await _companyService.GetCompanyUsersAsync(page, pageSize);
            return this.ApiOk(users);
        }

        [HttpGet("user-activity-summary")]
        [RequirePermission(Permissions.MembersRead)]
        public async Task<IActionResult> GetUserActivitySummary()
        {
            var summary = await _companyService.GetUserActivitySummaryAsync();
            return this.ApiOk(summary);
        }

        [HttpPut("users/{userId:int}/role")]
        [RequirePermission(Permissions.MembersManage)]
        public async Task<IActionResult> UpdateUserRole(int userId, [FromBody] UpdateUserRoleDto dto)
        {
            dto.UserId = userId;
            var result = await _companyService.UpdateUserRoleAsync(dto);
            return this.ApiOk(result, "Kullanıcı rolü güncellendi.");
        }

        [HttpDelete("users/{userId:int}")]
        [RequirePermission(Permissions.MembersManage)]
        public async Task<IActionResult> RemoveUser(int userId)
        {
            var result = await _companyService.RemoveUserAsync(userId);
            return this.ApiOk(result, "Kullanıcı pasifleştirildi.");
        }

        // Şirket yönetimi
        [HttpPost("generate-code")]
        [RequirePermission(Permissions.OrganizationManage)]
        public async Task<IActionResult> GenerateCompanyCode()
        {
            var code = await _companyService.GenerateCompanyCodeAsync();
            return this.ApiOk(code, "Şirket kodu oluşturuldu.");
        }
    }
}
