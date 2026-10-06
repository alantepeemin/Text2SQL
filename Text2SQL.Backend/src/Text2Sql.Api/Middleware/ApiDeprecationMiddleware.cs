using Text2Sql.Api.Http;

namespace Text2Sql.Api.Middleware
{
    /// <summary>
    /// v1 uçlarını RFC 8594 uyumlu şekilde "kullanımdan kaldırılacak" olarak işaretler.
    ///
    /// Neden: v1'i sessizce yaşatmak, istemcilerin geçiş yapması için hiçbir sinyal
    /// üretmez ve eski sözleşme kalıcı hale gelir. Deprecation + Link başlıkları,
    /// istemci araçlarının (ve geliştiricinin ağ sekmesinin) uyarı gösterebilmesini
    /// sağlar. Yanıt gövdesi DEĞİŞMEZ — bu, kırılmayan bir iletişim kanalıdır.
    /// </summary>
    public class ApiDeprecationMiddleware
    {
        private readonly RequestDelegate _next;

        public ApiDeprecationMiddleware(RequestDelegate next) => _next = next;

        public Task InvokeAsync(HttpContext context)
        {
            var path = context.Request.Path.Value ?? string.Empty;

            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !context.IsV2())
            {
                context.Response.OnStarting(static state =>
                {
                    var ctx = (HttpContext)state;
                    ctx.Response.Headers["Deprecation"] = "true";
                    ctx.Response.Headers["Link"] =
                        "</api/v2>; rel=\"successor-version\"";
                    return Task.CompletedTask;
                }, context);
            }

            return _next(context);
        }
    }
}
