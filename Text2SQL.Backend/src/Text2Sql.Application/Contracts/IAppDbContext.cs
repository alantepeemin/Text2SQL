using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Application.Contracts
{
    /// <summary>
    /// Application katmanının veri erişim portu (Faz 1).
    ///
    /// Neden repository değil: EF Core DbContext zaten unit-of-work +
    /// repository'dir; üzerine generic repository koymak sorgu esnekliğini
    /// azaltır, test edilebilirliğe katkı sağlamaz (testler gerçek
    /// SQLite/Testcontainers ile koşar). Bu ince port, Application'ın
    /// Infrastructure'a derleme zamanı bağımlılığını keser — somut
    /// ApplicationDbContext yalnızca Infrastructure'da yaşar.
    ///
    /// Bilinçli tradeoff: Application, EF Core paketine bağımlıdır
    /// (DbSet/DatabaseFacade). Bu, pragmatik Clean Architecture'da kabul
    /// görmüş bir tavizdir (bkz. docs/adr).
    /// </summary>
    public interface IAppDbContext
    {
        DbSet<Company>            Companies          { get; }
        DbSet<User>               Users              { get; }
        DbSet<Membership>         Memberships        { get; }
        DbSet<Project>            Projects           { get; }
        DbSet<ProjectAccess>      ProjectAccesses    { get; }
        DbSet<ProjectDatabase>    ProjectDatabases   { get; }
        DbSet<QueryHistory>       QueryHistories     { get; }
        DbSet<UserToken>          UserTokens         { get; }
        DbSet<CompanyInvitation>  CompanyInvitations { get; }
        DbSet<RefreshToken>       RefreshTokens      { get; }
        DbSet<PendingRegistration> PendingRegistrations { get; }
        DbSet<AuditLog>           AuditLogs          { get; }
        DbSet<UsageRecord>        UsageRecords       { get; }
        DbSet<ApiKey>             ApiKeys            { get; }
        DbSet<Plan>               Plans              { get; }
        DbSet<PlanFeature>        PlanFeatures       { get; }
        DbSet<Subscription>       Subscriptions      { get; }
        DbSet<SchemaAnnotation>   SchemaAnnotations  { get; }
        DbSet<QueryFeedback>      QueryFeedbacks     { get; }
        DbSet<QueryJob>           QueryJobs          { get; }

        /// <summary>Transaction erişimi (BeginTransactionAsync vb.).</summary>
        DatabaseFacade Database { get; }

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
