using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Text2Sql.Api.Http;
using Microsoft.AspNetCore.RateLimiting;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Auth;

namespace Text2Sql.Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    [Route("api/v2/auth")]
    [Route("api/v1/auth")] // Faz 2: v1 alias — mevcut route korunur
    [EnableRateLimiting("auth")] // Faz 2: IP başına brute-force koruması
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        /// <summary>Adım 1: Şirket bilgilerini gönder, kayıt token'ı al.</summary>
        [HttpPost("create-company")]
        [AllowAnonymous]
        public async Task<IActionResult> CreateCompany([FromBody] CreateCompanyDto dto)
        {
            var result = await _authService.CreateCompanyAsync(dto);
            return this.ApiOk(result, "Şirket kaydı başlatıldı. Lütfen admin bilgilerinizi girerek kaydı tamamlayın.");
        }

        /// <summary>Adım 2: Admin kullanıcıyı oluştur, JWT al.</summary>
        [HttpPost("complete-registration")]
        [AllowAnonymous]
        public async Task<IActionResult> CompleteRegistration([FromBody] CompleteRegistrationDto dto)
        {
            var result = await _authService.CompleteRegistrationAsync(dto);
            return this.ApiOk(result, "Şirket ve admin kaydı tamamlandı.");
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginDto dto)
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _authService.LoginAsync(dto, ipAddress);
            return this.ApiOk(result);
        }

        /// <summary>Davetiye ile kayıt — onay gerektirir, token döndürmez.</summary>
        [HttpPost("accept-invitation")]
        [AllowAnonymous]
        public async Task<IActionResult> AcceptInvitation([FromBody] AcceptInvitationDto dto)
        {
            var result = await _authService.AcceptInvitationAsync(dto);
            return this.ApiOk(result);
        }

        /// <summary>Şirket kodu ile kayıt — onay gerektirir, token döndürmez.</summary>
        [HttpPost("join-by-code")]
        [AllowAnonymous]
        public async Task<IActionResult> JoinByCode([FromBody] JoinByCodeDto dto)
        {
            var result = await _authService.JoinByCodeAsync(dto);
            return this.ApiOk(result);
        }

        /// <summary>Access token yenile.</summary>
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _authService.RefreshTokenAsync(dto.RefreshToken, ipAddress);
            return this.ApiOk(result, "Token yenilendi.");
        }

        /// <summary>SaaS-7: E-posta doğrulama (anonim — bağlantıya tıklayan henüz giriş yapmamış olabilir).</summary>
        [HttpPost("confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail([FromBody] ConfirmEmailDto dto, CancellationToken ct)
        {
            var result = await _authService.ConfirmEmailAsync(dto.Token, ct);
            return this.ApiOk(result, "E-posta adresiniz doğrulandı.");
        }

        /// <summary>SaaS-7: Doğrulama e-postasını yeniden gönder.</summary>
        [HttpPost("resend-email-confirmation")]
        [Authorize]
        public async Task<IActionResult> ResendEmailConfirmation(CancellationToken ct)
        {
            await _authService.ResendEmailConfirmationAsync(ct);
            return this.ApiOk(null!,
                "Doğrulama e-postası gönderildi (e-posta gönderimi kapalıysa loglara bakın).");
        }

        /// <summary>SaaS-2: Aktif organizasyonu değiştir — yeni token seti döner.</summary>
        [HttpPost("switch-organization")]
        [Authorize]
        public async Task<IActionResult> SwitchOrganization([FromBody] SwitchOrganizationDto dto)
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var result = await _authService.SwitchOrganizationAsync(dto.OrganizationId, ipAddress);
            return this.ApiOk(result, "Organizasyon değiştirildi.");
        }

        /// <summary>Çıkış yap — refresh token'ı geçersiz kıl.</summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout([FromBody] RefreshTokenDto dto)
        {
            await _authService.RevokeRefreshTokenAsync(dto.RefreshToken, "Logout");
            return this.ApiOk(null!, "Çıkış yapıldı.");
        }
    }
}
