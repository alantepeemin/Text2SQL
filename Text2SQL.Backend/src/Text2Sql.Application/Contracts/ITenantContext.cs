namespace Text2Sql.Application.Contracts
{
    /// <summary>
    /// SaaS-1: Persistence katmanının ihtiyaç duyduğu minimal kiracı bağlamı.
    ///
    /// ICurrentUserContext'ten ayrı tutulur çünkü DbContext'in kullanıcı rolü/durumu
    /// gibi bilgilere ihtiyacı yoktur (ISP) ve arka plan işleri kiracısız çalışır.
    ///
    /// HasTenant=false olduğunda global filtreler DEVRE DIŞI kalır. Bu bilinçlidir:
    /// kimlik doğrulaması yapılmamış akışlar (login, kayıt, davet kabulü) ve arka plan
    /// servisleri kiracı bağlamı olmadan çalışmak zorundadır. Bu akışlarda güvenlik,
    /// servis içindeki explicit kontrollerle sağlanır.
    /// </summary>
    public interface ITenantContext
    {
        bool HasTenant { get; }
        int TenantId { get; }
    }
}
