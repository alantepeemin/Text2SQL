# Test Mimarisi

Testler **dört projeye** ayrılmıştır. Ayrım keyfi değildir: her seviyenin
farklı bir maliyeti, farklı bir geri bildirim hızı ve farklı bir sorumluluğu vardır.

```
tests/
├── Text2Sql.Tests.Common/         ← araç seti (test içermez)
├── Text2Sql.Tests.Unit/           ← host yok, DB yok — milisaniyeler
├── Text2Sql.Tests.Architecture/   ← kod tabanının ŞEKLİNİ korur
└── Text2Sql.Tests.Integration/    ← gerçek uygulama, gerçek HTTP, gerçek EF
```

## Neden dört proje?

| Proje | Sorumluluk | Bağımlılık kuralı |
|---|---|---|
| **Common** | Fixture, builder, assertion, sahte servisler | Test içermez; diğerleri bunu kullanır |
| **Unit** | Saf mantık: doğrulayıcı, şema daraltma, hata kodları, entity davranışı | **Api'ye referans YOK** — bir birim testi controller'a ihtiyaç duyuyorsa yanlış katmandadır |
| **Architecture** | Katman yönü, kiracı filtreleri, yetkilendirme kapsamı, katmanlar arası sabitler | Davranış değil YAPI doğrular |
| **Integration** | Uçtan uca HTTP: middleware, yetki, EF, arka plan işleri | Yalnızca LLM sahtelenir; dış servise çıkılmaz |

## Entegrasyon klasörleri

| Klasör | Kapsam |
|---|---|
| `Auth/` | Kayıt, giriş, davet, çoklu organizasyon, oturum geçersizleştirme |
| `Authorization/` | İzin uygulaması, proje düzeyi izinler (Viewer/Editor/Owner) |
| `MultiTenancy/` | Kiracılar arası erişim, global filtre |
| `DataSources/` | Veri kaynağı ekleme, şema sözlüğü |
| `Queries/` | Sorgu çalıştırma, asenkron işler, önbellek, sınırlar |
| `Billing/` | Paketler, özellik kapıları, plan limitleri |
| `ApiKeys/` | Anahtar üretimi, kapsamlar, iptal |
| `Compliance/` | Denetim kaydı, kullanım ölçümü, GDPR |
| `Contracts/` | v1/v2 sözleşmeleri, sayfalama, hata biçimi, deprecation |
| `Security/` | Sertleştirme, hız sınırlama |
| `Validation/` | Girdi doğrulama |
| `Reliability/` | Idempotency, sağlık uçları |
| `Performance/` | Yanıt bütçeleri (kıyaslama değil, regresyon kapısı) |
| `Scenarios/` | Uçtan uca kullanıcı yolculukları |

## Araç seti

```csharp
// Kiracı kurulumu — kayıt akışının tek tanımı
var kiraci = await app.NewTenant("Sorgu A.Ş.")
                      .WithPlan("pro")
                      .WithSqliteDataSource()
                      .BuildAsync();

// Üye ekleme (davet → kabul → onay → giriş)
var uye = await Members.AddAsync(app, kiraci.Client, role: "user");

// Sözleşme doğrulamaları
var veri = await ApiAssert.EnvelopeAsync(v1Yanit);     // v1 zarfı
var govde = await ApiAssert.BareAsync(v2Yanit);        // v2 çıplak
await ApiAssert.ProblemAsync(yanit, HttpStatusCode.NotFound, "NOT_FOUND");
ApiAssert.PaginationHeaders(yanit, beklenenSayfa: 1, beklenenBoyut: 10);
```

Özel yapılandırma gerektiren senaryolar için ayrı host'lar vardır:
`RateLimitedTestApp` (hız sınırı açık), `RowLimitedTestApp` (satır tavanı 2).
Ayarları global olarak değiştirmek yerine ayrı host kullanmak, bir testin
diğerlerini etkilemesini yapısal olarak imkânsız kılar.

## Kurallar

1. **Paylaşılan statik durum yasak.** Sahte LLM'in sayacı host başınadır;
   xUnit sınıfları paralel koşar ve paylaşılan sayaç testleri rastgele kırar
   (bu bir kez gerçekten oldu).
2. **Her düzeltme için önce kırmızı test.** Regresyon testi olmayan hata
   düzeltmesi, geri gelmeye açık bir düzeltmedir.
3. **Güvenlik sınırları test edilmeden birleştirilmez** (kiracı izolasyonu,
   yetkilendirme, SQL doğrulama).
4. **E-postalar benzersiz olmalı** (`SampleData.UniqueEmail()`) — Email alanı
   benzersiz indekslidir.
5. **Testler dış servise çıkmaz.** LLM sahtelenir; ağ erişimi gerektiren bir
   test yazılmaz.

## Çalıştırma

### Önerilen: yardımcı betik

Ham `dotnet test` çıktısı yüzlerce satır gürültü içerir ve Windows'ta Türkçe
karakterleri bozar. Betik ikisini de çözer:

```powershell
.\scripts\test.ps1                                    # tümü + gruplanmış özet
.\scripts\test.ps1 -Project Unit                       # tek proje
.\scripts\test.ps1 -Filter "FullyQualifiedName~MultiTenancy"
.\scripts\test.ps1 -Coverage                           # kapsam oranlarıyla
.\scripts\test.ps1 -Detailed                           # geçen testleri de listele
```

Linux/macOS: `./scripts/test.sh [-p Unit] [-f <filtre>] [-c]`

Çıktı şuna benzer:

```
  ÖZET
  ✓ Integration / Contracts                    11/11      842 ms
  ✓ Integration / MultiTenancy                  9/9      1.204 ms
  ✗ Integration / Queries                      14/15     3.910 ms
  ✓ Unit / Application                         22/22       31 ms

  EN YAVAŞ 5 TEST
     6.326 ms  ProjectPermissionTests.Editor sorgu calistirabilir
     ...

  BAŞARISIZ (1)
  ✗ QueryExecutionLimitsTests.Satir tavani asan sonuc kirpilir
      Assert.Equal() Failure: 3 != 2
      ↳   at Text2Sql.Tests.Integration.Queries...

  ✗ 1 başarısız · 202 geçti · 15,2 sn
```

### Ham komutlar

```bash
dotnet test                                        # tümü
dotnet test tests/Text2Sql.Tests.Unit              # hızlı geri bildirim
dotnet test --filter FullyQualifiedName~Contracts  # tek konu
```

### Türkçe karakter sorunu

`dotnet test` çıktıyı UTF-8 yazar; Windows konsolu varsayılan olarak cp857
kullanır ve `ı ş ğ İ Ç` bozulur. Betik bunu oturum bazında düzeltir. Kalıcı
çözüm isterseniz:

```powershell
# PowerShell profiline eklenebilir
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [Console]::OutputEncoding
```

veya Windows Ayarlar → Bölge → Yönetimsel → *"Dünya çapında dil desteği için
Unicode UTF-8 kullan"* seçeneği. Windows Terminal + PowerShell 7 bu sorunu
zaten yaşamaz.

### Test adları

`xunit.runner.json` ile alt çizgiler boşluğa çevrilir:
`Editor_SorguCalistirabilir` → **"Editor SorguCalistirabilir"**. 10 saniyeyi
aşan testler ayrıca uyarı üretir — yavaşlama fark edilmeden birikmesin.
