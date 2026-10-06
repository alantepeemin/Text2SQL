# Postman Koleksiyonu

API v2 sözleşmesi üzerine kurulu, **59 istek / 12 klasör**. Token yönetimi
otomatiktir; elle kopyala-yapıştır gerekmez.

## Dosyalar

| Dosya | İçerik |
|---|---|
| `Text2SQL.postman_collection.json` | Koleksiyonun kendisi |
| `Text2SQL-Yerel.postman_environment.json` | `http://localhost:5000` (dotnet run) |
| `Text2SQL-Docker.postman_environment.json` | `http://localhost:8080` (docker compose) |
| `Text2SQL-Dogruluk-Turu.postman_collection.json` | Doğruluk/hız turu — **ayrı koleksiyon**, veri dosyası gerektirmez |
| `dogruluk-sorulari.json` | Soru listesinin okunabilir kopyası (turun kendi kopyası script içindedir) |

## Kurulum

1. Postman → **Import** → üç JSON dosyasını da içe aktarın.
2. Sağ üstten ortamı seçin (**Yerel** veya **Docker**).
3. Klasörleri **numaralandırılmış sırayla** çalıştırın.

`01 · Kimlik` her koşumda benzersiz bir organizasyon ve e-posta üretir; aynı
koleksiyonu tekrar tekrar çalıştırabilirsiniz.

## Tek elle adım: veri kaynağı yükleme

`03 · Veri Kaynakları → SQLite yükle` isteğinde **Body → form-data →
DatabaseFile** alanında `film.db` dosyasını seçin. Postman güvenlik nedeniyle
dosya yollarını dışa aktarılan koleksiyona gömmez — bu adım kaçınılmazdır.

## Klasörler

| Klasör | Kapsam |
|---|---|
| `00 · Sağlık` | `/health`, `/health/live`, `/health/ready` |
| `01 · Kimlik` | Kayıt, giriş, token yenileme (rotasyon + yeniden kullanım reddi) |
| `02 · Projeler` | CRUD + 404 sözleşmesi |
| `03 · Veri Kaynakları` | SQLite yükleme, şema sözlüğü |
| `04 · Sorgular` | Senkron sorgu, idempotency tekrarı, sayfalı geçmiş |
| `05 · Asenkron Sorgu` | 202 → durum yoklama (otomatik döngü) |
| `06 · Organizasyon` | Üyeler, davet, kullanım/maliyet, denetim kaydı, GDPR ihracı |
| `07 · Paketler` | Katalog, abonelik, Pro'ya yükseltme |
| `08 · API Anahtarları` | Üretim, kapsam sınırı, iptal, iptal sonrası ret |
| `09 · Sözleşme Kontrolleri` | v1 zarfı ↔ v2 çıplak, Deprecation, ProblemDetails |
| `10 · Doğruluk ve Hız` | Veri dosyasıyla sorgu bataryası |
| `11 · Temizlik` | Oluşturulan kaynakları siler |

## Doğruluk ve hız turu

Postman'in **Data File** özelliği ücretli planlara alındığı için tur **ayrı bir
koleksiyona** taşındı: `Text2SQL-Dogruluk-Turu.postman_collection.json`. Soruları
kendi içinde taşır ve `setNextRequest` ile kendi kendine döner.

1. Bu koleksiyonu da import edin.
2. Değişkenlerine `eposta` (ana koleksiyonun oluşturduğu hesap) ve gerekiyorsa
   `baseUrl` yazın.
3. **Run collection** → sonuçlar için **View → Show Postman Console**.

Proje ve veri kaynağı otomatik bulunur. Soruları değiştirmek için
`4 · Doğruluk turu` isteğinin **Pre-request Script** sekmesindeki `SORULAR`
dizisini düzenleyin.

Tur sonunda ortalama süre, doğruluk oranı, en yavaş soru ve beklendiği gibi
sonuçlanmayan soruların listesi raporlanır.

> **Doğruluk nasıl ölçülüyor?** Üretilen SQL'in çalışması *ve* beklenen
> tablo/işlev anahtar kelimelerini içermesi aranır. Kırmızı bir satır her zaman
> modelin hatası değildir — beklenen kelime listesi fazla katı da olabilir.
> Günlükteki SQL'i okuyup karar verin.

## Koleksiyon genelinde çalışan kontroller

Her isteğe otomatik uygulanır:

- Sunucu **5xx dönmedi**
- JSON gövde ayrıştırılabiliyor
- **v2 hataları** her zaman `application/problem+json` + `code` + `traceId`
- Hata yanıtlarında **yığın izi sızdırılmıyor**

## Uyarılar

- Koleksiyon **üretim ortamında çalıştırılmamalıdır** — kayıt oluşturur ve
  `11 · Temizlik` kaynak siler.
- `04 · Sorgular` ve `10 · Doğruluk ve Hız` gerçek LLM çağrısı yapar; token
  harcar ve maliyet üretir.
- Hız sınırı üretim yapılandırmasında açıktır; hızlı ardışık koşumlarda `429`
  görebilirsiniz (kod: `RATE_LIMITED`).
