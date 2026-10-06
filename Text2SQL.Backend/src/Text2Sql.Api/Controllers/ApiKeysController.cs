using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Text2Sql.Api.Http;
using Text2Sql.Api.Auth;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.ApiKeys;
using Text2Sql.Domain.Authorization;
using Text2Sql.Domain.Billing;

namespace Text2Sql.Api.Controllers
{
    /// <summary>
    /// SaaS-6: API anahtarı yönetimi.
    ///
    /// NOT: Bu uçlar bilinçli olarak API anahtarıyla kullanılamaz —
    /// gereken izinler (apikeys.*) anahtarlara verilemeyen scope'lar arasındadır.
    /// Böylece sızan bir anahtar yeni anahtar üretip kalıcılaşamaz.
    /// </summary>
    [ApiController]
    [Route("api/api-keys")]
    [Route("api/v2/api-keys")]
    [Route("api/v1/api-keys")]
    [Authorize]
    public class ApiKeysController : ControllerBase
    {
        private readonly IApiKeyService _apiKeyService;

        public ApiKeysController(IApiKeyService apiKeyService) => _apiKeyService = apiKeyService;

        /// <summary>Yeni anahtar üretir. Tam anahtar YALNIZCA bu yanıtta döner.</summary>
        [HttpPost]
        [RequirePermission(Permissions.ApiKeysManage)]
        [RequireFeature(Features.ApiKeys)]   // SaaS-5: Free planda kapalı
        public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request, CancellationToken ct)
        {
            var result = await _apiKeyService.CreateAsync(request, ct);
            return this.ApiOk(result,
                "API anahtarı oluşturuldu. Anahtarı güvenli bir yere kaydedin — bir daha gösterilmeyecek.");
        }

        [HttpGet]
        [RequirePermission(Permissions.ApiKeysRead)]
        [RequireFeature(Features.ApiKeys)]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var keys = await _apiKeyService.ListAsync(ct);
            return this.ApiOk(keys);
        }

        [HttpDelete("{keyId:int}")]
        [RequirePermission(Permissions.ApiKeysManage)]
        public async Task<IActionResult> Revoke(int keyId, CancellationToken ct)
        {
            await _apiKeyService.RevokeAsync(keyId, ct);
            return this.ApiOk(null!, "API anahtarı iptal edildi.");
        }
    }
}
