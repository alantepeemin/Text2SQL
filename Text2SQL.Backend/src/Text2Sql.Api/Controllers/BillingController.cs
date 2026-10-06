using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Text2Sql.Api.Http;
using Text2Sql.Api.Auth;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.DTOs.Billing;
using Text2Sql.Domain.Authorization;

namespace Text2Sql.Api.Controllers
{
    /// <summary>
    /// SaaS-5: Plan katalogu ve abonelik.
    /// Ödeme tahsilatı KAPSAM DIŞI — plan değişikliği doğrudan uygulanır.
    /// Stripe entegrasyonu geldiğinde ChangePlan bir checkout akışına dönüşecek;
    /// sözleşme (DTO'lar) aynı kalabilir.
    /// </summary>
    [ApiController]
    [Route("api/billing")]
    [Route("api/v2/billing")]
    [Route("api/v1/billing")]
    [Authorize]
    public class BillingController : ControllerBase
    {
        private readonly IPlanService _planService;

        public BillingController(IPlanService planService) => _planService = planService;

        /// <summary>Plan katalogu — hangi paket ne içeriyor (mevcut plan işaretli).</summary>
        [HttpGet("plans")]
        public async Task<IActionResult> GetPlans(CancellationToken ct)
        {
            var plans = await _planService.GetPlanCatalogAsync(ct);
            return this.ApiOk(plans);
        }

        /// <summary>Mevcut abonelik: durum, limitler ve limitlere göre kullanım.</summary>
        [HttpGet("subscription")]
        public async Task<IActionResult> GetSubscription(CancellationToken ct)
        {
            var subscription = await _planService.GetCurrentSubscriptionAsync(ct);
            return this.ApiOk(subscription);
        }

        [HttpPost("subscription/change-plan")]
        [RequirePermission(Permissions.BillingManage)]
        public async Task<IActionResult> ChangePlan([FromBody] ChangePlanRequest request, CancellationToken ct)
        {
            var subscription = await _planService.ChangePlanAsync(request.PlanCode, ct);
            return this.ApiOk(subscription, "Plan güncellendi.");
        }
    }
}
