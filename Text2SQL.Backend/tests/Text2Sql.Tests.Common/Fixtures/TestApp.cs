using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Text2Sql.Application.Contracts;
using Text2Sql.Infrastructure.Persistence;
using Text2Sql.Tests.Common.Fakes;

namespace Text2Sql.Tests.Common.Fixtures
{
    /// <summary>
    /// Test host'u. Üretim boru hattının TAMAMI çalışır (middleware, filtreler,
    /// yetkilendirme); yalnızca iki şey değiştirilir:
    ///
    ///   1. Uygulama veritabanı → izole geçici SQLite dosyası
    ///   2. LLM → deterministik sahte üreteç
    ///
    /// Yapılandırma <see cref="Settings"/> ile test bazında ezilebilir; böylece
    /// "hız sınırı 2/dk olsaydı ne olurdu" gibi senaryolar ayrı bir host ile
    /// kurulabilir ve diğer testleri etkilemez.
    /// </summary>
    public class TestApp : WebApplicationFactory<Program>
    {
        private readonly string _dbPath =
            Path.Combine(Path.GetTempPath(), $"t2s_test_{Guid.NewGuid():N}.db");

        /// <summary>Bu host'a özel dosya deposu kökü.</summary>
        public string StorageRoot { get; } =
            Path.Combine(Path.GetTempPath(), $"t2s_storage_{Guid.NewGuid():N}");

        /// <summary>Varsayılan ayarları ezmek için alt sınıflarda doldurulur.</summary>
        protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>();

        /// <summary>Bu host'a ait sahte LLM durumu.</summary>
        public FakeLlmState Llm => Services.GetRequiredService<FakeLlmState>();

        /// <summary>Kapsamlı (scoped) bir DbContext ile doğrudan veritabanına bakar.</summary>
        public async Task<T> QueryDatabaseAsync<T>(Func<ApplicationDbContext, Task<T>> sorgu)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await sorgu(db);
        }

        /// <summary>Doğrudan veritabanına yazar (senaryo hazırlığı için).</summary>
        public async Task ModifyDatabaseAsync(Func<ApplicationDbContext, Task> islem)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await islem(db);
            await db.SaveChangesAsync();
        }

        public T GetService<T>() where T : notnull
        {
            using var scope = Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<T>();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                var ayarlar = new Dictionary<string, string?>
                {
                    // ≥ 32 byte — fail-fast kuralını karşılar
                    ["JwtSettings:SecretKey"] = "TEST-ONLY-secret-key-0123456789ABCDEF-0123456789",
                    ["JwtSettings:Issuer"]    = "Text2SQL.API",
                    ["JwtSettings:Audience"]  = "Text2SQL.Client",
                    ["OpenRouter:ApiKey"]     = "test-api-key-not-used",
                    ["Storage:SqliteRoot"]    = StorageRoot,
                    ["Storage:ResultsRoot"]   = Path.Combine(StorageRoot, "results"),
                    ["ConnectionStrings:DefaultConnection"] = "unused-in-tests",

                    // Hız sınırı varsayılan olarak devre dışı; sınırı test eden
                    // host bunu Settings ile geri açar.
                    ["RateLimiting:AuthPerMinute"]  = "100000",
                    ["RateLimiting:QueryPerMinute"] = "100000",

                    // Ölçüm testleri için bilinen fiyat ve kota katsayısı
                    ["Usage:TokensPerQueryAllowance"] = "3000",
                    ["Usage:Pricing:test/fake-model:InputPer1M"]  = "1.00",
                    ["Usage:Pricing:test/fake-model:OutputPer1M"] = "2.00"
                };

                foreach (var (anahtar, deger) in Settings)
                    ayarlar[anahtar] = deger;

                cfg.AddInMemoryCollection(ayarlar);
            });

            builder.ConfigureServices(services =>
            {
                // Üretim veritabanı kaydını sök (EF9: options + IDbContextOptionsConfiguration)
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

                services.AddDbContext<ApplicationDbContext>(o =>
                {
                    o.UseSqlite($"Data Source={_dbPath}");
                    o.ConfigureWarnings(w => w.Ignore(
                        CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
                });

                services.RemoveAll<ISqlGeneratorService>();
                services.AddSingleton<FakeLlmState>();
                services.AddScoped<ISqlGeneratorService, FakeSqlGenerator>();

                using var scope = services.BuildServiceProvider().CreateScope();
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                     .Database.EnsureCreated();
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(_dbPath); } catch { /* best effort */ }
            try { Directory.Delete(StorageRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
