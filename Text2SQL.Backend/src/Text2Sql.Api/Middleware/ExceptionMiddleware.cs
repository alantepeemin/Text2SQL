using System.Net;
using Text2Sql.Api.Http;
using Text2Sql.Application.Common;
using Text2Sql.Application.Common.Exceptions;

namespace Text2Sql.Api.Middleware
{
    /// <summary>
    /// Merkezî hata işleme — SÜRÜME DUYARLI.
    ///
    /// v1: { success:false, message } (eski sözleşme aynen korunur)
    /// v2: RFC 7807 ProblemDetails + "code" (makine-okunur) + "traceId"
    ///
    /// Neden iki biçim: v1 istemcileri kırılmadan v2'de standart, araç
    /// dostu bir hata sözleşmesi sunmak için. v1 emekliye ayrıldığında
    /// yalnızca alt dal silinir.
    /// </summary>
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IWebHostEnvironment _env;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IWebHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (AppException ex)
            {
                // Beklenen alan hatası: stack trace'siz, Warning seviyesinde
                _logger.LogWarning(
                    "Alan hatası ({Code}/{Status}) — TraceId: {TraceId} | Path: {Path} | Message: {Message}",
                    ex.Code, ex.StatusCode, context.TraceIdentifier, context.Request.Path, ex.Message);

                await YanitYazAsync(context, ex.StatusCode, ex.Code, ex.Message);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // İstemci bağlantıyı kopardı — hata değil. 499 (Nginx konvansiyonu).
                _logger.LogDebug("İstek iptal edildi — Path: {Path}", context.Request.Path);
                if (!context.Response.HasStarted) context.Response.StatusCode = 499;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "İşlenmemiş hata — TraceId: {TraceId} | Path: {Path} | Message: {Message}",
                    context.TraceIdentifier, context.Request.Path, ex.Message);

                var (status, code, message) = EslesmeBul(ex, context);
                await YanitYazAsync(context, status, code, message);
            }
        }

        /// <summary>
        /// BCL exception tiplerinin eski eşlemeleri — geçiş dönemi.
        /// Yeni kod AppException türevleri kullanmalıdır.
        /// </summary>
        private (int Status, string Code, string Message) EslesmeBul(Exception ex, HttpContext context) => ex switch
        {
            UnauthorizedAccessException => ((int)HttpStatusCode.Forbidden, "FORBIDDEN", ex.Message),
            ArgumentException           => ((int)HttpStatusCode.BadRequest, "BAD_REQUEST", ex.Message),
            InvalidOperationException   => ((int)HttpStatusCode.BadRequest, "BAD_REQUEST", ex.Message),
            KeyNotFoundException        => ((int)HttpStatusCode.NotFound, "NOT_FOUND", ex.Message),
            FileNotFoundException       => ((int)HttpStatusCode.NotFound, "NOT_FOUND", "İstenen kaynak bulunamadı."),
            NotSupportedException       => ((int)HttpStatusCode.BadRequest, "NOT_SUPPORTED", ex.Message),

            // Üretimde iç hata detayı istemciye sızdırılmaz
            _ when _env.IsProduction() => ((int)HttpStatusCode.InternalServerError, "INTERNAL_ERROR",
                                            $"Sunucu hatası. Referans: {context.TraceIdentifier}"),
            _ => ((int)HttpStatusCode.InternalServerError, "INTERNAL_ERROR", ex.Message)
        };

        private static Task YanitYazAsync(HttpContext context, int status, string code, string message)
            => ApiErrorWriter.WriteAsync(context, status, code, message);
    }
}
