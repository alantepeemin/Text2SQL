using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Text2Sql.Application.Common;

namespace Text2Sql.Api.Http
{
    /// <summary>
    /// Hata gövdelerinin TEK yazıcısı.
    ///
    /// Neden gerekli: hata yanıtı üç ayrı yerden yazılıyordu (exception
    /// middleware, oturum damgası middleware'i, hız sınırlayıcı geri çağrımı).
    /// Sonuç: v2 istemcisi bazı hatalarda ProblemDetails, bazılarında eski
    /// zarf alıyordu — yani "koda dallan" sözü tutulmuyordu. Artık sürüm
    /// duyarlı biçimlendirme tek yerde.
    /// </summary>
    public static class ApiErrorWriter
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public static async Task WriteAsync(
            HttpContext context, int status, string code, string message)
        {
            if (context.Response.HasStarted) return;

            context.Response.StatusCode = status;

            if (context.IsV2())
            {
                context.Response.ContentType = "application/problem+json";

                var problem = new ProblemDetails
                {
                    Status   = status,
                    Title    = BasligiBul(status),
                    Detail   = message,
                    Instance = context.Request.Path,
                    Type     = $"https://docs.text2sql.local/errors/{code.ToLowerInvariant()}"
                };
                problem.Extensions["code"]    = code;
                problem.Extensions["traceId"] = context.TraceIdentifier;

                await context.Response.WriteAsync(JsonSerializer.Serialize(problem, JsonOptions));
            }
            else
            {
                context.Response.ContentType = "application/json";
                var response = ApiResponse<object>.FailResponse(message);
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
            }
        }

        public static string BasligiBul(int status) => status switch
        {
            400 => "Geçersiz istek",
            401 => "Kimlik doğrulama gerekli",
            402 => "Paket yükseltmesi gerekli",
            403 => "Yetkisiz erişim",
            404 => "Bulunamadı",
            409 => "Çakışma",
            429 => "Limit aşıldı",
            _   => "Sunucu hatası"
        };
    }
}
