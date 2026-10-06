namespace Text2Sql.Tests.Common.Fixtures
{
    /// <summary>
    /// Sonuç satır tavanının etkisini doğrulayan host (Query:MaxRows = 2).
    /// Örnek veritabanında 3 şehir var; tavan çalışıyorsa 2 satır dönmelidir.
    /// </summary>
    public sealed class RowLimitedTestApp : TestApp
    {
        public const int MaxRows = 2;

        protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
        {
            ["Query:MaxRows"] = MaxRows.ToString()
        };
    }
}
