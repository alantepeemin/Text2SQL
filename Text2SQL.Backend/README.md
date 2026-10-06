# Text2SQL

Çok kiracılı (multi-tenant) **Text-to-SQL SaaS platformu**. Organizasyonlar kullanıcılarını
davet eder, projelere veri kaynağı (SQLite dosyası veya uzak PostgreSQL) bağlar; kullanıcılar
doğal dilde soru sorar, LLM'in ürettiği SQL **salt-okunur** bir bağlantıda doğrulanarak
çalıştırılır.

> **Lisans:** Tescilli yazılım. Tüm hakları saklıdır — bkz. [LICENSE](LICENSE).

---

## İçindekiler

- [Özellikler](#özellikler)
- [Mimari](#mimari)
- [Hızlı başlangıç](#hızlı-başlangıç-geliştirme)
- [Docker ile çalıştırma](#docker-ile-çalıştırma)
- [API sürümleri](#api-sürümleri)
- [Paketler ve limitler](#paketler-ve-limitler)
- [Test](#test)
- [Dokümantasyon](#dokümantasyon)

---

## Özellikler

| Alan | Yetenek |
|---|---|
| **Çok kiracılılık** | Paylaşımlı şema; `ITenantScoped` uygulayan her entity'ye EF global sorgu filtresi *reflection ile* uygulanır — yeni entity eklendiğinde unutulma riski yoktur |
| **Kimlik** | JWT + `SecurityStamp` iptali, refresh-token aileleri ve yeniden kullanım tespiti, e-posta doğrulama, çoklu organizasyon üyeliği ve organizasyon değiştirme |
| **Yetkilendirme** | İzin tabanlı (`[RequirePermission]`) + proje düzeyi izin (`[RequireProjectPermission]`) + paket kapısı (`[RequireFeature]` → 402) |
| **Programatik erişim** | Kapsamlı (scoped) API anahtarları, `Bearer`'a alternatif şema |
| **Sorgu motoru** | Strateji deseni (`IDataSourceProvider`): SQLite ve PostgreSQL; yeni tip = yeni sınıf + bir DI satırı |
| **SQL güvenliği** | Tokenizer tabanlı `SqlStatementValidator` — yalnızca tek `SELECT`; yorum/çoklu ifade/DDL/DML reddedilir, salt-okunur bağlantı ve satır+süre limiti |
| **Maliyet kontrolü** | Şema daraltma (anahtar kelime skoru + FK genişletme), SQL önbelleği, LLM token ölçümü ve organizasyon token kotası |
| **Anlamsal katman** | Şema sözlüğü (`SchemaAnnotation`) ile tablo/kolon açıklamaları prompt'a girer; sorgu geri bildirimi toplanır |
| **Asenkron sorgu** | `202 Accepted` + durum sorgulama; arka plan işçisi kiracı bağlamını `IAmbientContext` ile taşır |
| **Uyum** | Append-only denetim kaydı, GDPR veri dışa aktarma/silme, yapılandırılabilir veri saklama süreleri |
| **İşletim** | Serilog (+Seq), OpenTelemetry, sağlık kontrolleri, hız sınırlama, Docker Compose, GitHub Actions CI + gizli anahtar taraması |

---

## Mimari

Clean Architecture tabanlı modüler monolit. Bağımlılık yönü hem derleyici hem
**NetArchTest** ile zorlanır (ihlal = kırmızı test):

```
Api  →  Infrastructure  →  Application  →  Domain
 │            │                 │             │
 │            │                 │             └── entity'ler ve davranışları, sıfır bağımlılık
 │            │                 └── use-case'ler, portlar (arayüzler), DTO, doğrulama
 │            └── EF Core/Npgsql, veri kaynağı provider'ları, LLM istemcisi, e-posta, arka plan işleri
 └── controller'lar, kimlik/yetki filtreleri, middleware, sürüm sözleşmeleri
```

Kararların gerekçeleri `docs/adr/` altındadır (ADR-001…005). Geliştirme sürecinin
faz faz kaydı `docs/gelistirme-gunlugu/` altında arşivlenmiştir.

---

## Hızlı başlangıç (geliştirme)

**Gereksinimler:** .NET 9 SDK, Docker (PostgreSQL için), bir OpenRouter API anahtarı.

```bash
# 1. Ortam dosyası
cp .env.example .env            # değerleri doldurun
docker compose up -d postgres

# 2. Yapılandırma
cp src/Text2Sql.Api/appsettings.example.json src/Text2Sql.Api/appsettings.json

# 3. Sırlar — placeholder ile uygulama AÇILMAZ (bilinçli fail-fast)
dotnet user-secrets set "JwtSettings:SecretKey" "<64+ karakter rastgele>" --project src/Text2Sql.Api
dotnet user-secrets set "OpenRouter:ApiKey"     "<openrouter anahtarınız>" --project src/Text2Sql.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" \
  "Host=localhost;Port=5432;Database=text2sql;Username=text2sql;Password=<.env'deki şifre>" \
  --project src/Text2Sql.Api

# 4. Şema
dotnet ef database update --project src/Text2Sql.Infrastructure --startup-project src/Text2Sql.Api

# 5. Çalıştır
dotnet run --project src/Text2Sql.Api      # Swagger: http://localhost:5000/swagger
```

İlk kullanım akışı: `POST /api/v2/auth/create-company` → dönen `registrationToken` ile
`POST /api/v2/auth/complete-registration` → `accessToken` ile diğer uçlar.

---

## Docker ile çalıştırma

```bash
cp .env.example .env      # JWT_SECRET_KEY, OPENROUTER_API_KEY, POSTGRES_PASSWORD zorunlu
docker compose up -d --build
```

- API: `http://localhost:8080`
- Loglar (Seq): `http://localhost:5341`

Ayrıntılar, üretim sertleştirmesi ve yedekleme için: **[docs/dagitim.md](docs/dagitim.md)**.

---

## API sürümleri

Aynı controller iki sözleşmeyi de besler; fark yalnızca yol önekindedir.

| | **v1** (`/api/...`, `/api/v1/...`) | **v2** (`/api/v2/...`) |
|---|---|---|
| Başarı gövdesi | `{ success, message, data }` | Çıplak veri |
| Hata gövdesi | `{ success:false, message }` | RFC 7807 `ProblemDetails` + `code` + `traceId` |
| Sayfalama | Gövdede liste | Gövdede liste + `X-Total-Count`, `X-Page`, `X-Page-Size` |
| Durum | Kullanımdan kaldırılacak (`Deprecation` başlığı) | Önerilen |

**Yeni istemciler v2 kullanmalıdır.** v1 geriye dönük uyumluluk için korunur.
Uç listesi, hata kodları ve örnekler: **[docs/api-referansi.md](docs/api-referansi.md)**.

---

## Paketler ve limitler

| | Free | Pro | Enterprise |
|---|---|---|---|
| Aylık token | 200.000 | 5.000.000 | Sınırsız |
| Üye / Proje / Veri kaynağı | 3 / 3 / 3 | 25 / 20 / 50 | Sınırsız |
| API anahtarı | — | 10 | Sınırsız |
| Uzak veri kaynağı (PostgreSQL) | — | ✓ | ✓ |
| Kullanım raporları | ✓ | ✓ | ✓ |
| Denetim kaydı | — | — | ✓ |
| Aylık ücret | $0 | $49 | $499 |

Paket dışı bir özellik istendiğinde API `402 Payment Required` döner (kod: `FEATURE_NOT_AVAILABLE`).

---

## Test

Testler dört projeye ayrılmıştır — her seviyenin maliyeti ve sorumluluğu farklıdır:

```powershell
.\scripts\test.ps1                 # gruplanmış özet + yavaş testler (Windows)
./scripts/test.sh                  # Linux/macOS
.\scripts\test.ps1 -Project Unit   # tek seviye
.\scripts\test.ps1 -Coverage       # kapsam oranlarıyla
```

Betik, Windows konsolundaki Türkçe karakter bozulmasını da düzeltir. Ham komut:
`dotnet test`.

Ayrıntılar, klasör haritası ve araç seti kullanımı: **[tests/README.md](tests/README.md)**.

---

## Dokümantasyon

| Belge | İçerik |
|---|---|
| [docs/api-referansi.md](docs/api-referansi.md) | Uç listesi, sözleşmeler, hata kodları, kimlik doğrulama |
| [docs/yapilandirma.md](docs/yapilandirma.md) | Tüm yapılandırma anahtarları, varsayılanlar, ortam değişkenleri |
| [docs/dagitim.md](docs/dagitim.md) | Docker/üretim dağıtımı, migration, yedekleme, sertleştirme |
| [docs/RUNBOOK.md](docs/RUNBOOK.md) | Olay müdahale ve işletim yordamları |
| [docs/uretim-hazirlik-raporu.md](docs/uretim-hazirlik-raporu.md) | Üretime hazırlık durumu, engelleyiciler, bilinen sınırlar |
| [postman/](postman/README.md) | Postman koleksiyonu ve doğruluk/hız turu |
| [docs/adr/](docs/adr/) | Mimari karar kayıtları ve gerekçeleri |
| [docs/Text2SQL-Mimari-ve-Gecis-Plani.md](docs/Text2SQL-Mimari-ve-Gecis-Plani.md) | Modernizasyon mimarisi ve geçiş planı (referans) |
| [docs/gelistirme-gunlugu/](docs/gelistirme-gunlugu/) | Faz raporları arşivi (tarihsel kayıt) |
| [CHANGELOG.md](CHANGELOG.md) | Sürüm geçmişi |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Geliştirme kuralları ve katkı akışı |
| [tests/README.md](tests/README.md) | Test mimarisi, seviyeler ve araç seti |
| [SECURITY.md](SECURITY.md) | Güvenlik modeli ve zafiyet bildirimi |
