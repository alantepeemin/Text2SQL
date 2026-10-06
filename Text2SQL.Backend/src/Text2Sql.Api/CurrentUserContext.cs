using Text2Sql.Application.Contracts;

namespace Text2Sql.Api.Auth
{
    public class CurrentUserContext : ICurrentUserContext, ITenantContext
    {
        private readonly IHttpContextAccessor _http;
        private readonly IAmbientContext _ambient;

        public CurrentUserContext(IHttpContextAccessor http, IAmbientContext ambient)
        {
            _http = http;
            _ambient = ambient;
        }

        /// <summary>
        /// HTTP isteği varsa token claim'leri, yoksa ambient bağlam (arka plan işi).
        /// Bu sıralama kritik: HTTP isteği içinde ambient'a bakmak, bir isteğin
        /// başka bir işin bağlamını miras almasına yol açabilirdi.
        /// </summary>
        private AmbientScope? Ambient =>
            _http.HttpContext == null ? _ambient.Current : null;

        public bool HasTenant
        {
            get
            {
                var user = _http.HttpContext?.User;
                if (user?.Identity?.IsAuthenticated == true)
                    return user.HasClaim(c => c.Type == "tenantId")
                           && user.HasClaim(c => c.Type == "userId");

                // Arka plan işi: ambient bağlam kiracıyı taşır → global
                // kiracı filtresi ÇALIŞIR (izolasyon korunur).
                return Ambient != null;
            }
        }

        public int TenantId => _http.HttpContext != null
            ? GetClaimAsInt("tenantId")
            : Ambient?.TenantId ?? 0;

        public int UserId => _http.HttpContext != null
            ? GetClaimAsInt("userId")
            : Ambient?.UserId ?? 0;
        // SaaS-4 (r2): "role" claim'i JWT tarafından ClaimTypes.Role'a map edilir.
        // Eskiden yalnızca "role" aranıyordu → Role daima boş → IsAdmin daima false.
        // Bu, "admin her projeye erişir" kuralının hiç çalışmamasına yol açıyordu.
        public string Role => _http.HttpContext?.User.GetOrganizationRole()
                              ?? Ambient?.Role
                              ?? string.Empty;
        public string Status => GetClaim("status") ?? string.Empty;
        public bool IsApproved => Status == "approved";
        public bool IsAdmin => Role == "admin";

        private string? GetClaim(string claimType)
            => _http.HttpContext?.User?.Claims
                .FirstOrDefault(c => c.Type == claimType)?.Value;

        private int GetClaimAsInt(string claimType)
        {
            var val = GetClaim(claimType);
            return int.TryParse(val, out var n) ? n : 0;
        }
    }
}
