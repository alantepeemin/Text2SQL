namespace Text2Sql.Tests.Common.Fakes
{
    /// <summary>
    /// Sahte LLM'in gözlemlenebilir durumu — HER TEST HOST'UNA ÖZEL.
    ///
    /// Statik alan kullanılmaz: xUnit test sınıflarını paralel koşturur ve
    /// paylaşılan bir sayaç başka bir sınıfın çağrılarıyla kirlenir. Bu daha
    /// önce gerçek bir hataya yol açtı (önbellek testi rastgele kırıldı) —
    /// ölçüm aracının kendisi izole olmalıdır.
    /// </summary>
    public sealed class FakeLlmState
    {
        private int _callCount;

        /// <summary>LLM'e kaç kez gidildi (önbellek isabetleri sayılmaz).</summary>
        public int CallCount => Volatile.Read(ref _callCount);

        /// <summary>Son çağrıda prompt'a giren şema.</summary>
        public string? LastSchema { get; private set; }

        /// <summary>Son çağrıda prompt'a giren iş sözlüğü.</summary>
        public string? LastGlossary { get; private set; }

        /// <summary>
        /// Ayarlanırsa üreteç bu SQL'i döndürür — "LLM tehlikeli SQL üretirse
        /// ne olur?" senaryolarını test etmek için.
        /// </summary>
        public string? NextSqlOverride { get; set; }

        public void Record(string schema, string? glossary)
        {
            LastSchema = schema;
            LastGlossary = glossary;
            Interlocked.Increment(ref _callCount);
        }

        public void ResetGlossary() => LastGlossary = null;
    }
}
