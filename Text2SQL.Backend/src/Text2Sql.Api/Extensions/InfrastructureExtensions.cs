using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Text2Sql.Api.Auth;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

namespace Text2Sql.Api.Extensions
{
    public static class AuthExtensions
    {
        public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            // SaaS-6: İki kimlik türü aynı uçlarda desteklenir. Policy scheme,
            // isteğe bakarak doğru şemaya yönlendirir — controller'lara
            // AuthenticationSchemes yazmak gerekmez (tek nokta, unutma riski yok).
            services
                .AddAuthentication("MultiAuth")
                .AddPolicyScheme("MultiAuth", "JWT veya API anahtarı", options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        if (context.Request.Headers.ContainsKey(ApiKeyDefaults.HeaderName))
                            return ApiKeyDefaults.Scheme;

                        var auth = context.Request.Headers.Authorization.ToString();
                        if (auth.StartsWith("Bearer " + ApiKeyDefaults.KeyPrefix, StringComparison.Ordinal))
                            return ApiKeyDefaults.Scheme;

                        return JwtBearerDefaults.AuthenticationScheme;
                    };
                })
                .AddJwtBearer()
                .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                    ApiKeyDefaults.Scheme, displayName: "API anahtarı", configureOptions: null);

            // FAZ 2 DÜZELTMESİ: Ayarlar kayıt anında SABİTLENMEZ — JwtBearerOptions
            // ilk kimlik doğrulamada lazily çözülür ve o anki NİHAİ config'i okur.
            // Böylece token'ı imzalayan (runtime) ve doğrulayan (options) taraf her
            // zaman aynı secret'ı görür; test/host override'ları da doğru çalışır.
            // Eksik/kısa secret kontrolü: JwtOptions.ValidateOnStart (fail-fast).
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
                .Configure<IConfiguration>((options, config) =>
                {
                    var secret = config["JwtSettings:SecretKey"]
                        ?? throw new InvalidOperationException(
                            "JwtSettings:SecretKey yapılandırılmamış. Environment variable veya user-secrets ile sağlayın.");

                    if (Encoding.UTF8.GetByteCount(secret) < 32)
                        throw new InvalidOperationException(
                            "JwtSettings:SecretKey en az 32 byte (256 bit) olmalıdır.");

                    var issuer   = config["JwtSettings:Issuer"]   ?? "Text2SQL.API";
                    var audience = config["JwtSettings:Audience"] ?? "Text2SQL.Client";

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer           = true,
                        ValidIssuer              = issuer,
                        ValidateAudience         = true,
                        ValidAudience            = audience,
                        ValidateIssuerSigningKey  = true,
                        IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                        ValidateLifetime         = true,
                        RoleClaimType            = System.Security.Claims.ClaimTypes.Role,
                        NameClaimType            = System.Security.Claims.ClaimTypes.Name,
                        ClockSkew                = TimeSpan.Zero
                    };
                });

            return services;
        }
    }

    public static class SwaggerExtensions
    {
        public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Text2SQL API", Version = "v1" });

                var jwtScheme = new OpenApiSecurityScheme
                {
                    Name        = "Authorization",
                    Type        = SecuritySchemeType.Http,
                    Scheme      = "bearer",
                    BearerFormat = "JWT",
                    In          = ParameterLocation.Header,
                    Description = "Bearer {token}"
                };

                c.AddSecurityDefinition("Bearer", jwtScheme);
                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    { jwtScheme, Array.Empty<string>() }
                });
            });

            return services;
        }
    }

    public static class CorsExtensions
    {
        public static IServiceCollection AddReactCors(this IServiceCollection services, IConfiguration configuration)
        {
            // SaaS-7: Çoklu origin desteği (staging/prod/özel domain).
            // Cors:AllowedOrigins (dizi) tercih edilir; tek origin'lik eski
            // ayar (Cors:AllowedOrigin) geriye uyumluluk için desteklenir.
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();

            if (origins == null || origins.Length == 0)
            {
                var single = configuration["Cors:AllowedOrigin"];
                origins = string.IsNullOrWhiteSpace(single)
                    ? new[] { "http://localhost:3001" }
                    : new[] { single };
            }

            services.AddCors(options =>
            {
                options.AddPolicy("AllowReact", policy =>
                {
                    // AllowAnyOrigin BİLİNÇLİ OLARAK kullanılmıyor: kimlik
                    // bilgisi taşıyan isteklerde origin beyaz listesi şarttır.
                    policy.WithOrigins(origins)
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });

            return services;
        }
    }
}
