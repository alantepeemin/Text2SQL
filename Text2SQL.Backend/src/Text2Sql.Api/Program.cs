using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Text2Sql.Api.Extensions;
using Text2Sql.Api.Middleware;
using Text2Sql.Api.Options;
using Text2Sql.Application.Validators;
using Text2Sql.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// NOT (Faz 2 düzeltmesi): appsettings/env/user-secrets kaynaklarını elle YENİDEN
// EKLEMİYORUZ — WebApplicationBuilder bunları zaten doğru öncelik sırasıyla ekler.
// Elle eklemek, test/host override'larının önceliğini bozuyordu.

// Faz 2: Yapısal loglama — çıktı biçimi ve sink'ler config'ten ("Serilog" bölümü)
builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services
    .AddPersistence(builder.Configuration)
    .AddJwtAuthentication(builder.Configuration)
    .AddSwaggerWithJwt()
    .AddReactCors(builder.Configuration)
    .AddApplicationServices(builder.Configuration)
    .AddOpenRouterHttpClient(builder.Configuration)
    .AddAppRateLimiting(builder.Configuration)          // Faz 2
    .AddStartupOptionsValidation(builder.Configuration); // Faz 2: fail-fast config

// Faz 2: FluentValidation — Application'daki tüm validator'lar otomatik devrede
builder.Services
    .AddValidatorsFromAssemblyContaining<LoginDtoValidator>()
    .AddFluentValidationAutoValidation();

// API v2: Controller'lar çıplak veri döndürür; v1 zarfı bu filter ile eklenir.
// Tek controller kodu iki sözleşmeyi de besler (bkz. ResponseEnvelopeFilter).
builder.Services.AddControllers(options =>
{
    options.Filters.Add<Text2Sql.Api.Filters.ResponseEnvelopeFilter>();
});

// Faz 5: Health checks — liveness (süreç ayakta mı) / readiness (DB erişilebilir mi)
builder.Services.AddHealthChecks()
    .AddDbContextCheck<Text2Sql.Infrastructure.Persistence.ApplicationDbContext>(
        "database", tags: new[] { "ready" });

// Faz 5: OpenTelemetry — config ile açılır (OpenTelemetry:Enabled).
// Özel span'ler (schema.extract / llm.generate / sql.execute) QueryService'te:
// LLM gecikmesi ↔ DB gecikmesi ayrımı artık ölçülebilir.
if (builder.Configuration.GetValue("OpenTelemetry:Enabled", false))
{
    var otlpEndpoint = new Uri(
        builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? "http://localhost:4317");

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("Text2Sql.Api"))
        .WithTracing(t => t
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("Text2Sql.Query")
            .AddOtlpExporter(o => o.Endpoint = otlpEndpoint))
        .WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddMeter("Text2Sql")
            .AddOtlpExporter(o => o.Endpoint = otlpEndpoint));
}

var app = builder.Build();

// Swagger sadece geliştirme ortamında
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Middleware sırası önemli!
app.UseMiddleware<ExceptionMiddleware>();       // 1. Hataları yakala
app.UseSerilogRequestLogging();                 // 2. İstek logları (TraceId ile)
app.UseCors("AllowReact");                      // 3. CORS
app.UseRateLimiter();                           // 4. Rate limiting (Faz 2)
app.UseAuthentication();                        // 5. Kimlik doğrulama
app.UseAuthorization();                         // 6. Yetkilendirme
app.UseMiddleware<SecurityStampMiddleware>();   // 7. Oturum damgası kontrolü (Faz 2)
app.UseMiddleware<ApiDeprecationMiddleware>();   // 8. v1 uçlarına Deprecation başlığı

// SaaS-7: DataProtection anahtar şifrelemesi yapılandırılmadıysa görünür uyarı.
// Sessiz güvenlik açığı bırakmamak için başlangıçta bir kez loglanır.
if (string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:CertificatePath"]))
{
    app.Logger.LogWarning(
        "DataProtection anahtarları ŞİFRESİZ saklanıyor. Üretimde " +
        "DataProtection:CertificatePath ayarlayın — DB yedeği sızarsa müşteri " +
        "veritabanı bağlantı dizeleri açılabilir.");
}

app.MapControllers();

// Faz 5: /health (legacy, korunur) + /health/live + /health/ready
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }))
   .AllowAnonymous();
app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

app.Run();

// Entegrasyon testlerinin WebApplicationFactory<Program> ile
// uygulamayı ayağa kaldırabilmesi için gerekli işaretleyici.
public partial class Program { }
