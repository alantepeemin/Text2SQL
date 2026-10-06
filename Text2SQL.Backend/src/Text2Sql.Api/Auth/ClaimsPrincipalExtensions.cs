using System.Security.Claims;

namespace Text2Sql.Api.Auth
{
    /// <summary>
    /// SaaS-4 (r2) — KRİTİK BULGU: JWT kütüphanesi "role" claim'ini yeniden adlandırır.
    ///
    /// JwtSecurityTokenHandler.DefaultInboundClaimTypeMap, "role" ve "roles" kısa
    /// adlarını otomatik olarak ClaimTypes.Role'a
    /// ("http://schemas.microsoft.com/ws/2008/06/identity/claims/role") map eder.
    /// Sonuç: token'a "role" claim'i yazsak bile doğrulama sonrası
    /// User.FindFirst("role") HER ZAMAN NULL döner.
    ///
    /// Bu, projede baştan beri var olan sessiz bir hataya yol açmıştı:
    /// ICurrentUserContext.Role boş string olduğu için IsAdmin daima false'tu →
    /// "admin her projeye erişir" kuralı hiç çalışmıyordu. Fark edilmemesinin
    /// nedeni, admin'in kendi oluşturduğu projelerde zaten Owner iznine sahip
    /// olmasıydı. SaaS-4'te yetkilendirme role bağlanınca hata yüzeye çıktı.
    ///
    /// Çözüm: rolü tek bir yerden, her iki claim adını da deneyerek okumak.
    /// (Alternatif olan MapInboundClaims=false, "sub"/"unique_name" gibi diğer
    /// standart eşlemeleri de kapatacağı için bilinçli olarak tercih edilmedi.)
    /// </summary>
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>Aktif organizasyondaki rol (admin/manager/user).</summary>
        public static string? GetOrganizationRole(this ClaimsPrincipal? principal)
            => principal?.FindFirst(ClaimTypes.Role)?.Value
               ?? principal?.FindFirst("role")?.Value;
    }
}
