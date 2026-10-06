namespace Text2Sql.Domain.Entities
{
    /// <summary>
    /// Faz 2: İki aşamalı şirket kaydının 1. aşaması artık IMemoryCache yerine
    /// burada tutulur — uygulama yeniden başlasa veya ölçeklense de kayıt akışı kopmaz.
    /// </summary>
    public class PendingRegistration
    {
        public int Id { get; set; }

        public string Token { get; set; } = string.Empty; // unique index

        public string Name { get; set; } = string.Empty;
        public string? Domain { get; set; }
        public bool AllowDomainAutoJoin { get; set; }
        public int MaxUsers { get; set; } = 10;
        public int MonthlyQueryLimit { get; set; } = 1000;

        public DateTime ExpiresAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    }
}
