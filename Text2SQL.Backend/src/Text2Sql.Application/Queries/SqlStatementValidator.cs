using System.Text;
using System.Text.RegularExpressions;
using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Contracts;

namespace Text2Sql.Application.Queries
{
    /// <summary>
    /// FAZ 3: Eski Contains("DROP ") yaklaşımının yerini alır.
    ///
    /// Eski yaklaşımın iki hatası vardı:
    /// 1. False-positive: SELECT 'DROP TABLE users' AS msg → reddediliyordu.
    /// 2. Atlatılabilirlik: yorum/whitespace varyasyonları yakalanmıyordu.
    ///
    /// Bu doğrulayıcı SQL'i durum makinesiyle tarar: string literal'ler ve
    /// yorumlar çıkarıldıktan sonra kalan metin üzerinde tam-kelime yasak
    /// listesi, tek-statement ve SELECT/WITH kontrolü yapılır.
    /// Not: AST tabanlı parser (SqlParserCS) ileriki iterasyon için ADR'de
    /// yükseltme yolu olarak kayıtlıdır; asıl güvence salt-okunur bağlantıdır.
    /// </summary>
    public sealed class SqlStatementValidator : ISqlStatementValidator
    {
        // Liste bilinçli olarak yalnızca (a) rezerve kelimeler (bare identifier
        // olamazlar → false-positive imkansız) ve (b) kimlik olarak kullanımı
        // pratikte görülmeyen statement kelimelerinden oluşur. REPLACE(x,y,z) ve
        // "copy" gibi meşru SELECT kullanımları bilinçli olarak listede DEĞİL —
        // tek-statement + SELECT/WITH kuralı onları statement olarak zaten engeller.
        private static readonly Regex ForbiddenKeywords = new(
            @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|MERGE|GRANT|REVOKE|" +
            @"PRAGMA|ATTACH|DETACH|VACUUM|REINDEX|EXECUTE|EXEC|CALL|PREPARE|DEALLOCATE|" +
            @"LISTEN|NOTIFY)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public void EnsureSafeSelect(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
                throw new BadRequestException("SQL sorgusu boş.");

            var stripped = StripLiteralsAndComments(sql);

            // Tek statement: sondaki noktalı virgül hariç ';' olamaz
            var trimmed = stripped.TrimEnd();
            if (trimmed.EndsWith(';')) trimmed = trimmed[..^1];
            if (trimmed.Contains(';'))
                throw new ForbiddenException("Güvenlik: birden fazla SQL statement'ına izin verilmez.");

            // İlk anlamlı kelime SELECT veya WITH olmalı
            var firstWord = Regex.Match(trimmed.TrimStart(), @"^[A-Za-z]+").Value.ToUpperInvariant();
            if (firstWord != "SELECT" && firstWord != "WITH")
                throw new ForbiddenException("Güvenlik: yalnızca SELECT ve WITH sorguları çalıştırılabilir.");

            // Yasaklı anahtar kelimeler (literal/yorum dışında, tam kelime)
            var match = ForbiddenKeywords.Match(trimmed);
            if (match.Success)
                throw new ForbiddenException($"Güvenlik: '{match.Value.ToUpperInvariant()}' komutu yasaktır.");

            // Parantez dengesi (literal'ler çıkarılmış metinde)
            if (trimmed.Count(c => c == '(') != trimmed.Count(c => c == ')'))
                throw new BadRequestException("SQL sorgusunda parantez dengesizliği.");
        }

        /// <summary>
        /// String literal'leri ('...', '' kaçışıyla), tırnaklı tanımlayıcıları ("...")
        /// ve yorumları (-- ile /* */) metinden çıkarır. Kapatılmamış literal/yorum
        /// → güvenli tarafta hata.
        /// </summary>
        private static string StripLiteralsAndComments(string sql)
        {
            var sb = new StringBuilder(sql.Length);
            int i = 0, n = sql.Length;

            while (i < n)
            {
                char c = sql[i];

                if (c == '\'') // string literal
                {
                    i++;
                    while (true)
                    {
                        if (i >= n)
                            throw new BadRequestException("SQL sorgusunda kapatılmamış string literal.");
                        if (sql[i] == '\'')
                        {
                            if (i + 1 < n && sql[i + 1] == '\'') { i += 2; continue; } // '' kaçışı
                            i++; break;
                        }
                        i++;
                    }
                    sb.Append("''"); // yerine boş literal
                }
                else if (c == '"') // tırnaklı tanımlayıcı
                {
                    i++;
                    while (true)
                    {
                        if (i >= n)
                            throw new BadRequestException("SQL sorgusunda kapatılmamış tırnaklı tanımlayıcı.");
                        if (sql[i] == '"') { i++; break; }
                        i++;
                    }
                    sb.Append("\"id\"");
                }
                else if (c == '-' && i + 1 < n && sql[i + 1] == '-') // satır yorumu
                {
                    while (i < n && sql[i] != '\n') i++;
                }
                else if (c == '/' && i + 1 < n && sql[i + 1] == '*') // blok yorumu
                {
                    i += 2;
                    while (true)
                    {
                        if (i + 1 >= n)
                            throw new BadRequestException("SQL sorgusunda kapatılmamış blok yorumu.");
                        if (sql[i] == '*' && sql[i + 1] == '/') { i += 2; break; }
                        i++;
                    }
                    sb.Append(' '); // yorum ayırıcıydı — token'ları birleştirme
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }

            return sb.ToString();
        }
    }
}
