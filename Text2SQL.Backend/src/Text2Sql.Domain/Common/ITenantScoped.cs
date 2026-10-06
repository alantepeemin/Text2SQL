namespace Text2Sql.Domain.Common
{
    /// <summary>
    /// SaaS-1: Bir kiracıya (organizasyona) ait olan entity'leri işaretler.
    ///
    /// Bu arayüzü uygulayan HER entity, ApplicationDbContext'te otomatik olarak
    /// global query filter alır — geliştirici elle Where yazmayı unutsa bile
    /// kiracılar arası veri sızıntısı oluşmaz (derinlemesine savunmanın 1. katmanı;
    /// mevcut explicit filtreler 2. katman olarak korunur).
    ///
    /// Filtreyi zorunlu kılan mimari test: TenantIsolationArchitectureTests.
    /// Yeni bir tenant entity'si eklenip bu arayüz uygulanmazsa test kırmızıya döner.
    /// </summary>
    public interface ITenantScoped
    {
        /// <summary>Sahip kiracının kimliği (Company/Organization).</summary>
        int CompanyId { get; }
    }
}
