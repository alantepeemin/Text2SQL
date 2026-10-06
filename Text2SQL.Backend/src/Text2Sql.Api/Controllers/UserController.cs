using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Text2Sql.Api.Http;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Auth;
using Text2Sql.Application.DTOs.User;

namespace Text2Sql.Api.Controllers
{
    [ApiController]
    [Route("api/users")]
    [Route("api/v2/users")]
    [Route("api/v1/users")] // Faz 2: v1 alias — mevcut route korunur
    [Authorize]
    public class UserController : ControllerBase
    {
        private readonly IUserService          _userService;
        private readonly ICurrentUserContext   _currentUser;
        private readonly IAuthService          _authService;

        public UserController(IUserService userService, ICurrentUserContext currentUser, IAuthService authService)
        {
            _userService  = userService;
            _currentUser  = currentUser;
            _authService  = authService;
        }

        /// <summary>SaaS-2: Kullanıcının üye olduğu organizasyonlar.</summary>
        [HttpGet("me/organizations")]
        public async Task<IActionResult> GetMyOrganizations()
        {
            var orgs = await _authService.GetMyOrganizationsAsync();
            return this.ApiOk(orgs);
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var profile = await _userService.GetProfileAsync(_currentUser.UserId);
            return this.ApiOk(profile);
        }

        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var result = await _userService.UpdateProfileAsync(_currentUser.UserId, request);
            return this.ApiOk(result, "Profil güncellendi.");
        }

        [HttpPut("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
        {
            var result = await _userService.ChangePasswordAsync(_currentUser.UserId, request);
            return this.ApiOk(result, "Şifre değiştirildi.");
        }

        /// <summary>Hesabı kalıcı silmez, devre dışı bırakır (soft delete).</summary>
        [HttpDelete("deactivate")]
        public async Task<IActionResult> DeactivateAccount()
        {
            var result = await _userService.DeactivateAccountAsync(_currentUser.UserId);
            return this.ApiOk(result, "Hesabınız devre dışı bırakıldı.");
        }
    }
}
