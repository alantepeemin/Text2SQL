using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Api.Filters
{
    /// <summary>
    /// Idempotency-Key desteği.
    ///
    /// Sorun: kullanıcı "Çalıştır" düğmesine iki kez basınca iki LLM çağrısı
    /// yapılıyor ve kota iki kez tüketiliyordu. İstemci aynı anahtarla tekrar
    /// gönderirse ilk yanıt tekrarlanır.
    ///
    /// Kapsam bilinçli olarak dar: yalnızca POST istekleri ve yalnızca
    /// istemci başlık gönderdiyse. Anahtar kiracı+kullanıcı ile ilişkilendirilir
    /// (başka kiracının anahtarıyla çakışma imkânsız).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
    {
        public const string HeaderName = "Idempotency-Key";
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var keyValues)
                || string.IsNullOrWhiteSpace(keyValues.ToString()))
            {
                await next();
                return;
            }

            var cache = context.HttpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var user  = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserContext>();

            var cacheKey = $"idem_{user.TenantId}_{user.UserId}_{keyValues}";

            if (cache.TryGetValue(cacheKey, out CachedResponse? cached) && cached != null)
            {
                context.HttpContext.Response.Headers["Idempotency-Replayed"] = "true";
                if (!string.IsNullOrEmpty(cached.Location))
                    context.HttpContext.Response.Headers.Location = cached.Location;

                // Durum kodu da tekrarlanır: 202 ile başlayan bir istek
                // tekrarlandığında 200 dönmemeli (istemci akışı bozulur).
                context.Result = new ObjectResult(cached.Value) { StatusCode = cached.StatusCode };
                return;
            }

            var executed = await next();

            // Yalnızca BAŞARILI yanıtlar tekrarlanır; hata yanıtını cache'lemek
            // geçici bir arızayı 10 dakika kalıcı hale getirirdi.
            if (executed.Result is ObjectResult { StatusCode: >= 200 and < 300 } ok && ok.Value != null)
            {
                var location = context.HttpContext.Response.Headers.Location.ToString();
                cache.Set(cacheKey, new CachedResponse(
                    ok.StatusCode ?? StatusCodes.Status200OK, ok.Value, location), Ttl);
            }
        }

        private sealed record CachedResponse(int StatusCode, object Value, string? Location);
    }
}
