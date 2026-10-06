using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Text2Sql.Application.Contracts;
using Text2Sql.Application.Services;

namespace Text2Sql.Api.Auth
{
    public static class ApiKeyDefaults
    {
        public const string Scheme     = "ApiKey";
        public const string HeaderName = "X-Api-Key";
        public const string KeyPrefix  = "t2s_";

        /// <summary>API anahtarının verdiği izinler bu claim tipiyle taşınır.</summary>
        public const string ScopeClaimType = "scope";

        public const string ApiKeyIdClaimType = "apiKeyId";

        /// <summary>Görüntüleme öneki uzunluğu — ApiKeyService ile aynı olmalı.</summary>
        public const int DisplayPrefixLength = 12;
    }

    /// <summary>
    /// SaaS-6: API anahtarı kimlik doğrulaması.
    ///
    /// Kabul edilen biçimler:
    ///   X-Api-Key: t2s_live_...
    ///   Authorization: Bearer t2s_live_...
    ///
    /// Üretilen principal'da rol claim'i YOKTUR — yetki yalnızca anahtarın
    /// scope'larından gelir (en-az-yetki). Böylece admin'in ürettiği bir anahtar
    /// bile yalnızca kendisine verilen izinleri kullanabilir.
    /// </summary>
    public sealed class ApiKeyAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IAppDbContext _context;

        public ApiKeyAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IAppDbContext context)
            : base(options, logger, encoder)
        {
            _context = context;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var sunulanAnahtar = AnahtariCoz();
            if (string.IsNullOrEmpty(sunulanAnahtar))
                return AuthenticateResult.NoResult();

            var hash = ApiKeyService.Hash(sunulanAnahtar);

            // NOT: Aralık ifadesi ([..12]) LINQ lambda'sının İÇİNDE kullanılamaz —
            // EF ifade ağacına çeviremez (CS8790/CS8792). Önek bu yüzden sorgudan
            // ÖNCE yerel değişkene alınır.
            var arananOnek = sunulanAnahtar[..ApiKeyDefaults.DisplayPrefixLength];

            // Kimlik doğrulama, kiracı bağlamı OLUŞMADAN ÖNCE çalışır; global
            // filtre bu noktada pasiftir. Yine de niyeti açık kılmak için
            // IgnoreQueryFilters yazıldı: anahtarın hangi kiracıya ait olduğunu
            // ancak anahtarı bulduktan sonra öğreniyoruz.
            var apiKey = await _context.ApiKeys
                .IgnoreQueryFilters()
                .Include(k => k.Company)
                .FirstOrDefaultAsync(k => k.Prefix == arananOnek && k.KeyHash == hash);

            if (apiKey == null)
            {
                Logger.LogWarning("Geçersiz API anahtarı sunuldu (önek: {Prefix})",
                    arananOnek);
                return AuthenticateResult.Fail("Geçersiz API anahtarı.");
            }

            if (apiKey.IsRevoked)
                return AuthenticateResult.Fail("API anahtarı iptal edilmiş.");

            if (apiKey.IsExpired)
                return AuthenticateResult.Fail("API anahtarının süresi dolmuş.");

            if (!apiKey.Company.IsActive)
                return AuthenticateResult.Fail("Organizasyon hesabı pasif.");

            // Son kullanım zamanı — izleme ve terk edilmiş anahtar tespiti için.
            // Her istekte yazmak yerine dakikada bir güncelleniyor (yazma yükü).
            if (apiKey.LastUsedAt == null || DateTime.UtcNow - apiKey.LastUsedAt > TimeSpan.FromMinutes(1))
            {
                await _context.ApiKeys
                    .IgnoreQueryFilters()
                    .Where(k => k.Id == apiKey.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow));
            }

            var claims = new List<Claim>
            {
                new("tenantId", apiKey.CompanyId.ToString()),
                // Ölçüm ve denetim kayıtlarının bir kullanıcıya bağlanabilmesi için
                // anahtarı oluşturan kişi aktör kabul edilir (audit özetinde anahtar
                // adı da geçer, böylece "insan mı anahtar mı" ayrımı korunur).
                new("userId", apiKey.CreatedByUserId.ToString()),
                new("username", $"apikey:{apiKey.Name}"),
                new(ApiKeyDefaults.ApiKeyIdClaimType, apiKey.Id.ToString()),
                new(ClaimTypes.Name, $"apikey:{apiKey.Name}")
            };

            claims.AddRange(apiKey.ScopeList.Select(s => new Claim(ApiKeyDefaults.ScopeClaimType, s)));

            var identity = new ClaimsIdentity(claims, ApiKeyDefaults.Scheme);
            var principal = new ClaimsPrincipal(identity);

            return AuthenticateResult.Success(
                new AuthenticationTicket(principal, ApiKeyDefaults.Scheme));
        }

        private string? AnahtariCoz()
        {
            if (Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var headerValues))
            {
                var value = headerValues.ToString().Trim();
                if (value.StartsWith(ApiKeyDefaults.KeyPrefix, StringComparison.Ordinal) && value.Length >= ApiKeyDefaults.DisplayPrefixLength)
                    return value;
            }

            var auth = Request.Headers.Authorization.ToString();
            const string bearer = "Bearer ";
            if (auth.StartsWith(bearer, StringComparison.OrdinalIgnoreCase))
            {
                var token = auth[bearer.Length..].Trim();
                if (token.StartsWith(ApiKeyDefaults.KeyPrefix, StringComparison.Ordinal) && token.Length >= ApiKeyDefaults.DisplayPrefixLength)
                    return token;
            }

            return null;
        }
    }
}
