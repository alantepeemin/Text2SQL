using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Api.Auth
{
    /// <summary>
    /// SaaS-5: Plan bazlı özellik kapısı.
    ///
    /// [RequirePermission] ile birlikte kullanılır ve FARKLI bir soruyu yanıtlar:
    ///   RequirePermission → "bu kullanıcı yapabilir mi?"   (403)
    ///   RequireFeature    → "bu paket kapsıyor mu?"        (402)
    ///
    /// Ayrı HTTP kodları bilinçli: istemci 403'te "yetkin yok", 402'de
    /// "paketini yükselt" mesajı gösterebilir.
    ///
    /// Authorization policy yerine action filter tercih edildi çünkü kontrol
    /// veritabanına gidiyor ve sonucu istemciye anlamlı bir 402 gövdesiyle
    /// dönmek gerekiyor (policy'ler yalnızca 403 üretebilir).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class RequireFeatureAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _featureKey;

        public RequireFeatureAttribute(string featureKey) => _featureKey = featureKey;

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var gate = context.HttpContext.RequestServices.GetService<IFeatureGate>();
            if (gate == null)
            {
                context.Result = new StatusCodeResult(500);
                return;
            }

            if (!await gate.IsEnabledAsync(_featureKey, context.HttpContext.RequestAborted))
            {
                // Yanıtı burada ELLE yazmak yerine alan istisnası fırlatılır:
                // durum kodu, makine-okunur kod ve sürüme uygun gövde tek
                // yerden (ExceptionMiddleware) üretilsin. Aksi halde v2
                // istemcisi bu uçta ProblemDetails yerine eski zarf alıyordu.
                throw new FeatureNotAvailableException(
                    "Bu özellik mevcut paketinizde bulunmuyor. Paketinizi yükseltin.");
            }

            await next();
        }
    }
}
