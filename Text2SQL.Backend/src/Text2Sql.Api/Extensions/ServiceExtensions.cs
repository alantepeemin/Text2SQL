using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Text2Sql.Infrastructure.BackgroundServices;
using Text2Sql.Application.Contracts;
using Text2Sql.Infrastructure.Persistence;
using Text2Sql.Api.Auth;
using Text2Sql.Application.Services;
using Text2Sql.Application.Queries;
using Text2Sql.Infrastructure.Providers;
using Text2Sql.Infrastructure.Audit;
using Text2Sql.Infrastructure.Email;
using Text2Sql.Infrastructure.Services;
using Text2Sql.Infrastructure.Storage;
using Text2Sql.Infrastructure.Usage;

namespace Text2Sql.Api.Extensions
{
    public static class ServiceExtensions
    {
        public static IServiceCollection AddApplicationServices(
            this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpContextAccessor();

            // Release: HTTP dışı bağlamlar için ambient kiracı bağlamı
            // (asenkron sorgu worker'ı bunu kullanır — kiracı izolasyonu korunur)
            services.AddSingleton<IAmbientContext, AmbientContext>();

            // SaaS-4: İzin tabanlı yetkilendirme — "perm:{izin}" politikaları
            // dinamik üretilir; yeni izin eklemek tek sabit satırıdır.
            services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
            services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

            // Kullanıcı bağlamı
            services.AddScoped<CurrentUserContext>();
            services.AddScoped<ICurrentUserContext>(sp => sp.GetRequiredService<CurrentUserContext>());
            // SaaS-1: DbContext global kiracı filtresi bu portu kullanır.
            // Aynı scoped örnek paylaşılır — istek içinde tutarlı kiracı bağlamı.
            services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<CurrentUserContext>());

            // Domain servisleri
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ICompanyService, CompanyService>();
            services.AddScoped<IProjectService, ProjectService>();
            services.AddScoped<IProjectDatabaseService, ProjectDatabaseService>();
            services.AddScoped<IQueryService, QueryService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IUsageService, UsageService>();
            services.AddScoped<ITenantDataService, TenantDataService>(); // SaaS-8
            services.AddScoped<IApiKeyService, ApiKeyService>(); // SaaS-6

            // SaaS-5: Plan/abonelik — tek sınıf üç portu uygular (aynı cache'i
            // paylaşmaları için bilinçli; ayrı sınıflar plan sorgusunu 3 kez atardı)
            services.AddScoped<PlanService>();
            services.AddScoped<IPlanService>(sp => sp.GetRequiredService<PlanService>());
            services.AddScoped<IFeatureGate>(sp => sp.GetRequiredService<PlanService>());
            services.AddScoped<IPlanLimitService>(sp => sp.GetRequiredService<PlanService>());

            // SaaS-3: Denetim kaydı ve kullanım ölçümü
            services.AddScoped<IAuditWriter, AuditWriter>();
            services.AddScoped<IUsageQuotaService, UsageQuotaService>();
            services.AddSingleton<ILlmCostCalculator, LlmCostCalculator>();

            // Altyapı servisleri
            services.AddScoped<IConnectionTester, ConnectionTester>();
            services.AddScoped<ISqlGeneratorService, OpenRouterSqlGenerator>();

            // Faz 3: Sorgu motoru — provider stratejisi + güvenlik doğrulayıcısı
            services.AddScoped<IDataSourceProvider, SqliteDataSourceProvider>();
            services.AddScoped<IDataSourceProvider, PostgresDataSourceProvider>();
            services.AddScoped<IDataSourceProviderFactory, DataSourceProviderFactory>();
            services.AddSingleton<ISqlStatementValidator, SqlStatementValidator>();

            // SaaS-9: Maliyet ve doğruluk optimizasyonu
            services.AddSingleton<ISchemaOptimizer, SchemaOptimizer>();
            services.AddScoped<ISchemaDictionaryService, SchemaDictionaryService>();
            services.AddScoped<IQueryFeedbackService, QueryFeedbackService>();

            // Release: Asenkron sorgu (202 + durum sorgulama)
            services.AddScoped<IQueryJobService, QueryJobService>();
            services.AddSingleton<IQueryJobQueue, InMemoryQueryJobQueue>();

            // Faz 4: dosya deposu + e-posta (Email:Enabled=false → NoOp, sistem
            // SMTP kurulumu olmadan da çalışır; davetiye linki yine üretilir)
            services.AddSingleton<IFileStorage, LocalFileStorage>();
            services.AddScoped<SmtpEmailSender>();
            services.AddScoped<NoOpEmailSender>();
            services.AddScoped<IEmailSender>(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                return config.GetValue("Email:Enabled", false)
                    ? sp.GetRequiredService<SmtpEmailSender>()
                    : sp.GetRequiredService<NoOpEmailSender>();
            });

            // Arka plan işleri
            services.AddHostedService<TokenResetBackgroundService>();
            services.AddHostedService<DataRetentionBackgroundService>(); // SaaS-8
            services.AddHostedService<QueryJobWorker>();                  // Release

            // Önbellek
            services.AddMemoryCache();

            // Veri koruma (connection string şifreleme)
            // Faz 0: Anahtarlar DB'de kalıcı. Aksi halde container/makine
            // yenilendiğinde tüm şifreli bağlantı dizeleri çözülemez olurdu.
            // SaaS-7: Anahtarlar DB'de KALICI ve (yapılandırılırsa) ŞİFRELİ.
            //
            // Sorun: anahtarlar şifresiz XML olarak saklanıyordu. DB yedeği
            // sızarsa müşterilerin uzak DB bağlantı dizeleri açılabilirdi.
            // Çözüm: X.509 sertifikasıyla anahtar şifreleme (DataProtection:
            // CertificatePath + CertificatePassword). Sertifika verilmezse
            // eski davranış sürer ve BAŞLANGIÇTA UYARI loglanır — sessiz
            // güvenlik açığı yerine görünür bir eksik.
            var dataProtection = services.AddDataProtection()
                    .SetApplicationName("Text2SQL")
                    .PersistKeysToDbContext<ApplicationDbContext>();

            var certPath = configuration["DataProtection:CertificatePath"];
            if (!string.IsNullOrWhiteSpace(certPath) && File.Exists(certPath))
            {
                var certPassword = configuration["DataProtection:CertificatePassword"];
                var certificate = System.Security.Cryptography.X509Certificates
                    .X509CertificateLoader.LoadPkcs12FromFile(certPath, certPassword);

                dataProtection.ProtectKeysWithCertificate(certificate);
            }

            return services;
        }

        public static IServiceCollection AddOpenRouterHttpClient(this IServiceCollection services, IConfiguration configuration)
        {
            // FAZ 2 DÜZELTMESİ: ApiKey kayıt anında sabitlenmez — istemci her
            // oluşturulduğunda nihai config'ten okunur (test/host override uyumlu).
            // Eksik key kontrolü: OpenRouterOptions.ValidateOnStart (fail-fast).
            services.AddHttpClient("OpenRouter")
                .ConfigureHttpClient((sp, client) =>
                {
                    var config = sp.GetRequiredService<IConfiguration>();
                    var apiKey = config["OpenRouter:ApiKey"]
                        ?? throw new InvalidOperationException("OpenRouter:ApiKey yapılandırması eksik.");

                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    client.DefaultRequestHeaders.Add("User-Agent", "Text2SQLApp/1.0");
                    // Zaman aşımı yönetimi resilience handler'da — çifte timeout olmasın
                    client.Timeout = TimeSpan.FromSeconds(150);
                })
                // Faz 3: LLM sağlayıcı kesintisi doğrudan 500'e dönüşmesin —
                // retry (2 deneme) + deneme başına 60 sn timeout + circuit breaker.
                .AddStandardResilienceHandler(options =>
                {
                    options.AttemptTimeout.Timeout       = TimeSpan.FromSeconds(60);
                    options.TotalRequestTimeout.Timeout  = TimeSpan.FromSeconds(130);
                    options.Retry.MaxRetryAttempts       = 2;
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
                });

            return services;
        }
    }
}
