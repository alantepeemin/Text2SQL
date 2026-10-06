namespace Text2Sql.Domain.Authorization
{
    /// <summary>
    /// SaaS-4: Granüler izinler — yetkilendirmenin tek doğruluk kaynağı.
    ///
    /// Neden: Üç string rol (admin/manager/user) SaaS'ın ihtiyacını karşılamıyordu.
    /// Plan bazlı kısıtlar, API anahtarı scope'ları (SaaS-6) ve ileride özel roller
    /// ancak izin modeliyle mümkün. Ayrıca "bu endpoint'e kim erişebilir?" sorusunun
    /// cevabı artık attribute'ta okunabiliyor — rol listesi tahmin etmek gerekmiyor.
    ///
    /// Adlandırma: {kaynak}.{eylem} — API anahtarı scope'larıyla birebir eşlenecek.
    /// </summary>
    public static class Permissions
    {
        // ── Üye yönetimi ─────────────────────────────────────────────────────
        public const string MembersRead    = "members.read";
        public const string MembersInvite  = "members.invite";
        public const string MembersApprove = "members.approve";
        public const string MembersManage  = "members.manage";   // rol değiştir / çıkar

        // ── Organizasyon ─────────────────────────────────────────────────────
        public const string OrganizationRead   = "organization.read";
        public const string OrganizationManage = "organization.manage"; // kod üret, ayarlar

        /// <summary>SaaS-8: KVKK/GDPR — veri ihracı ve organizasyon silme (yalnızca admin).</summary>
        public const string OrganizationExport = "organization.export";
        public const string OrganizationDelete = "organization.delete";

        // ── Projeler ─────────────────────────────────────────────────────────
        public const string ProjectsRead   = "projects.read";
        public const string ProjectsCreate = "projects.create";
        public const string ProjectsManage = "projects.manage";  // tüm projeleri görme (admin)

        // ── Veri kaynakları ──────────────────────────────────────────────────
        public const string DataSourcesRead   = "datasources.read";
        public const string DataSourcesManage = "datasources.manage";

        /// <summary>SaaS-9: Şema sözlüğü (semantic layer) düzenleme.</summary>
        public const string SchemaDictionaryManage = "schema.dictionary.manage";

        // ── Sorgu ────────────────────────────────────────────────────────────
        public const string QueriesExecute = "queries.execute";
        public const string QueriesRead    = "queries.read";

        // ── Kullanım / denetim / faturalama ──────────────────────────────────
        public const string UsageRead   = "usage.read";
        public const string AuditRead   = "audit.read";
        public const string BillingManage = "billing.manage";     // SaaS-5

        // ── API anahtarları (SaaS-6) ─────────────────────────────────────────
        public const string ApiKeysRead   = "apikeys.read";
        public const string ApiKeysManage = "apikeys.manage";

        /// <summary>Tüm izinler (doğrulama ve test amaçlı).</summary>
        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            MembersRead, MembersInvite, MembersApprove, MembersManage,
            OrganizationRead, OrganizationManage, OrganizationExport, OrganizationDelete,
            ProjectsRead, ProjectsCreate, ProjectsManage,
            DataSourcesRead, DataSourcesManage, SchemaDictionaryManage,
            QueriesExecute, QueriesRead,
            UsageRead, AuditRead, BillingManage,
            ApiKeysRead, ApiKeysManage
        };
    }

    /// <summary>Organizasyon rolleri — üyelikte saklanır.</summary>
    public static class Roles
    {
        public const string Admin   = "admin";
        public const string Manager = "manager";
        public const string User    = "user";

        public static readonly IReadOnlySet<string> All =
            new HashSet<string> { Admin, Manager, User };
    }

    /// <summary>
    /// Rol → izin kümesi eşlemesi.
    ///
    /// Bilinçli tasarım: eşleme KODDA sabit tutuldu (veritabanında değil).
    /// Gerekçe: (a) izin listesi kodla birlikte sürümlenir ve gözden geçirilebilir,
    /// (b) yanlış yapılandırma ile ayrıcalık yükseltme riski ortadan kalkar,
    /// (c) özel rol ihtiyacı doğduğunda tabloya taşımak yalnızca bu sınıfı etkiler.
    ///
    /// Mevcut davranışla geriye uyumluluk: admin bugün her şeye erişiyordu →
    /// tüm izinleri alır. manager proje oluşturabiliyordu → o izni alır.
    /// user yalnızca kendi projelerinde çalışıyordu → okuma + sorgu izinleri.
    /// </summary>
    public static class RolePermissions
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
            new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [Roles.Admin] = Permissions.All,

                [Roles.Manager] = new HashSet<string>
                {
                    Permissions.MembersRead,
                    Permissions.OrganizationRead,
                    Permissions.ProjectsRead, Permissions.ProjectsCreate,
                    Permissions.DataSourcesRead, Permissions.DataSourcesManage,
                    Permissions.SchemaDictionaryManage,
                    Permissions.QueriesExecute, Permissions.QueriesRead,
                    Permissions.UsageRead
                },

                [Roles.User] = new HashSet<string>
                {
                    Permissions.OrganizationRead,
                    Permissions.ProjectsRead,
                    Permissions.DataSourcesRead,
                    Permissions.QueriesExecute, Permissions.QueriesRead
                }
            };

        public static IReadOnlySet<string> For(string? role)
            => role != null && Map.TryGetValue(role, out var perms)
                ? perms
                : new HashSet<string>();

        public static bool Has(string? role, string permission)
            => For(role).Contains(permission);
    }
}
