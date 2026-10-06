#!/usr/bin/env bash
# Testleri çalıştırır ve okunabilir bir özet basar (Linux/macOS).
# Windows için: scripts/test.ps1
set -uo pipefail

KOK="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SONUC="$KOK/TestResults"

export DOTNET_CLI_UI_LANGUAGE=tr
export LANG="${LANG:-tr_TR.UTF-8}"

PROJE="All"; FILTRE=""; KAPSAM=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    -p|--project)  PROJE="$2"; shift 2 ;;
    -f|--filter)   FILTRE="$2"; shift 2 ;;
    -c|--coverage) KAPSAM=1; shift ;;
    -h|--help)
      echo "Kullanım: $0 [-p Unit|Architecture|Integration] [-f <filtre>] [-c]"
      exit 0 ;;
    *) echo "Bilinmeyen seçenek: $1"; exit 1 ;;
  esac
done

if [[ "$PROJE" == "All" ]]; then
  HEDEF="$KOK/Text2Sql.sln"
else
  HEDEF="$KOK/tests/Text2Sql.Tests.$PROJE/Text2Sql.Tests.$PROJE.csproj"
fi

rm -rf "$SONUC"; mkdir -p "$SONUC"

ARGS=(test "$HEDEF" --nologo --logger "trx" --results-directory "$SONUC" --verbosity quiet)
[[ -n "$FILTRE" ]] && ARGS+=(--filter "$FILTRE")
[[ $KAPSAM -eq 1 ]] && ARGS+=(--collect:"XPlat Code Coverage")

printf '\n\033[36m── Testler çalışıyor: %s ──\033[0m\n' "$(basename "$HEDEF")"
dotnet "${ARGS[@]}" | grep -Ei "error|hata" || true

# TRX özeti — python3 varsa gruplanmış, yoksa sade
if command -v python3 >/dev/null 2>&1; then
  python3 - "$SONUC" <<'PYEOF'
import glob, sys, xml.etree.ElementTree as ET
from collections import defaultdict

ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
gruplar = defaultdict(lambda: [0, 0, 0.0])   # [gecen, kalan, ms]
hatalar, sureler = [], []

for dosya in glob.glob(f"{sys.argv[1]}/**/*.trx", recursive=True):
    kok = ET.parse(dosya).getroot()
    tanim = {u.get('id'): u.find('t:TestMethod', ns).get('className')
             for u in kok.iterfind('.//t:UnitTest', ns)}

    for r in kok.iterfind('.//t:UnitTestResult', ns):
        sinif = tanim.get(r.get('testId'), '?')
        parcalar = sinif.split('.')
        grup = '.'.join(parcalar[2:-1]) or parcalar[-1]
        sure = r.get('duration', '00:00:00')
        h, d, s = sure.split(':')
        ms = (int(h) * 3600 + int(d) * 60 + float(s)) * 1000

        basarili = r.get('outcome') == 'Passed'
        gruplar[grup][0 if basarili else 1] += 1
        gruplar[grup][2] += ms
        sureler.append((ms, f"{parcalar[-1]}.{r.get('testName','').split('.')[-1]}"))

        if not basarili:
            bilgi = r.find('.//t:ErrorInfo/t:Message', ns)
            hatalar.append((f"{parcalar[-1]}.{r.get('testName','').split('.')[-1]}",
                            (bilgi.text or '').strip() if bilgi is not None else ''))

print("\n\033[1m  ÖZET\033[0m")
for ad in sorted(gruplar):
    gecen, kalan, ms = gruplar[ad]
    renk = "\033[31m" if kalan else "\033[32m"
    isaret = "✗" if kalan else "✓"
    print(f"  {renk}{isaret} {ad:<40} {gecen:>3}/{gecen+kalan:<3} {ms:>7.0f} ms\033[0m")

print("\n\033[1m  EN YAVAŞ 5 TEST\033[0m")
for ms, ad in sorted(sureler, reverse=True)[:5]:
    print(f"  \033[90m{ms:>7.0f} ms  {ad}\033[0m")

if hatalar:
    print(f"\n\033[31m\033[1m  BAŞARISIZ ({len(hatalar)})\033[0m")
    for ad, mesaj in hatalar:
        print(f"\n  \033[31m✗ {ad}\033[0m")
        for satir in mesaj.splitlines()[:6]:
            print(f"      \033[33m{satir}\033[0m")

toplam_gecen = sum(g[0] for g in gruplar.values())
toplam_kalan = sum(g[1] for g in gruplar.values())
print()
if toplam_kalan:
    print(f"  \033[31m✗ {toplam_kalan} başarısız · {toplam_gecen} geçti\033[0m")
    sys.exit(1)
print(f"  \033[32m✓ {toplam_gecen} test geçti\033[0m")
PYEOF
  exit $?
fi

echo "python3 bulunamadı — ham TRX dosyaları: $SONUC"
