using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Text2Sql.Api.Http;

namespace Text2Sql.Api.Extensions
{
    /// <summary>
    /// Faz 2: Yerleşik .NET RateLimiter.
    /// "auth"  — IP başına: login brute-force ve kayıt spam'ine karşı.
    /// "query" — kullanıcı başına: LLM maliyet koruması (kota'dan bağımsız ani yük freni).
    /// Limitler config'ten gelir; testler yüksek değerle etkisizleştirir.
    /// </summary>
    public static class RateLimitingExtensions
    {
        public static IServiceCollection AddAppRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // FAZ 2 DÜZELTMESİ: Limitler kayıt anında sabitlenmez — partition ilk
                // oluşturulurken nihai config'ten okunur (test override'ları çalışır).
                options.AddPolicy("auth", httpContext =>
                {
                    var limit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
                        .GetValue("RateLimiting:AuthPerMinute", 10);
                    return RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limit,
                            Window      = TimeSpan.FromMinutes(1),
                            QueueLimit  = 0
                        });
                });

                options.AddPolicy("query", httpContext =>
                {
                    var limit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
                        .GetValue("RateLimiting:QueryPerMinute", 30);
                    // SaaS-6: API anahtarı isteklerinde limit ANAHTAR bazında —
                    // aynı anahtarı oluşturan kullanıcının tarayıcı oturumu
                    // anahtarın kotasını tüketmesin (ve tersi).
                    return RateLimitPartition.GetFixedWindowLimiter(
                        httpContext.User.FindFirst("apiKeyId")?.Value is { Length: > 0 } keyId
                            ? "apikey:" + keyId
                            : httpContext.User.FindFirst("userId")?.Value
                                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                                ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limit,
                            Window      = TimeSpan.FromMinutes(1),
                            QueueLimit  = 0
                        });
                });

                options.OnRejected = async (context, cancellationToken) =>
                {
                    await ApiErrorWriter.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status429TooManyRequests,
                        "RATE_LIMITED",
                        "Çok fazla istek. Lütfen bir dakika sonra tekrar deneyin.");
                };
            });

            return services;
        }
    }
}
