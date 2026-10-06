using Text2Sql.Application.Contracts;

namespace Text2Sql.Tests.Common.Fakes
{
    /// <summary>
    /// Deterministik SQL üreteci — testler ASLA gerçek LLM'e çıkmaz.
    ///
    /// Üretilen SQL, örnek veritabanına (cities) uygun sabit bir SELECT'tir:
    /// testler LLM'in zekâsını değil, uygulamanın SQL'i işleme boru hattını
    /// (doğrulama, çalıştırma, ölçüm, önbellek, denetim) doğrular.
    /// </summary>
    public sealed class FakeSqlGenerator : ISqlGeneratorService
    {
        public const string GeneratedSql =
            "SELECT name, population FROM cities ORDER BY population DESC;";

        // Gerçekçi token kullanımı: sıfır token her ölçüm testini trivial geçirir.
        public const int FakePromptTokens = 1200;
        public const int FakeCompletionTokens = 60;
        public const string FakeModel = "test/fake-model";

        private readonly FakeLlmState _state;

        public FakeSqlGenerator(FakeLlmState state) => _state = state;

        public Task<SqlGenerationResult> GenerateSqlAsync(
            string schema, string question, string dialect,
            string? glossary = null, CancellationToken ct = default)
        {
            _state.Record(schema, glossary);

            var sql = _state.NextSqlOverride ?? GeneratedSql;

            return Task.FromResult(new SqlGenerationResult(
                sql, FakeModel, FakePromptTokens, FakeCompletionTokens));
        }
    }
}
