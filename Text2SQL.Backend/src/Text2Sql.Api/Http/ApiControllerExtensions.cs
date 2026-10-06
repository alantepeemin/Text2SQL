using Microsoft.AspNetCore.Mvc;

namespace Text2Sql.Api.Http
{
    /// <summary>
    /// Controller'lar ARTIK ÇIPLAK VERİ döndürür; v1 zarfı
    /// <see cref="Filters.ResponseEnvelopeFilter"/> tarafından eklenir.
    ///
    /// Bu yaklaşım sayesinde tek controller kodu iki sözleşmeyi de besler:
    /// v1 istemcisi kırılmaz, v2 temiz kalır ve ileride v1 emekliye
    /// ayrıldığında yalnızca filter silinir.
    /// </summary>
    public static class ApiControllerExtensions
    {
        /// <summary>200 OK — isteğe bağlı başarı mesajı yalnızca v1 zarfında görünür.</summary>
        public static IActionResult ApiOk(this ControllerBase controller, object? data, string? message = null)
        {
            // Bu uç v1'de zarflı yanıt veriyordu → işaretle.
            controller.HttpContext.Items[ApiVersioning.EnvelopeItemKey] = true;

            if (message != null)
                controller.HttpContext.Items[ApiVersioning.MessageItemKey] = message;

            return controller.Ok(data);
        }
    }
}
