using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Text2Sql.Api.Http;
using Text2Sql.Application.Common;

namespace Text2Sql.Api.Filters
{
    /// <summary>
    /// Sürüme göre yanıt biçimlendirme — API v2'nin kalbi.
    ///
    /// v1: { success, message, data }  (eski sözleşme, aynen korunur)
    /// v2: çıplak veri + sayfalama başlıkları (X-Total-Count, X-Page, X-Page-Size)
    ///
    /// Neden filter: controller'ları iki kez yazmamak için. Aksi halde her uç
    /// v1 ve v2 için ayrı ayrı yazılırdı — 30+ endpoint'te sürdürülemez bir yük.
    /// </summary>
    public sealed class ResponseEnvelopeFilter : IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(
            ResultExecutingContext context, ResultExecutionDelegate next)
        {
            if (context.Result is ObjectResult objectResult && EtkilenirMi(objectResult))
            {
                var isV2 = context.HttpContext.IsV2();
                var deger = objectResult.Value;

                // Sayfalı sonuçlarda meta veriyi başlıklara taşı, gövdede
                // yalnızca listeyi bırak (her iki sürümde de gövde aynı şekle sahip).
                if (deger is not null && SayfaliMi(deger, out var items, out var page, out var pageSize, out var total))
                {
                    if (isV2)
                    {
                        var headers = context.HttpContext.Response.Headers;
                        headers["X-Total-Count"] = total.ToString();
                        headers["X-Page"] = page.ToString();
                        headers["X-Page-Size"] = pageSize.ToString();
                    }

                    deger = items;
                }

                // v1'de zarf YALNIZCA daha önce zarflı olan uçlara uygulanır
                // (this.ApiOk ile işaretlenenler). Zaten çıplak dönen uçlar
                // çıplak kalır — geriye dönük uyumluluk buna bağlı.
                var zarfliMi = context.HttpContext.Items.ContainsKey(ApiVersioning.EnvelopeItemKey);

                if (isV2 || !zarfliMi)
                {
                    objectResult.Value = deger;
                }
                else
                {
                    var mesaj = context.HttpContext.Items.TryGetValue(ApiVersioning.MessageItemKey, out var m)
                        ? m as string
                        : null;

                    objectResult.Value = new ApiResponse<object?>(true, mesaj ?? "İşlem başarılı", deger);
                }
            }

            await next();
        }

        /// <summary>Yalnızca 2xx JSON gövdeleri sarmalanır; dosya/redirect/hata sonuçlarına dokunulmaz.</summary>
        private static bool EtkilenirMi(ObjectResult result)
        {
            var status = result.StatusCode ?? StatusCodes.Status200OK;
            if (status is < 200 or > 299) return false;

            // ProblemDetails asla sarmalanmaz (hata sözleşmesi ayrıdır)
            return result.Value is not ProblemDetails;
        }

        private static bool SayfaliMi(
            object deger, out object? items, out int page, out int pageSize, out int total)
        {
            items = null; page = 0; pageSize = 0; total = 0;

            var tip = deger.GetType();
            if (!tip.IsGenericType || tip.GetGenericTypeDefinition() != typeof(PagedResult<>))
                return false;

            items    = tip.GetProperty(nameof(PagedResult<object>.Items))!.GetValue(deger);
            page     = (int)tip.GetProperty(nameof(PagedResult<object>.Page))!.GetValue(deger)!;
            pageSize = (int)tip.GetProperty(nameof(PagedResult<object>.PageSize))!.GetValue(deger)!;
            total    = (int)tip.GetProperty(nameof(PagedResult<object>.TotalCount))!.GetValue(deger)!;
            return true;
        }
    }
}
