namespace Text2Sql.Application.Contracts
{
    /// <summary>
    /// SaaS-9: Prompt'a gönderilecek şemayı daraltır.
    ///
    /// Sorun: her sorguda TAM şema gönderiliyordu. 20 tablolu bir müşteri
    /// veritabanı tek sorguda 5–10k input token demek — hem pahalı hem yavaş,
    /// ayrıca alakasız tablolar LLM'i yanlış JOIN'lere yönlendirebiliyor
    /// ("gürültü" doğruluğu da düşürür).
    ///
    /// Strateji: soruyla ilgili tabloları skorla, yalnızca aday tabloları
    /// (+ onlarla ilişkili olanları) prompt'a koy. Eşik altında kalırsa
    /// tam şema gönderilir — küçük şemalarda daraltmanın faydası yok, riski var.
    /// </summary>
    public interface ISchemaOptimizer
    {
        /// <summary>
        /// Soruya göre daraltılmış şema metni döndürür.
        /// Daraltma yapılmadıysa (küçük şema / eşik altı skor) tam şema döner.
        /// </summary>
        SchemaOptimizationResult Optimize(string fullSchema, string question);
    }

    public sealed record SchemaOptimizationResult(
        string Schema,
        int TotalTableCount,
        int IncludedTableCount,
        bool WasReduced)
    {
        /// <summary>Kabaca token tasarrufu oranı (karakter uzunluğu üzerinden).</summary>
        public double ReductionRatio { get; init; }
    }
}
