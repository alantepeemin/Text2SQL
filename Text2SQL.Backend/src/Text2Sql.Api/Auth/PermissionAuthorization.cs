using Microsoft.AspNetCore.Authorization;
using Text2Sql.Domain.Authorization;

namespace Text2Sql.Api.Auth
{
    /// <summary>
    /// SaaS-4: Endpoint'in gerektirdiği izni bildirir.
    /// Kullanım: [RequirePermission(Permissions.MembersInvite)]
    ///
    /// Rol listesi yerine izin yazmanın faydası: endpoint'in gereksinimi
    /// okunur hale gelir ve rol→izin haritası değiştiğinde controller'lara
    /// dokunmak gerekmez.
    /// </summary>
    public sealed class RequirePermissionAttribute : AuthorizeAttribute
    {
        public const string PolicyPrefix = "perm:";

        public RequirePermissionAttribute(string permission)
            => Policy = PolicyPrefix + permission;
    }

    /// <summary>
    /// "perm:{izin}" politikalarını dinamik üretir — her izin için elle
    /// AddPolicy yazmak gerekmez (yeni izin eklemek tek sabit satırı).
    /// </summary>
    public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
    {
        private readonly DefaultAuthorizationPolicyProvider _fallback;

        public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)
            => _fallback = new DefaultAuthorizationPolicyProvider(options);

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();
        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            if (policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            {
                var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];

                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permission))
                    .Build();

                return Task.FromResult<AuthorizationPolicy?>(policy);
            }

            return _fallback.GetPolicyAsync(policyName);
        }
    }

    public sealed class PermissionRequirement : IAuthorizationRequirement
    {
        public PermissionRequirement(string permission) => Permission = permission;
        public string Permission { get; }
    }

    /// <summary>
    /// İzin kontrolü: kullanıcının AKTİF ORGANİZASYONDAKİ rolü (token'daki "role"
    /// claim'i, SaaS-2'de üyelikten üretiliyor) istenen izne sahip mi?
    ///
    /// Not: Rolün hâlâ geçerli olduğu SecurityStampMiddleware tarafından ayrıca
    /// doğrulanır (üyelik iptal/rol değişiminde damga yenilenir) — bu handler
    /// yalnızca "bu rol bu izne sahip mi" sorusuna cevap verir.
    /// </summary>
    public sealed class PermissionAuthorizationHandler
        : AuthorizationHandler<PermissionRequirement>
    {
        private readonly ILogger<PermissionAuthorizationHandler> _logger;

        public PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
            => _logger = logger;

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            // SaaS-6: İki kimlik türü desteklenir.
            // 1) API anahtarı → yetki YALNIZCA anahtarın scope'larından gelir
            //    (rol claim'i yoktur; en-az-yetki ilkesi).
            // 2) Kullanıcı JWT'si → yetki aktif organizasyondaki rolden gelir.
            var scopes = context.User.FindAll(ApiKeyDefaults.ScopeClaimType)
                                     .Select(c => c.Value)
                                     .ToList();

            if (scopes.Count > 0)
            {
                if (scopes.Contains(requirement.Permission, StringComparer.OrdinalIgnoreCase))
                    context.Succeed(requirement);
                else
                    _logger.LogWarning(
                        "İzin reddi (API anahtarı) — Gerekli izin: {Permission}, Anahtar scope'ları: {Scopes}",
                        requirement.Permission, string.Join(' ', scopes));

                return Task.CompletedTask;
            }

            // "role" claim'i JWT tarafından ClaimTypes.Role'a map edilir —
            // bkz. ClaimsPrincipalExtensions'taki açıklama.
            var role = context.User.GetOrganizationRole();

            if (RolePermissions.Has(role, requirement.Permission))
            {
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning(
                    "İzin reddi — Rol: {Role}, Gerekli izin: {Permission}",
                    role ?? "(yok)", requirement.Permission);
            }

            return Task.CompletedTask;
        }
    }
}
