using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Text2Sql.Application.Common;
using Text2Sql.Domain.Entities;
using Text2Sql.Infrastructure.Persistence;
using Text2Sql.Tests.Common.Fixtures;

namespace Text2Sql.Tests.Common.Builders
{
    /// <summary>
    /// Plan/abonelik kurulumu. Ödeme sağlayıcısı test kapsamı dışında olduğu
    /// için abonelik doğrudan veritabanında ayarlanır; testin hangi paketi
    /// gerektirdiği böylece TEK satırda ve açıkça görünür.
    /// </summary>
    public static class Plans
    {
        /// <summary>Organizasyon ADI ile yükseltir (aynı adlı en yeni kayıt).</summary>
        public static async Task UpgradeAsync(TestApp app, string organizationName, string planCode)
        {
            var companyId = await app.QueryDatabaseAsync(db => db.Companies
                .IgnoreQueryFilters()
                .Where(c => c.Name == organizationName)
                .OrderByDescending(c => c.Id)
                .Select(c => c.Id)
                .FirstAsync());

            await UpgradeAsync(app, companyId, planCode);
        }

        public static async Task UpgradeAsync(TestApp app, int companyId, string planCode)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var plan = await db.Plans.FirstAsync(p => p.Code == planCode);

            var subscription = await db.Subscriptions
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.CompanyId == companyId);

            if (subscription == null)
            {
                db.Subscriptions.Add(new Subscription
                {
                    CompanyId = companyId,
                    PlanId = plan.Id,
                    Status = SubscriptionStatuses.Active,
                    CurrentPeriodStart = DateTime.UtcNow
                });
            }
            else
            {
                subscription.PlanId = plan.Id;
                subscription.Status = SubscriptionStatuses.Active;
            }

            await db.SaveChangesAsync();

            // Plan önbelleği (60 sn) düşürülmezse test eski planı görür
            scope.ServiceProvider.GetRequiredService<IMemoryCache>()
                 .Remove(CacheKeys.Plan(companyId));
        }
    }
}
