using Text2Sql.Application.Queries;
using Xunit;

namespace Text2Sql.Tests.Unit.Infrastructure
{
    /// <summary>
    /// SaaS-9 — Şema daraltma (birim testleri; LLM/DB gerektirmez).
    /// </summary>
    public class SchemaOptimizerTests
    {
        private readonly SchemaOptimizer _optimizer = new();

        /// <summary>12 tablolu sahte şema — daraltma eşiğinin (8) üstünde.</summary>
        private static string BuyukSema() => string.Join("\n\n", new[]
        {
            "CREATE TABLE film (film_id INTEGER PRIMARY KEY, title TEXT, length INTEGER, rating TEXT);",
            "CREATE TABLE actor (actor_id INTEGER PRIMARY KEY, first_name TEXT, last_name TEXT);",
            "CREATE TABLE film_actor (film_id INTEGER REFERENCES film, actor_id INTEGER REFERENCES actor);",
            "CREATE TABLE category (category_id INTEGER PRIMARY KEY, name TEXT);",
            "CREATE TABLE customer (customer_id INTEGER PRIMARY KEY, email TEXT, active TEXT);",
            "CREATE TABLE payment (payment_id INTEGER PRIMARY KEY, customer_id INTEGER REFERENCES customer, amount REAL);",
            "CREATE TABLE rental (rental_id INTEGER PRIMARY KEY, customer_id INTEGER REFERENCES customer);",
            "CREATE TABLE inventory (inventory_id INTEGER PRIMARY KEY, film_id INTEGER REFERENCES film);",
            "CREATE TABLE staff (staff_id INTEGER PRIMARY KEY, username TEXT);",
            "CREATE TABLE store (store_id INTEGER PRIMARY KEY, manager_staff_id INTEGER REFERENCES staff);",
            "CREATE TABLE address (address_id INTEGER PRIMARY KEY, district TEXT);",
            "CREATE TABLE country (country_id INTEGER PRIMARY KEY, country TEXT);"
        });

        [Fact]
        public void KucukSema_Daraltilmaz()
        {
            var kucuk = "CREATE TABLE film (id INTEGER, title TEXT);\n\nCREATE TABLE actor (id INTEGER);";
            var sonuc = _optimizer.Optimize(kucuk, "Kaç film var?");

            // Eşik altı: daraltmanın kazancı küçük, yanlış tablo eleme riski görece büyük
            Assert.False(sonuc.WasReduced);
            Assert.Equal(kucuk, sonuc.Schema);
        }

        [Fact]
        public void BuyukSema_IlgiliTablolarlaDaraltilir()
        {
            var sonuc = _optimizer.Optimize(BuyukSema(), "En uzun film hangisi?");

            Assert.True(sonuc.WasReduced, "Büyük şema daraltılmalıydı.");
            Assert.Equal(12, sonuc.TotalTableCount);
            Assert.True(sonuc.IncludedTableCount < 12);

            // İlgili tablo prompt'ta olmalı
            Assert.Contains("CREATE TABLE film", sonuc.Schema);

            // Alakasız tablolar elenmiş olmalı
            Assert.DoesNotContain("CREATE TABLE country", sonuc.Schema);
            Assert.DoesNotContain("CREATE TABLE address", sonuc.Schema);

            // Token tasarrufu olmalı
            Assert.True(sonuc.ReductionRatio > 0.2,
                $"Beklenen tasarruf > %20, gerçekleşen: %{sonuc.ReductionRatio * 100:F0}");
        }

        [Fact]
        public void Daraltma_IliskiliTablolariDaEkler()
        {
            // "payment" seçilirse, JOIN için "customer" da gerekli (FK ilişkisi)
            var sonuc = _optimizer.Optimize(BuyukSema(), "Müşteri ödemeleri toplamı nedir?");

            Assert.Contains("CREATE TABLE payment", sonuc.Schema);
            Assert.Contains("CREATE TABLE customer", sonuc.Schema);
        }

        [Fact]
        public void EslesmeYok_TamSemaGonderilir()
        {
            // Hiçbir tabloyla eşleşmeyen soru → daraltma YAPILMAZ.
            // Bu bilinçli: yanlış daraltma, hatalı SQL demektir.
            var sonuc = _optimizer.Optimize(BuyukSema(), "Merhaba nasılsın bugün hava güzel");

            Assert.False(sonuc.WasReduced);
            Assert.Equal(12, sonuc.IncludedTableCount);
        }

        [Fact]
        public void Daraltilmis_SemaLLMeUyariIcerir()
        {
            var sonuc = _optimizer.Optimize(BuyukSema(), "En uzun film hangisi?");

            // LLM'e şemanın sınırlandırıldığı söylenmeli; aksi halde
            // "tablo yok" varsayıp uydurma isim üretebilir.
            Assert.Contains("sınırlandırılmıştır", sonuc.Schema);
        }

        [Fact]
        public void CokTablolulSoru_AraTabloVeKarsiTarafiDaGetirir()
        {
            // GERÇEK HATA REGRESYONU:
            // "En çok filmde oynayan 10 aktörü bul" sorusunda daraltıcı yalnızca
            // film + language döndürüyordu. Sebep: ara tablo (film_actor) hiçbir
            // soru kelimesiyle eşleşmiyor ve ters FK genişletmesi "skoru > 0"
            // koşuluna bağlıydı. Sonuç: LLM aktör tablosunu hiç görmüyor,
            // "bu tablolar şemada yok" diyip SQL üretemiyordu.
            var sonuc = _optimizer.Optimize(BuyukSema(), "En çok filmde oynayan 10 aktörü bul");

            Assert.Contains("CREATE TABLE film ", sonuc.Schema);
            Assert.Contains("CREATE TABLE film_actor", sonuc.Schema);
            Assert.Contains("CREATE TABLE actor ", sonuc.Schema);
        }

        [Fact]
        public void AraTabloGenisletmesi_SemayiTamamenGeriGetirmez()
        {
            // Emniyet supabı: 2b/2c genişletmesi çok tablo çekerse daraltma
            // anlamsızlaşır. Alakasız uçlar hâlâ elenmiş olmalı.
            var sonuc = _optimizer.Optimize(BuyukSema(), "En çok filmde oynayan 10 aktörü bul");

            Assert.True(sonuc.WasReduced, "Şema yine de daraltılmış olmalı.");
            Assert.DoesNotContain("CREATE TABLE country", sonuc.Schema);
            Assert.True(sonuc.IncludedTableCount < 12);
        }

    }
}
