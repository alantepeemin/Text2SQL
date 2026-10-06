# Text2SQL

Çok kiracılı (multi-tenant) **Text-to-SQL SaaS platformu**: kullanıcılar bir projeye
veri kaynağı bağlar, doğal dilde soru sorar; LLM'in ürettiği SQL doğrulanıp
**salt-okunur** bağlantıda çalıştırılır.

> **Lisans:** Tescilli yazılım, tüm hakları saklıdır — bkz. [LICENSE](Text2SQL.Backend/LICENSE).

## Depo yapısı

| Klasör | Açıklama |
|---|---|
| [`Text2SQL.Backend/`](Text2SQL.Backend) | .NET 9 Web API, testler, Docker ve dokümantasyon |
| [`.github/workflows/`](.github/workflows) | CI: secret taraması, derleme + test, Docker imajı |

## Başlarken

Kurulum, mimari, yapılandırma ve test talimatları backend README'sindedir:
**[Text2SQL.Backend/README.md](Text2SQL.Backend/README.md)**

Sık kullanılan belgeler:

- [API referansı](Text2SQL.Backend/docs/api-referansi.md)
- [Yapılandırma](Text2SQL.Backend/docs/yapilandirma.md)
- [Dağıtım](Text2SQL.Backend/docs/dagitim.md)
- [Güvenlik politikası](Text2SQL.Backend/SECURITY.md)
- [Katkı rehberi](Text2SQL.Backend/CONTRIBUTING.md)
- [Değişiklik günlüğü](Text2SQL.Backend/CHANGELOG.md)

## Örnek veri

[`Text2SQL.Backend/film.db`](Text2SQL.Backend/film.db), Postman koleksiyonunda ve
manuel denemelerde veri kaynağı olarak yüklenen herkese açık
[Sakila](https://dev.mysql.com/doc/sakila/en/) örnek veri tabanının SQLite sürümüdür.
Müşteri verisi içermez.

## Gizli bilgiler

`.env`, anahtarlar ve parolalar **asla** commit edilmez. Şablon için
`Text2SQL.Backend/.env.example` dosyasını kopyalayıp `.env` olarak doldurun.
