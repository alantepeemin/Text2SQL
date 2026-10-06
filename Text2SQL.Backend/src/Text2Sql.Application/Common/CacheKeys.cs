namespace Text2Sql.Application.Common
{
    public static class CacheKeys
    {
        /// <summary>SecurityStamp middleware önbelleği (60 sn TTL).</summary>
        public static string SecurityStamp(int userId) => $"sstamp_{userId}";

        /// <summary>SaaS-2: Kullanıcının belirli organizasyondaki üyelik durumu (60 sn TTL).</summary>
        public static string Membership(int userId, int tenantId) => $"mship_{userId}_{tenantId}";

        /// <summary>SaaS-5: Organizasyonun etkin planı ve özellikleri (60 sn TTL).</summary>
        public static string Plan(int tenantId) => $"plan_{tenantId}";
    }
}
