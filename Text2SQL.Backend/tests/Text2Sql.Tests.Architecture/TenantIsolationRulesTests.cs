using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Text2Sql.Domain.Common;
using Text2Sql.Infrastructure.Persistence;
using Text2Sql.Tests.Common.Fixtures;
using Xunit;

namespace Text2Sql.Tests.Architecture
{
    /// <summary>
    /// SaaS-1 — Kiracı izolasyonunun YAPISAL güvencesi.
    ///
    /// Bu testlerin amacı tek tek endpoint'leri denemek değil; yeni kod
    /// eklendiğinde korumanın kendiliğinden devam ettiğini garanti etmektir.
    /// </summary>
    public class TenantIsolationRulesTests
    {
        [Fact]
        public void ITenantScoped_UygulayanHerEntity_GlobalFiltreyeSahip()
        {
            using var factory = new TestApp();
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var eksik = db.Model.GetEntityTypes()
                .Where(e => e.BaseType == null)
                .Where(e => typeof(ITenantScoped).IsAssignableFrom(e.ClrType))
                .Where(e => e.GetQueryFilter() == null)
                .Select(e => e.ClrType.Name)
                .ToList();

            Assert.True(eksik.Count == 0,
                "Global kiracı filtresi olmayan tenant entity'leri: " + string.Join(", ", eksik));
        }

        [Fact]
        public void KiracıyaAitOlmasıBeklenenEntityler_ITenantScopedUyguluyor()
        {
            // Regresyon kapısı: bu entity'lerden birinden arayüz kaldırılırsa
            // filtre sessizce kaybolur — test bunu yakalar.
            // SaaS-2: User artık ITenantScoped DEĞİL (hesap organizasyondan bağımsız);
            // kiracı izolasyonu Membership üzerinden sağlanır.
            var beklenen = new[] { "Project", "ProjectDatabase", "QueryHistory",
                                   "ProjectAccess", "CompanyInvitation", "Membership",
                                   "AuditLog", "UsageRecord",   // SaaS-3
                                   "ApiKey",                    // SaaS-6
                                   "Subscription",              // SaaS-5
                                   "SchemaAnnotation", "QueryFeedback",  // SaaS-9
                                   "QueryJob" };                // Release: asenkron sorgu

            var domainAssembly = typeof(ITenantScoped).Assembly;
            foreach (var ad in beklenen)
            {
                var tip = domainAssembly.GetTypes().Single(t => t.Name == ad);
                Assert.True(typeof(ITenantScoped).IsAssignableFrom(tip),
                    $"{ad} artık ITenantScoped uygulamıyor — kiracı filtresi kaybolur!");
            }
        }
    }
}
