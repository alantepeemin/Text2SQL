namespace Text2Sql.Domain.Billing
{
    /// <summary>
    /// SaaS-5: Ürün özellikleri (feature) — plan bazlı kapılar bunlara bağlanır.
    /// İzinlerden (Permissions) FARKLIDIR: izin "bu kişi yapabilir mi",
    /// özellik "bu organizasyonun paketi bunu içeriyor mu" sorusunu yanıtlar.
    /// İkisi birlikte çalışır: yetkili kullanıcı, planı kapsamıyorsa yapamaz.
    /// </summary>
    public static class Features
    {
        /// <summary>Uzak veri kaynağı (PostgreSQL vb.) bağlama — SQLite yükleme her planda var.</summary>
        public const string RemoteDataSources = "remote_datasources";

        /// <summary>Programatik erişim anahtarları.</summary>
        public const string ApiKeys = "api_keys";

        /// <summary>Denetim kaydı erişimi (uyum gereksinimi olan müşteriler).</summary>
        public const string AuditLogs = "audit_logs";

        /// <summary>Kullanım/maliyet raporları.</summary>
        public const string UsageReports = "usage_reports";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            RemoteDataSources, ApiKeys, AuditLogs, UsageReports
        };
    }

    /// <summary>Sistem planları — kodda tanımlı, veritabanında satır olarak tutulur.</summary>
    public static class PlanCodes
    {
        public const string Free       = "free";
        public const string Pro        = "pro";
        public const string Enterprise = "enterprise";
    }
}
