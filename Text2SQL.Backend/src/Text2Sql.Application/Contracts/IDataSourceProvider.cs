using Text2Sql.Domain.Entities;

namespace Text2Sql.Application.Contracts
{
    /// <summary>Bir veri kaynağı sorgusunun standart sonucu.</summary>
    public sealed record QueryExecutionResult(List<string> Columns, List<List<string>> Rows);

    /// <summary>
    /// FAZ 3: Sorgu motorunun genişleme noktası (Strateji deseni).
    /// Yeni veritabanı tipi eklemek = yeni provider + DI kaydı; çekirdek akış
    /// (ExecuteQueryUseCase) hiçbir DB tipini tanımaz (OCP).
    /// </summary>
    public interface IDataSourceProvider
    {
        /// <summary>LLM prompt'una enjekte edilen lehçe adı (ör. "SQLite", "PostgreSQL").</summary>
        string Dialect { get; }

        /// <summary>Bu provider verilen dbType değerini işleyebilir mi? (ör. "sqlite")</summary>
        bool CanHandle(string dbType);

        /// <summary>Şema metnini çıkarır (LLM bağlamı için).</summary>
        Task<string> GetSchemaAsync(ProjectDatabase dataSource, CancellationToken ct = default);

        /// <summary>
        /// Doğrulanmış SQL'i SALT-OKUNUR bağlantı/oturum üzerinde çalıştırır.
        /// Satır limiti ve komut timeout'u zorunludur.
        /// </summary>
        Task<QueryExecutionResult> ExecuteReadOnlyAsync(
            ProjectDatabase dataSource, string sql, int maxRows, int timeoutSeconds,
            CancellationToken ct = default);
    }

    public interface IDataSourceProviderFactory
    {
        /// <exception cref="NotSupportedException">Desteklenmeyen dbType.</exception>
        IDataSourceProvider GetProvider(string dbType);
    }

    /// <summary>
    /// FAZ 3: LLM çıktısının güvenlik doğrulaması.
    /// Literal/yorum farkındalıklı tokenizer: string içindeki "DROP" false-positive
    /// üretmez, yorumla gizlenmiş ikinci statement'ı yakalar. Salt-okunur
    /// bağlantıyla birlikte derinlemesine savunmanın 2. katmanıdır.
    /// </summary>
    public interface ISqlStatementValidator
    {
        /// <summary>Tek, salt-okunur SELECT/WITH statement'ı olduğunu garanti eder; değilse fırlatır.</summary>
        void EnsureSafeSelect(string sql);
    }
}
