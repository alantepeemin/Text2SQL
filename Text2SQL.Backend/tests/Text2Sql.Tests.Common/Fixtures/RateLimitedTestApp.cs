namespace Text2Sql.Tests.Common.Fixtures
{
    /// <summary>
    /// Hız sınırının GERÇEKTEN uygulandığı host. Varsayılan host'ta sınır
    /// kapalıdır (her test 429 yemeden koşabilsin); sınırı doğrulayan testler
    /// bu host'u kullanır. Ayrı sınıf olması, ayarların yalnızca ilgili
    /// testleri etkilemesini garanti eder.
    /// </summary>
    public sealed class RateLimitedTestApp : TestApp
    {
        public const int AuthPerMinute  = 3;
        public const int QueryPerMinute = 3;

        protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["RateLimiting:AuthPerMinute"]  = AuthPerMinute.ToString(),
            ["RateLimiting:QueryPerMinute"] = QueryPerMinute.ToString()
        };
    }
}
