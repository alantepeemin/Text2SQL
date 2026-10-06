using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Text.Json;
using Text2Sql.Application.Common;
using Text2Sql.Application.Contracts;

using Text2Sql.Api.Http;

namespace Text2Sql.Api.Middleware
{
    /// <summary>
    /// Faz 2: UserStatusMiddleware'in yerini alır.
    ///
    /// Eski tasarım her authenticated istekte DB'ye gidiyordu. Yeni tasarım:
    /// token'daki "sstamp" claim'i, kullanıcının DB'deki SecurityStamp değeriyle
    /// karşılaştırılır; DB durumu 60 sn cache'lenir. Suspend/rol değişikliği
    /// stamp'i yeniler VE cache'i düşürür → tek instance'ta kesinti anındadır,
    /// DB yükü ~%99 azalır. Excluded-path listesi tamamen kalktı (yalnızca
    /// authenticated istekler kontrol edilir).
    /// </summary>
    public class SecurityStampMiddleware
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SecurityStampMiddleware> _logger;

        private sealed record UserSecurityState(string SecurityStamp, bool IsActive);
        private sealed record MembershipState(bool IsActive, string Status);

        public SecurityStampMiddleware(
            RequestDelegate next,
            IMemoryCache cache,
            ILogger<SecurityStampMiddleware> logger)
        {
            _next   = next;
            _cache  = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!(context.User.Identity?.IsAuthenticated ?? false))
            {
                await _next(context);
                return;
            }

            // SaaS-6: API anahtarı kimliğinde kullanıcı oturumu yoktur —
            // anahtarın geçerliliği (iptal/süre/organizasyon) kimlik doğrulama
            // aşamasında zaten her istekte DB'den kontrol edilir.
            if (context.User.HasClaim(c => c.Type == "apiKeyId"))
            {
                await _next(context);
                return;
            }

            var userIdClaim = context.User.FindFirst("userId")?.Value;
            if (!int.TryParse(userIdClaim, out var userId))
            {
                await _next(context);
                return;
            }

            var tokenStamp = context.User.FindFirst("sstamp")?.Value;
            int.TryParse(context.User.FindFirst("tenantId")?.Value, out var tenantId);

            // 1) Hesap düzeyi durum (organizasyondan bağımsız)
            var state = await _cache.GetOrCreateAsync(
                CacheKeys.SecurityStamp(userId),
                async entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                    var db = context.RequestServices.GetRequiredService<IAppDbContext>();
                    return await db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => new UserSecurityState(u.SecurityStamp, u.IsActive))
                        .FirstOrDefaultAsync();
                });

            // 2) SaaS-2: Aktif organizasyondaki ÜYELİK hâlâ geçerli mi?
            // IgnoreQueryFilters: bu kontrol kiracı filtresinden ÖNCE gelmeli
            // (filtre zaten bu tenantId'ye göre çalışır; UserId kısıtı güvencedir).
            MembershipState? membership = null;
            if (tenantId > 0)
            {
                membership = await _cache.GetOrCreateAsync(
                    CacheKeys.Membership(userId, tenantId),
                    async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                        var db = context.RequestServices.GetRequiredService<IAppDbContext>();
                        return await db.Memberships
                            .IgnoreQueryFilters()
                            .AsNoTracking()
                            .Where(m => m.UserId == userId && m.CompanyId == tenantId)
                            .Select(m => new MembershipState(m.IsActive, m.Status))
                            .FirstOrDefaultAsync();
                    });
            }

            string? rejectMessage = null;

            if (state == null)
                rejectMessage = "Kullanıcı bulunamadı.";
            else if (!state.IsActive)
                rejectMessage = "Hesabınız devre dışı bırakılmıştır.";
            else if (tokenStamp == null || tokenStamp != state.SecurityStamp)
                rejectMessage = "Oturumunuz geçersiz kılınmış. Lütfen yeniden giriş yapın.";
            else if (membership == null)
                rejectMessage = "Bu organizasyonda üyeliğiniz bulunmuyor.";
            else if (!membership.IsActive)
                rejectMessage = "Bu organizasyondaki üyeliğiniz devre dışı bırakılmıştır.";
            else if (membership.Status == "suspended")
                rejectMessage = "Bu organizasyondaki üyeliğiniz askıya alınmıştır.";
            else if (membership.Status != "approved")
                rejectMessage = "Üyeliğiniz henüz onaylanmamış.";

            if (rejectMessage != null)
            {
                _logger.LogWarning(
                    "Engellenen erişim — UserId: {UserId}, Sebep: {Reason}", userId, rejectMessage);

                // Sürüm duyarlı tek yazıcı: v2 istemcisi burada da
                // ProblemDetails + makine-okunur kod alır.
                await ApiErrorWriter.WriteAsync(
                    context, (int)HttpStatusCode.Forbidden, "SESSION_INVALIDATED", rejectMessage);
                return;
            }

            await _next(context);
        }
    }
}
