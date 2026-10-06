using System.ComponentModel.DataAnnotations;

namespace Text2Sql.Api.Options
{
    /// <summary>
    /// Faz 2: Startup'ta doğrulanan yapılandırma modelleri.
    /// Eksik/hatalı config → uygulama hiç ayağa kalkmaz (fail-fast),
    /// üretimde ilk istekte patlamaz.
    /// </summary>
    public class JwtOptions
    {
        public const string Section = "JwtSettings";

        [Required, MinLength(32, ErrorMessage = "SecretKey en az 32 karakter olmalıdır.")]
        public string SecretKey { get; set; } = string.Empty;

        public string Issuer   { get; set; } = "Text2SQL.API";
        public string Audience { get; set; } = "Text2SQL.Client";

        [Range(5, 1440)]
        public int AccessTokenExpiryMinutes { get; set; } = 60;

        [Range(1, 90)]
        public int RefreshTokenExpiryDays { get; set; } = 7;
    }

    public class OpenRouterOptions
    {
        public const string Section = "OpenRouter";

        [Required]
        public string ApiKey { get; set; } = string.Empty;

        [Required]
        public string Model { get; set; } = "anthropic/claude-3.5-sonnet";

        [Range(5, 300)]
        public int TimeoutSeconds { get; set; } = 60;
    }

    public static class OptionsExtensions
    {
        public static IServiceCollection AddStartupOptionsValidation(
            this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions<JwtOptions>()
                .Bind(configuration.GetSection(JwtOptions.Section))
                .ValidateDataAnnotations()
                .Validate(o => !o.SecretKey.StartsWith("REPLACE_"),
                    "JwtSettings:SecretKey placeholder değeriyle çalıştırılamaz — gerçek bir secret sağlayın.")
                .ValidateOnStart();

            services.AddOptions<OpenRouterOptions>()
                .Bind(configuration.GetSection(OpenRouterOptions.Section))
                .ValidateDataAnnotations()
                .Validate(o => !o.ApiKey.StartsWith("REPLACE_"),
                    "OpenRouter:ApiKey placeholder değeriyle çalıştırılamaz — gerçek bir API key sağlayın.")
                .ValidateOnStart();

            return services;
        }
    }
}
