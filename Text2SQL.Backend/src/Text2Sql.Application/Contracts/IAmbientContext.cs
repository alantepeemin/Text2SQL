namespace Text2Sql.Application.Contracts
{
    /// <summary>
    /// HTTP dışı bağlamlarda (arka plan işleri) kiracı/kullanıcı bağlamı taşır.
    ///
    /// NEDEN GEREKLİ: Asenkron sorgu worker'ının HttpContext'i yoktur. Bu olmadan
    /// global kiracı filtresi pasif kalır ve _currentUser.UserId = 0 olurdu —
    /// yani arka planda çalışan sorgu KİRACI İZOLASYONUNU DELERDİ.
    ///
    /// AsyncLocal kullanılır: aynı işlem zincirinde akar, paralel işler
    /// birbirinin bağlamını görmez.
    /// </summary>
    public interface IAmbientContext
    {
        AmbientScope? Current { get; }

        /// <summary>Bağlamı ayarlar; dispose edildiğinde önceki değere döner.</summary>
        IDisposable Push(int userId, int tenantId, string role);
    }

    public sealed record AmbientScope(int UserId, int TenantId, string Role);
}
