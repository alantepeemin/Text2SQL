using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Text2Sql.Application.Contracts;
using Text2Sql.Infrastructure.Persistence;

namespace Text2Sql.Infrastructure.Extensions
{
    public static class PersistenceExtensions
    {
        public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
        {
            // Faz 1: SQL Server → PostgreSQL (onaylanan mimari karar).
            // Migration'lar Npgsql hedefiyle sıfırdan oluşturulur.
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
                options.ConfigureWarnings(w =>
                    w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
            });

            // Application katmanı somut context'i değil portu görür
            services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<ApplicationDbContext>());

            return services;
        }
    }
}
