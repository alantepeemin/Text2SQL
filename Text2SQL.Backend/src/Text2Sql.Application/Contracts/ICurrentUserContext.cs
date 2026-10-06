namespace Text2Sql.Application.Contracts
{
    /// <summary>
    /// JWT token'dan elde edilen mevcut kullanıcı bağlamını sağlar.
    /// </summary>
    public interface ICurrentUserContext
    {
        int TenantId { get; }
        int UserId { get; }
        string Role { get; }
        string Status { get; }
        bool HasTenant { get; }
        bool IsApproved { get; }
        bool IsAdmin { get; }
    }
}
