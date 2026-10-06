using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;
using Text2Sql.Domain.Common;
using Text2Sql.Application.Contracts;
using Text2Sql.Domain.Entities;

namespace Text2Sql.Infrastructure.Persistence
{
    /// <summary>
    /// Ana veritabanı bağlamı.
    /// 
    /// TENANT İZOLASYONU (SaaS-1):
    /// ITenantScoped uygulayan her entity otomatik global query filter alır.
    /// Servislerdeki explicit .Where(x => x.CompanyId == tenantId) çağrıları
    /// KALDIRILMAZ — derinlemesine savunmanın ikinci katmanıdır.
    /// </summary>
    public class ApplicationDbContext : DbContext, IDataProtectionKeyContext, IAppDbContext
    {
        private readonly ITenantContext? _tenantContext;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext? tenantContext = null)
            : base(options)
        {
            // tenantContext null olabilir: design-time (dotnet ef) ve bazı test
            // senaryolarında DI dışından örneklenir. Bu durumda filtre pasiftir.
            _tenantContext = tenantContext;
        }

        // ── SaaS-1: Global kiracı filtresi ───────────────────────────────────
        // EF, filtre ifadesindeki DbContext örnek üyelerini HER SORGUDA yeniden
        // değerlendirir (parametre olarak derler). Bu yüzden aşağıdaki property'ler
        // isteğe göre değişen kiracı bağlamını doğru yansıtır — model bir kez
        // oluşturulsa bile filtre "donmaz".
        private bool TenantFilterEnabled => _tenantContext != null && _tenantContext.HasTenant;
        private int  CurrentTenantId     => _tenantContext?.TenantId ?? 0;

        // Faz 0: DataProtection anahtarları DB'de kalıcı — container/makine
        // yenilendiğinde şifrelenmiş bağlantı dizeleri çözülebilir kalır.
        public DbSet<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey> DataProtectionKeys
            => Set<Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.DataProtectionKey>();

        public DbSet<Company>            Companies          => Set<Company>();
        public DbSet<User>               Users              => Set<User>();
        public DbSet<Membership>         Memberships        => Set<Membership>();
        public DbSet<Project>            Projects           => Set<Project>();
        public DbSet<ProjectAccess>      ProjectAccesses    => Set<ProjectAccess>();
        public DbSet<ProjectDatabase>    ProjectDatabases   => Set<ProjectDatabase>();
        public DbSet<QueryHistory>       QueryHistories     => Set<QueryHistory>();
        public DbSet<UserToken>          UserTokens         => Set<UserToken>();
        public DbSet<CompanyInvitation>  CompanyInvitations => Set<CompanyInvitation>();
        public DbSet<RefreshToken>       RefreshTokens      => Set<RefreshToken>();
        public DbSet<PendingRegistration> PendingRegistrations => Set<PendingRegistration>();
        public DbSet<AuditLog>           AuditLogs          => Set<AuditLog>();
        public DbSet<UsageRecord>        UsageRecords       => Set<UsageRecord>();
        public DbSet<ApiKey>             ApiKeys            => Set<ApiKey>();
        public DbSet<Plan>               Plans              => Set<Plan>();
        public DbSet<PlanFeature>        PlanFeatures       => Set<PlanFeature>();
        public DbSet<Subscription>       Subscriptions      => Set<Subscription>();
        public DbSet<SchemaAnnotation>   SchemaAnnotations  => Set<SchemaAnnotation>();
        public DbSet<QueryFeedback>      QueryFeedbacks     => Set<QueryFeedback>();
        public DbSet<QueryJob>           QueryJobs          => Set<QueryJob>();

        /// <summary>
        /// SaaS-5: Plan katalogu HasData ile seed edilir — migration'a gömülür,
        /// her ortamda (test/dev/prod) aynı katalog garantilenir. Sabit Id'ler
        /// bilinçli: seed verisi için deterministik anahtar gerekir.
        ///
        /// Limitler: 0 = sınırsız.
        /// </summary>
        private static void SeedPlans(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Plan>().HasData(
                new Plan
                {
                    Id = 1, Code = "free", Name = "Free",
                    MonthlyTokenLimit = 200_000,
                    MaxMembers = 3, MaxProjects = 3, MaxDataSources = 3, MaxApiKeys = 0,
                    MonthlyPriceUsd = 0m, IsActive = true, SortOrder = 1
                },
                new Plan
                {
                    Id = 2, Code = "pro", Name = "Pro",
                    MonthlyTokenLimit = 5_000_000,
                    MaxMembers = 25, MaxProjects = 20, MaxDataSources = 50, MaxApiKeys = 10,
                    MonthlyPriceUsd = 49m, IsActive = true, SortOrder = 2
                },
                new Plan
                {
                    Id = 3, Code = "enterprise", Name = "Enterprise",
                    MonthlyTokenLimit = 0, // sınırsız
                    MaxMembers = 0, MaxProjects = 0, MaxDataSources = 0, MaxApiKeys = 0,
                    MonthlyPriceUsd = 499m, IsActive = true, SortOrder = 3
                });

            modelBuilder.Entity<PlanFeature>().HasData(
                // Free: yalnızca temel kullanım (SQLite yükleme her planda mevcut)
                new PlanFeature { Id = 1, PlanId = 1, FeatureKey = "usage_reports", IsEnabled = true },

                // Pro: uzak veri kaynakları + API anahtarları + raporlar
                new PlanFeature { Id = 2, PlanId = 2, FeatureKey = "remote_datasources", IsEnabled = true },
                new PlanFeature { Id = 3, PlanId = 2, FeatureKey = "api_keys",           IsEnabled = true },
                new PlanFeature { Id = 4, PlanId = 2, FeatureKey = "usage_reports",      IsEnabled = true },

                // Enterprise: hepsi + denetim kaydı (uyum gereksinimi)
                new PlanFeature { Id = 5, PlanId = 3, FeatureKey = "remote_datasources", IsEnabled = true },
                new PlanFeature { Id = 6, PlanId = 3, FeatureKey = "api_keys",           IsEnabled = true },
                new PlanFeature { Id = 7, PlanId = 3, FeatureKey = "usage_reports",      IsEnabled = true },
                new PlanFeature { Id = 8, PlanId = 3, FeatureKey = "audit_logs",         IsEnabled = true });
        }

        /// <summary>
        /// ITenantScoped uygulayan tüm entity'lere global filtre uygular.
        /// Reflection ile yapılır ki yeni entity eklendiğinde burada bir şey
        /// değiştirmek gerekmesin (unutulma riski = sızıntı riski).
        /// </summary>
        private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
        {
            var method = typeof(ApplicationDbContext)
                .GetMethod(nameof(SetTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (entityType.BaseType != null) continue; // TPH türevleri
                if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType)) continue;

                method.MakeGenericMethod(entityType.ClrType)
                      .Invoke(this, new object[] { modelBuilder });
            }
        }

        private void SetTenantFilter<TEntity>(ModelBuilder modelBuilder)
            where TEntity : class, ITenantScoped
        {
            // !TenantFilterEnabled → kiracısız bağlam (login/kayıt/arka plan): filtre pasif.
            // Aksi halde yalnızca aktif kiracının satırları görünür.
            Expression<Func<TEntity, bool>> filter =
                e => !TenantFilterEnabled || e.CompanyId == CurrentTenantId;

            modelBuilder.Entity<TEntity>().HasQueryFilter(filter);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ── PendingRegistration (Faz 2) ───────────────────────────────
            modelBuilder.Entity<PendingRegistration>()
                .HasIndex(p => p.Token).IsUnique();

            // ── Company ────────────────────────────────────────────────────
            // Companies silindiğinde bağlı Projects silinsin (doğrudan ilişki, path yok)
            modelBuilder.Entity<Project>()
                .HasOne(p => p.Company)
                .WithMany(c => c.Projects)
                .HasForeignKey(p => p.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // ── User ───────────────────────────────────────────────────────
            // SQL Server Kısıtı (Error 1785):
            // Aynı tabloya birden fazla CASCADE/SET NULL yolu açılamaz.
            // Companies → Users zaten Cascade.
            // Users → Users (self-ref) için SET NULL KULLANMA → NO ACTION + ClientSetNull.
            // ClientSetNull: EF Core ilişkiyi uygulama tarafında null'a çeker,
            // DB constraint'i NO ACTION olur → döngüsel cascade hatası ortadan kalkar.
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(u => u.Email).IsUnique();

                // SaaS-8 (r2): Legacy "birincil organizasyon" bağı — SetNull.
                // Cascade OLMAMALI: organizasyon silinince kullanıcı hesabı
                // silinmez (başka organizasyonlarda üyeliği olabilir).
                entity.HasOne(u => u.Company)
                    .WithMany(c => c.Users)
                    .HasForeignKey(u => u.CompanyId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Self-referencing: kimin davet ettiği
                entity.HasOne(u => u.InvitedByUser)
                    .WithMany(u => u.InvitedUsers)
                    .HasForeignKey(u => u.InvitedBy)
                    .OnDelete(DeleteBehavior.ClientSetNull); // ← NO ACTION in DB

                // Self-referencing: kimin onayladığı
                entity.HasOne(u => u.ApprovedByUser)
                    .WithMany(u => u.ApprovedUsers)
                    .HasForeignKey(u => u.ApprovedBy)
                    .OnDelete(DeleteBehavior.ClientSetNull); // ← NO ACTION in DB
            });

            // ── CompanyInvitation ──────────────────────────────────────────
            modelBuilder.Entity<CompanyInvitation>(entity =>
            {
                entity.HasIndex(i => i.InvitationToken).IsUnique();

                entity.HasOne(i => i.Company)
                    .WithMany(c => c.Invitations)
                    .HasForeignKey(i => i.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Companies → Users (Cascade) + Users → CompanyInvitation (SetNull)
                // = çoklu path → ClientSetNull kullan
                entity.HasOne(i => i.InvitedByUser)
                    .WithMany(u => u.SentInvitations)
                    .HasForeignKey(i => i.InvitedBy)
                    .OnDelete(DeleteBehavior.ClientSetNull); // ← NO ACTION in DB
            });

            // ── UserToken — 1:1 ───────────────────────────────────────────
            // Companies → Users (Cascade) → UserToken (Cascade): tek yol, sorun yok
            modelBuilder.Entity<UserToken>()
                .HasOne(t => t.User)
                .WithOne(u => u.TokenInfo)
                .HasForeignKey<UserToken>(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // ── RefreshToken ───────────────────────────────────────────────
            // Companies → Users (Cascade) → RefreshToken (Cascade): tek yol, sorun yok
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.HasIndex(r => r.Token).IsUnique();

                entity.HasOne(r => r.User)
                    .WithMany(u => u.RefreshTokens)
                    .HasForeignKey(r => r.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ── ProjectAccess ──────────────────────────────────────────────
            modelBuilder.Entity<ProjectAccess>(entity =>
            {
                entity.HasIndex(pa => new { pa.ProjectId, pa.UserId }).IsUnique();

                // User → ProjectAccess (kullanıcı erişimi): Companies → Users → ProjectAccess
                // + Companies → Projects → ProjectAccess = iki ayrı path → NoAction
                entity.HasOne(pa => pa.User)
                    .WithMany(u => u.ProjectAccesses)
                    .HasForeignKey(pa => pa.UserId)
                    .OnDelete(DeleteBehavior.ClientNoAction); // ← NO ACTION in DB

                entity.HasOne(pa => pa.Project)
                    .WithMany(p => p.AccessList)
                    .HasForeignKey(pa => pa.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Kimin verdiği (GrantedBy): Companies → Users → ProjectAccess zaten var
                entity.HasOne(pa => pa.GrantedBy)
                    .WithMany()
                    .HasForeignKey(pa => pa.GrantedById)
                    .OnDelete(DeleteBehavior.ClientSetNull); // ← NO ACTION in DB
            });

            // ── QueryHistory ───────────────────────────────────────────────
            modelBuilder.Entity<QueryHistory>(entity =>
            {
                // Companies → Users → QueryHistory
                // + Companies → Projects → QueryHistory = iki path → NoAction
                entity.HasOne(q => q.User)
                    .WithMany(u => u.QueryHistories)
                    .HasForeignKey(q => q.UserId)
                    .OnDelete(DeleteBehavior.ClientNoAction); // ← NO ACTION in DB

                entity.HasOne(q => q.Project)
                    .WithMany(p => p.QueryHistories)
                    .HasForeignKey(q => q.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Veritabanı silindiğinde history'de null bırak
                entity.HasOne(q => q.Database)
                    .WithMany()
                    .HasForeignKey(q => q.DatabaseId)
                    .OnDelete(DeleteBehavior.ClientSetNull); // ← NO ACTION in DB
            });

            // ── Release: Asenkron sorgu işleri ─────────────────────────────
            modelBuilder.Entity<QueryJob>(entity =>
            {
                entity.HasIndex(j => new { j.CompanyId, j.CreatedAt });
                entity.HasIndex(j => j.Status);
                entity.Property(j => j.Status).HasMaxLength(20).IsRequired();
                entity.Property(j => j.Question).HasMaxLength(2000).IsRequired();
                entity.Property(j => j.ErrorMessage).HasMaxLength(1000);
                entity.Ignore(j => j.IsTerminal);
            });

            // ── SaaS-9: Şema sözlüğü ve sorgu geri bildirimi ───────────────
            modelBuilder.Entity<SchemaAnnotation>(entity =>
            {
                // Aynı tablo/kolon için tek açıklama (upsert deseni)
                entity.HasIndex(a => new { a.DataSourceId, a.TableName, a.ColumnName }).IsUnique();
                entity.Property(a => a.TableName).HasMaxLength(128).IsRequired();
                entity.Property(a => a.ColumnName).HasMaxLength(128);
                entity.Property(a => a.Description).HasMaxLength(500).IsRequired();

                entity.HasOne(a => a.DataSource)
                    .WithMany()
                    .HasForeignKey(a => a.DataSourceId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Ignore(a => a.IsTableLevel);
            });

            modelBuilder.Entity<QueryFeedback>(entity =>
            {
                // Kullanıcı başına tek geri bildirim (güncellenebilir)
                entity.HasIndex(f => new { f.QueryHistoryId, f.UserId }).IsUnique();
                entity.Property(f => f.CorrectedSql).HasMaxLength(4000);
                entity.Property(f => f.Comment).HasMaxLength(1000);

                entity.HasOne(f => f.QueryHistory)
                    .WithMany()
                    .HasForeignKey(f => f.QueryHistoryId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ── SaaS-5: Plan, özellik ve abonelik ──────────────────────────
            modelBuilder.Entity<Plan>(entity =>
            {
                entity.HasIndex(p => p.Code).IsUnique();
                entity.Property(p => p.Code).HasMaxLength(30).IsRequired();
                entity.Property(p => p.Name).HasMaxLength(100).IsRequired();
                entity.Property(p => p.MonthlyPriceUsd).HasPrecision(18, 2);
                entity.Property(p => p.ExternalPriceId).HasMaxLength(100);
            });

            modelBuilder.Entity<PlanFeature>(entity =>
            {
                entity.HasIndex(f => new { f.PlanId, f.FeatureKey }).IsUnique();
                entity.Property(f => f.FeatureKey).HasMaxLength(60).IsRequired();

                entity.HasOne(f => f.Plan)
                    .WithMany(p => p.Features)
                    .HasForeignKey(f => f.PlanId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Subscription>(entity =>
            {
                entity.HasIndex(s => s.CompanyId);
                entity.Property(s => s.Status).HasMaxLength(20).IsRequired();
                entity.Property(s => s.ExternalSubscriptionId).HasMaxLength(100);

                entity.HasOne(s => s.Company)
                    .WithMany()
                    .HasForeignKey(s => s.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Plan silinmesi abonelikleri kırmasın — plan pasifleştirilir, silinmez
                entity.HasOne(s => s.Plan)
                    .WithMany()
                    .HasForeignKey(s => s.PlanId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            SeedPlans(modelBuilder);

            // ── SaaS-6: API anahtarları ────────────────────────────────────
            modelBuilder.Entity<ApiKey>(entity =>
            {
                // Kimlik doğrulama sorgusu: Prefix (indeks) + KeyHash karşılaştırması.
                // Sadece hash ile arama da mümkündü; önek indeksi tam tablo
                // taramasını önler ve hash karşılaştırmasını tek satıra indirir.
                entity.HasIndex(k => k.Prefix);
                entity.HasIndex(k => k.KeyHash).IsUnique();
                entity.HasIndex(k => k.CompanyId);

                entity.Property(k => k.Name).HasMaxLength(100).IsRequired();
                entity.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
                entity.Property(k => k.Prefix).HasMaxLength(20).IsRequired();
                entity.Property(k => k.Scopes).HasMaxLength(1000).IsRequired();

                entity.HasOne(k => k.Company)
                    .WithMany()
                    .HasForeignKey(k => k.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Ignore(k => k.ScopeList);
            });

            // ── SaaS-3: Denetim kaydı ve kullanım ölçümü ───────────────────
            modelBuilder.Entity<AuditLog>(entity =>
            {
                // Sorgu deseni: "bu organizasyonun son olayları"
                entity.HasIndex(a => new { a.CompanyId, a.OccurredAt });
                entity.HasIndex(a => a.Action);
                entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
                entity.Property(a => a.TargetType).HasMaxLength(50);
                entity.Property(a => a.TargetId).HasMaxLength(50);
                entity.Property(a => a.Summary).HasMaxLength(500);
                entity.Property(a => a.ActorEmail).HasMaxLength(256);
                entity.Property(a => a.IpAddress).HasMaxLength(64);
                entity.Property(a => a.UserAgent).HasMaxLength(256);
            });

            modelBuilder.Entity<UsageRecord>(entity =>
            {
                // Kota ve raporlama sorgularının ana indeksi
                entity.HasIndex(u => new { u.CompanyId, u.OccurredAt });
                entity.HasIndex(u => new { u.CompanyId, u.UserId, u.OccurredAt });
                entity.Property(u => u.Type).HasMaxLength(30).IsRequired();
                entity.Property(u => u.Model).HasMaxLength(120);
                entity.Property(u => u.EstimatedCostUsd).HasPrecision(18, 8);
                entity.Ignore(u => u.TotalTokens); // hesaplanan alan
            });

            // ── SaaS-2: Membership ─────────────────────────────────────────
            modelBuilder.Entity<Membership>(entity =>
            {
                // Bir kullanıcı bir organizasyona yalnızca bir kez üye olabilir
                entity.HasIndex(m => new { m.UserId, m.CompanyId }).IsUnique();
                entity.HasIndex(m => m.CompanyId);

                entity.HasOne(m => m.User)
                    .WithMany(u => u.Memberships)
                    .HasForeignKey(m => m.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Company → Users (Cascade) + Company → Memberships (Cascade)
                // PostgreSQL çoklu cascade yolunu destekler (SQL Server kısıtı yok).
                entity.HasOne(m => m.Company)
                    .WithMany(c => c.Memberships)
                    .HasForeignKey(m => m.CompanyId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ── SaaS-1: Kiracı sorgu performansı için indeksler ────────────
            modelBuilder.Entity<QueryHistory>().HasIndex(q => new { q.CompanyId, q.CreatedAt });
            modelBuilder.Entity<ProjectAccess>().HasIndex(pa => pa.CompanyId);
            modelBuilder.Entity<Project>().HasIndex(p => p.CompanyId);

            // ── ProjectDatabase ────────────────────────────────────────────
            modelBuilder.Entity<ProjectDatabase>(entity =>
            {
                entity.HasIndex(db => new { db.ProjectId, db.ConnectionName });

                entity.HasOne(db => db.Project)
                    .WithMany(p => p.Databases)
                    .HasForeignKey(db => db.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ── SaaS-1: Global kiracı filtreleri EN SONDA uygulanır ────────
            // (tüm entity'ler modele eklendikten sonra)
            ApplyTenantQueryFilters(modelBuilder);
        }
    }
}
