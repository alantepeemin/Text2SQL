<#
.SYNOPSIS
    Testleri çalıştırır ve okunabilir bir özet basar.

.DESCRIPTION
    İki sorunu çözer:
      1. Türkçe karakterler — konsol kod sayfası UTF-8'e alınır.
      2. Okunabilirlik — ham VSTest çıktısı yerine proje/klasör bazında
         gruplanmış özet, yavaş testler ve (varsa) hataların ayrıntısı basılır.

.PARAMETER Filter
    dotnet test --filter ifadesi. Örnek: -Filter "FullyQualifiedName~Contracts"

.PARAMETER Project
    Yalnızca tek bir test projesi. Örnek: -Project Unit | Architecture | Integration

.PARAMETER Coverage
    Kod kapsamı toplar ve derleme (assembly) bazında satır kapsamını basar.

.PARAMETER Detailed
    Geçen testleri de tek tek listeler (varsayılan: yalnızca özet).

.EXAMPLE
    .\scripts\test.ps1
    .\scripts\test.ps1 -Project Unit
    .\scripts\test.ps1 -Filter "FullyQualifiedName~MultiTenancy" -Detailed
    .\scripts\test.ps1 -Coverage
#>

[CmdletBinding()]
param(
    [string]$Filter,
    [ValidateSet('Unit', 'Architecture', 'Integration', 'All')]
    [string]$Project = 'All',
    [switch]$Coverage,
    [switch]$Detailed
)

$ErrorActionPreference = 'Stop'

# ── 1. Türkçe karakter sorunu ────────────────────────────────────────────────
# dotnet/VSTest çıktıyı UTF-8 yazar; Windows konsolu varsayılan olarak
# cp857/cp1254 kullanır ve "ı ş ğ İ Ç" bozulur. Kod sayfasını bu oturum
# için UTF-8'e alıyoruz (kalıcı değişiklik yapmaz).
$oncekiKodSayfasi = [Console]::OutputEncoding
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$env:DOTNET_CLI_UI_LANGUAGE = 'tr'
if ($env:OS -eq 'Windows_NT') { chcp 65001 > $null }

$kok = Split-Path -Parent $PSScriptRoot
$sonucDizini = Join-Path $kok 'TestResults'

function Yaz([string]$metin, [string]$renk = 'Gray') { Write-Host $metin -ForegroundColor $renk }
function Cizgi { Yaz ('─' * 78) DarkGray }

try {
    # ── 2. Hedef ─────────────────────────────────────────────────────────────
    $hedef = if ($Project -eq 'All') {
        Join-Path $kok 'Text2Sql.sln'
    } else {
        Join-Path $kok "tests/Text2Sql.Tests.$Project/Text2Sql.Tests.$Project.csproj"
    }

    if (Test-Path $sonucDizini) { Remove-Item $sonucDizini -Recurse -Force }
    New-Item -ItemType Directory -Path $sonucDizini | Out-Null

    $argumanlar = @(
        'test', $hedef,
        '--nologo',
        '--logger', 'trx',
        '--results-directory', $sonucDizini,
        '--verbosity', 'quiet'
    )
    if ($Filter)   { $argumanlar += @('--filter', $Filter) }
    if ($Coverage) { $argumanlar += @('--collect', 'XPlat Code Coverage') }

    Cizgi
    Yaz "  Testler çalışıyor — hedef: $(Split-Path -Leaf $hedef)" Cyan
    if ($Filter) { Yaz "  Filtre: $Filter" DarkCyan }
    Cizgi

    $kronometre = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @argumanlar 2>&1 | ForEach-Object {
        # Derleme hatalarını göster, VSTest gürültüsünü gizle
        if ($_ -match 'error|hata' ) { Yaz $_ Red }
    }
    $kronometre.Stop()

    # ── 3. Sonuçları oku ─────────────────────────────────────────────────────
    $trxDosyalari = Get-ChildItem -Path $sonucDizini -Filter *.trx -Recurse
    if (-not $trxDosyalari) {
        Yaz "`n  Sonuç dosyası bulunamadı — derleme başarısız olmuş olabilir." Red
        exit 1
    }

    $tumSonuclar = foreach ($trx in $trxDosyalari) {
        $xml = [xml](Get-Content $trx.FullName -Encoding UTF8)

        $tanimlar = @{}
        foreach ($t in $xml.TestRun.TestDefinitions.UnitTest) {
            $tanimlar[$t.id] = $t.TestMethod.className
        }

        foreach ($r in $xml.TestRun.Results.UnitTestResult) {
            $sinif = $tanimlar[$r.testId]
            $parcalar = $sinif -split '\.'
            [pscustomobject]@{
                Proje    = ($sinif -replace '^(Text2Sql\.Tests\.[^.]+).*$', '$1')
                Grup     = if ($parcalar.Length -ge 2) { $parcalar[-2] } else { $sinif }
                Sinif    = $parcalar[-1]
                Ad       = $r.testName -replace '^.*\.', ''
                Sonuc    = $r.outcome
                Sure     = if ($r.duration) { [TimeSpan]::Parse($r.duration).TotalMilliseconds } else { 0 }
                Mesaj    = $r.Output.ErrorInfo.Message
                Yigin    = $r.Output.ErrorInfo.StackTrace
            }
        }
    }

    $gecen     = @($tumSonuclar | Where-Object Sonuc -eq 'Passed')
    $kalan     = @($tumSonuclar | Where-Object Sonuc -eq 'Failed')
    $atlanan   = @($tumSonuclar | Where-Object Sonuc -eq 'NotExecuted')

    # ── 4. Gruplanmış özet ───────────────────────────────────────────────────
    Write-Host ''
    Cizgi
    Yaz '  ÖZET' White
    Cizgi

    $tumSonuclar |
        Group-Object Proje, Grup |
        Sort-Object Name |
        ForEach-Object {
            $g = $_.Group
            $b = @($g | Where-Object Sonuc -eq 'Failed').Count
            $t = $g.Count
            $sure = [math]::Round(($g | Measure-Object Sure -Sum).Sum)
            $ad = $_.Name -replace 'Text2Sql\.Tests\.', '' -replace ', ', ' / '
            $renk = if ($b -gt 0) { 'Red' } else { 'Green' }
            $isaret = if ($b -gt 0) { '✗' } else { '✓' }
            Write-Host ("  {0} {1,-42} {2,3}/{3,-3} {4,6} ms" -f $isaret, $ad, ($t - $b), $t, $sure) -ForegroundColor $renk
        }

    # ── 5. Yavaş testler ─────────────────────────────────────────────────────
    Write-Host ''
    Yaz '  EN YAVAŞ 5 TEST' White
    $tumSonuclar | Sort-Object Sure -Descending | Select-Object -First 5 | ForEach-Object {
        Write-Host ("  {0,7} ms  {1}.{2}" -f [math]::Round($_.Sure), $_.Sinif, $_.Ad) -ForegroundColor DarkGray
    }

    # ── 6. Hatalar ───────────────────────────────────────────────────────────
    if ($kalan.Count -gt 0) {
        Write-Host ''
        Cizgi
        Yaz "  BAŞARISIZ TESTLER ($($kalan.Count))" Red
        Cizgi
        foreach ($h in $kalan) {
            Write-Host ''
            Yaz "  ✗ $($h.Sinif).$($h.Ad)" Red
            if ($h.Mesaj) {
                $h.Mesaj -split "`n" | Select-Object -First 6 | ForEach-Object { Yaz "      $_" Yellow }
            }
            if ($h.Yigin) {
                # Yalnızca kendi kodumuzdaki ilk satır — çerçeve gürültüsü değil
                $ilk = ($h.Yigin -split "`n" | Where-Object { $_ -match 'Text2Sql' } | Select-Object -First 1)
                if ($ilk) { Yaz "      ↳$ilk" DarkYellow }
            }
        }
    }

    if ($Detailed) {
        Write-Host ''
        Yaz '  GEÇEN TESTLER' White
        $gecen | Sort-Object Sinif, Ad | ForEach-Object {
            Write-Host ("  ✓ {0,-45} {1,5} ms" -f "$($_.Sinif).$($_.Ad)", [math]::Round($_.Sure)) -ForegroundColor DarkGreen
        }
    }

    # ── 7. Kod kapsamı ───────────────────────────────────────────────────────
    if ($Coverage) {
        Write-Host ''
        Cizgi
        Yaz '  KOD KAPSAMI (satır)' White
        Cizgi

        Get-ChildItem -Path $sonucDizini -Filter 'coverage.cobertura.xml' -Recurse | ForEach-Object {
            $cx = [xml](Get-Content $_.FullName -Encoding UTF8)
            foreach ($paket in $cx.coverage.packages.package) {
                $oran = [math]::Round([double]$paket.'line-rate' * 100, 1)
                $renk = if ($oran -ge 70) { 'Green' } elseif ($oran -ge 40) { 'Yellow' } else { 'Red' }
                Write-Host ("  {0,-42} %{1}" -f $paket.name, $oran) -ForegroundColor $renk
            }
        }
        Yaz "`n  Ayrıntılı HTML rapor için:" DarkGray
        Yaz '    dotnet tool install -g dotnet-reportgenerator-globaltool' DarkGray
        Yaz '    reportgenerator -reports:TestResults/**/coverage.cobertura.xml -targetdir:TestResults/rapor' DarkGray
    }

    # ── 8. Sonuç satırı ──────────────────────────────────────────────────────
    Write-Host ''
    Cizgi
    $sure = [math]::Round($kronometre.Elapsed.TotalSeconds, 1)
    if ($kalan.Count -eq 0) {
        Yaz "  ✓ $($gecen.Count) test geçti · $sure sn" Green
    } else {
        Yaz "  ✗ $($kalan.Count) başarısız · $($gecen.Count) geçti · $sure sn" Red
    }
    if ($atlanan.Count -gt 0) { Yaz "  ! $($atlanan.Count) test atlandı" Yellow }
    Cizgi

    if ($kalan.Count -eq 0) { exit 0 } else { exit 1 }
}
finally {
    [Console]::OutputEncoding = $oncekiKodSayfasi
}
