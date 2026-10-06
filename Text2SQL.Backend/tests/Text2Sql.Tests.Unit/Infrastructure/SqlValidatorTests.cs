using Text2Sql.Application.Common.Exceptions;
using Text2Sql.Application.Queries;
using Text2Sql.Infrastructure.Providers;
using Xunit;

namespace Text2Sql.Tests.Unit.Infrastructure
{
    /// <summary>
    /// FAZ 3 — SQL doğrulayıcı saldırı korpusu.
    /// Bu paket, LLM çıktısının çalıştırılmadan önce geçtiği güvenlik
    /// katmanını mühürler. Kırmızıya dönen her test bir güvenlik regresyonudur.
    /// </summary>
    public class SqlValidatorTests
    {
        private readonly SqlStatementValidator _validator = new();

        // ── Geçmesi gerekenler ───────────────────────────────────────────────

        [Theory]
        [InlineData("SELECT * FROM users;")]
        [InlineData("SELECT id, name FROM users WHERE age > 18 ORDER BY name LIMIT 10;")]
        [InlineData("WITH t AS (SELECT id FROM orders) SELECT COUNT(*) FROM t;")]
        [InlineData("select lower(name) from users")]                       // noktalı virgülsüz + küçük harf
        [InlineData("SELECT REPLACE(name, 'a', 'b') FROM users;")]          // REPLACE() fonksiyonu meşru
        [InlineData("SELECT copy, status FROM campaigns;")]                 // 'copy' kolon adı meşru
        [InlineData("SELECT 'DROP TABLE users' AS mesaj FROM dual;")]       // literal içinde DROP — eski kod bunu reddediyordu!
        [InlineData("SELECT \"update\" FROM audit;")]                       // tırnaklı tanımlayıcı
        [InlineData("SELECT id -- son kayıtlar\nFROM users;")]              // satır yorumu
        [InlineData("SELECT id /* açıklama */ FROM users;")]                // blok yorumu
        [InlineData("SELECT name FROM t WHERE note = 'it''s ok';")]         // '' kaçışı
        public void GecerliSorgular_Kabul(string sql)
            => _validator.EnsureSafeSelect(sql); // exception fırlatmamalı

        // ── Reddedilmesi gerekenler ─────────────────────────────────────────

        [Theory]
        [InlineData("DROP TABLE users;")]
        [InlineData("DELETE FROM users;")]
        [InlineData("UPDATE users SET role = 'admin';")]
        [InlineData("INSERT INTO users VALUES (1);")]
        [InlineData("PRAGMA writable_schema = ON;")]
        [InlineData("ATTACH DATABASE '/tmp/x' AS x;")]
        [InlineData("SELECT 1; DROP TABLE users;")]                          // çoklu statement
        [InlineData("SELECT 1;/**/DROP TABLE users;")]                       // yorumla gizlenmiş ikinci statement
        [InlineData("SELECT 1;\n-- yorum\nDELETE FROM users;")]              // yorum satırı arkasına saklama
        [InlineData("WITH x AS (DELETE FROM users RETURNING *) SELECT * FROM x;")] // PG yazan-CTE
        [InlineData("VACUUM;")]
        [InlineData("CALL do_something();")]
        [InlineData("EXECUTE hazir_plan;")]
        public void TehlikeliSorgular_Reddedilir(string sql)
            => Assert.Throws<ForbiddenException>(() => _validator.EnsureSafeSelect(sql));

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("SELECT 'kapatilmamis literal FROM users;")]             // açık string
        [InlineData("SELECT /* kapatilmamis yorum FROM users;")]            // açık yorum
        [InlineData("SELECT (1 + 2 FROM users;")]                            // parantez dengesizliği
        public void BozukSorgular_Reddedilir(string sql)
            => Assert.Throws<BadRequestException>(() => _validator.EnsureSafeSelect(sql));

        [Fact]
        public void SondakiNoktaliVirgul_CokluStatementSayilmaz()
            => _validator.EnsureSafeSelect("SELECT 1;");
    }
}
