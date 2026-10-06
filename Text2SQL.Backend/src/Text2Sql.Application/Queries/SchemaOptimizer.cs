using System.Text;
using System.Text.RegularExpressions;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Application.Queries
{
    /// <summary>
    /// SaaS-9: Anahtar kelime skorlamasıyla ilgili tablo seçimi.
    ///
    /// Neden embedding değil: embedding altyapısı (vektör DB, model çağrısı,
    /// yeniden indeksleme) bu kazanç için fazla karmaşık ve YENİ bir LLM
    /// maliyeti getirirdi. Anahtar kelime eşleşmesi sıfır maliyetli, deterministik
    /// ve test edilebilir; büyük şemalarda kazancın çoğunu zaten sağlıyor.
    /// Embedding, doğruluk ölçümü bunu yetersiz gösterirse sonraki adım olur.
    ///
    /// Güvenlik notu: bu sınıf yalnızca prompt'u DARALTIR. Yanlış tablo seçilse
    /// bile sonuç hatalı SQL olur, güvenlik açığı olmaz — üretilen SQL yine
    /// SqlStatementValidator'dan ve salt-okunur bağlantıdan geçer.
    /// </summary>
    public sealed class SchemaOptimizer : ISchemaOptimizer
    {
        /// <summary>Bu tablo sayısının altında daraltma yapılmaz (riski faydasından fazla).</summary>
        private const int MinTablesForReduction = 8;

        /// <summary>Seçilen tablo sayısı toplamın bu oranını aşarsa tam şema gönderilir.</summary>
        private const double MaxSelectionRatio = 0.7;

        private static readonly Regex TableBlockRegex = new(
            @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?[""'`\[]?(?<name>[A-Za-z_][A-Za-z0-9_]*)[""'`\]]?\s*\((?<body>.*?)\)\s*;",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>Türkçe ve İngilizce yaygın durak kelimeler — skorlamada gürültü yapar.</summary>
        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "kaç","kac","tane","nedir","hangi","en","çok","cok","az","olan","olanlar","için","icin",
            "ve","veya","ile","bir","bu","şu","su","o","var","yok","göster","goster","listele",
            "toplam","ortalama","say","sayısı","sayisi","adet","tüm","tum","hepsi","son","ilk",
            "the","a","an","and","or","of","in","on","for","to","how","many","much","what","which",
            "show","list","count","total","average","sum","all","top","most","least","is","are"
        };

        public SchemaOptimizationResult Optimize(string fullSchema, string question)
        {
            if (string.IsNullOrWhiteSpace(fullSchema))
                return new SchemaOptimizationResult(fullSchema, 0, 0, false);

            var tablolar = TabloBloklariniAyikla(fullSchema);

            // Az tablolu şemada daraltma yapmıyoruz: kazanç küçük, yanlış tablo
            // eleme riski görece büyük.
            if (tablolar.Count < MinTablesForReduction)
                return new SchemaOptimizationResult(fullSchema, tablolar.Count, tablolar.Count, false);

            var sorguKelimeleri = KelimeleriAyikla(question);
            if (sorguKelimeleri.Count == 0)
                return new SchemaOptimizationResult(fullSchema, tablolar.Count, tablolar.Count, false);

            // 1. Doğrudan skorlama: tablo adı ve kolon adları soru kelimeleriyle eşleşiyor mu?
            var skorlar = tablolar.ToDictionary(
                t => t.Name,
                t => Skorla(t, sorguKelimeleri),
                StringComparer.OrdinalIgnoreCase);

            var secilen = skorlar.Where(kv => kv.Value > 0)
                .Select(kv => kv.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (secilen.Count == 0)
                return new SchemaOptimizationResult(fullSchema, tablolar.Count, tablolar.Count, false);

            // 2a. Giden FK: seçilen tablonun İŞARET ETTİĞİ tablolar olmadan JOIN yazılamaz.
            foreach (var tablo in tablolar.Where(t => secilen.Contains(t.Name)).ToList())
                foreach (var iliskili in tablo.ReferencedTables)
                    secilen.Add(iliskili);

            // 2b. Gelen FK: seçilen tabloya İŞARET EDEN tablolar.
            //
            // DÜZELTME (gerçek hata): burada eskiden "&& skorlar[tablo.Name] > 0"
            // koşulu vardı. Ara (junction) tablolar — film_actor, film_category —
            // hiçbir soru kelimesiyle eşleşmez, çünkü kullanıcı "film_actor"
            // demez. Skor koşulu bu tabloları SİSTEMATİK olarak eliyordu ve
            // "en çok filmde oynayan aktörler" gibi çok tablolu sorular
            // cevaplanamıyordu: LLM'e film + language gönderiliyor, aktör
            // tablosu hiç görünmüyordu.
            var gelenBaglantilar = tablolar
                .Where(t => !secilen.Contains(t.Name)
                            && t.ReferencedTables.Any(r => secilen.Contains(r)))
                .ToList();

            foreach (var tablo in gelenBaglantilar)
                secilen.Add(tablo.Name);

            // 2c. Ara tablonun KARŞI tarafı. film_actor seçildiyse actor da
            // gereklidir; yoksa JOIN'in bir ucu şemada yoktur. İki veya daha
            // fazla tabloya referans veren tablo, tanımı gereği bir bağlantı
            // tablosudur.
            foreach (var araTablo in gelenBaglantilar.Where(t => t.ReferencedTables.Count >= 2))
                foreach (var karsiTaraf in araTablo.ReferencedTables)
                    secilen.Add(karsiTaraf);

            // 3. Çok fazla tablo seçildiyse daraltmanın anlamı yok.
            // Bu aynı zamanda 2b/2c'nin emniyet supabıdır: genişletme şemanın
            // çoğunu geri getiriyorsa tam şema gönderilir — eksik tabloyla
            // cevapsız kalmaktansa biraz fazla token harcamak yeğdir.
            if (secilen.Count > tablolar.Count * MaxSelectionRatio)
                return new SchemaOptimizationResult(fullSchema, tablolar.Count, tablolar.Count, false);

            var sb = new StringBuilder();
            foreach (var tablo in tablolar.Where(t => secilen.Contains(t.Name)))
            {
                sb.AppendLine(tablo.Ddl.Trim());
                sb.AppendLine();
            }

            // LLM'e şemanın daraltıldığını bildirmek önemli: aksi halde
            // "tablo yok" varsayıp uydurma isim üretebilir.
            sb.AppendLine($"-- NOT: Bu şema, soruyla ilgili {secilen.Count} tabloyla sınırlandırılmıştır");
            sb.AppendLine($"-- (veritabanında toplam {tablolar.Count} tablo var). Yalnızca yukarıdaki tabloları kullan.");

            var daraltilmis = sb.ToString();

            return new SchemaOptimizationResult(daraltilmis, tablolar.Count, secilen.Count, true)
            {
                ReductionRatio = 1.0 - ((double)daraltilmis.Length / fullSchema.Length)
            };
        }

        private static int Skorla(TableInfo tablo, HashSet<string> sorguKelimeleri)
        {
            var skor = 0;

            foreach (var kelime in sorguKelimeleri)
            {
                // Tablo adı eşleşmesi en güçlü sinyal
                if (BenzerMi(tablo.Name, kelime)) skor += 10;

                // Kolon adı eşleşmesi
                foreach (var kolon in tablo.Columns)
                    if (BenzerMi(kolon, kelime)) skor += 3;
            }

            return skor;
        }

        /// <summary>
        /// Basit ama etkili eşleşme: içerme + tekil/çoğul toleransı
        /// ("film" ↔ "films", "şehir" ↔ "cities" gibi çeviri farkları
        /// yakalanamaz — o durumda skor 0 kalır ve tam şema gönderilir.)
        /// </summary>
        private static bool BenzerMi(string tanimlayici, string kelime)
        {
            if (kelime.Length < 3) return false;

            var t = tanimlayici.Replace("_", "");
            if (t.Contains(kelime, StringComparison.OrdinalIgnoreCase)) return true;
            if (kelime.Contains(t, StringComparison.OrdinalIgnoreCase) && t.Length >= 4) return true;

            // Çoğul/tekil toleransı
            if (kelime.EndsWith('s') && t.Contains(kelime[..^1], StringComparison.OrdinalIgnoreCase)) return true;
            if (t.EndsWith("s") && kelime.Contains(t[..^1], StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static HashSet<string> KelimeleriAyikla(string question)
            => Regex.Matches(question ?? string.Empty, @"[\p{L}\p{N}_]{3,}")
                .Select(m => m.Value)
                .Where(w => !StopWords.Contains(w))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static List<TableInfo> TabloBloklariniAyikla(string schema)
            => TableBlockRegex.Matches(schema)
                .Select(m => new TableInfo(
                    m.Groups["name"].Value,
                    m.Value,
                    KolonlariAyikla(m.Groups["body"].Value),
                    ReferansliTablolariAyikla(m.Groups["body"].Value)))
                .ToList();

        private static List<string> KolonlariAyikla(string body)
            => body.Split(',')
                .Select(satir => satir.Trim())
                .Where(satir => satir.Length > 0)
                .Select(satir => Regex.Match(satir, @"^[""'`\[]?(?<col>[A-Za-z_][A-Za-z0-9_]*)").Groups["col"].Value)
                .Where(col => col.Length > 0
                              && !col.Equals("PRIMARY", StringComparison.OrdinalIgnoreCase)
                              && !col.Equals("FOREIGN", StringComparison.OrdinalIgnoreCase)
                              && !col.Equals("CONSTRAINT", StringComparison.OrdinalIgnoreCase)
                              && !col.Equals("UNIQUE", StringComparison.OrdinalIgnoreCase))
                .ToList();

        private static List<string> ReferansliTablolariAyikla(string body)
            => Regex.Matches(body, @"REFERENCES\s+[""'`\[]?(?<ref>[A-Za-z_][A-Za-z0-9_]*)",
                    RegexOptions.IgnoreCase)
                .Select(m => m.Groups["ref"].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private sealed record TableInfo(
            string Name, string Ddl, List<string> Columns, List<string> ReferencedTables);
    }
}
