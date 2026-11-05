# DiskChecker: skutecne cteci testy povrchu (2026-11-05)

Graficka aplikace Avalonia mela historicky simulovany SurfaceTestViewModel.RunTestAsync:
rychlosti generovane nahodne, test bez realneho I/O, uspesny zaznam i bez precteni disku.
Tato cesta je odstranena.

## Profily GUI
- Cteni 100 MB: prvnich 100 MiB, bez zapisu.
- Cteni 1 GB: prvni 1 GiB, bez zapisu.
- Cteni celeho disku / Full Read: sekvencni cteni vsech deklarovanych bajtu bez zapisu.
- Sanitizace: puvodni DESTRUKTIVNI postup zapisu nul, overeni, spravy oddilu.

Historicke simulovane plne zapisove profily jsou z GUI odebrany. Skutecny zapis
a overeni je nadale oddelenou volbou v TUI.

## Full Read
SurfaceReadScanner otevre disk pouze jako FileAccess.Read. Nezapise zadna data,
nevytvori oddily a neporovnava obsah se vzorem ci nulami. Cteni muze selhat
kvuli pristupovym pravum, chybe disku nebo obsazenemu svazku. EOF pred koncem
a chyby cteni se vzdy eviduji jako selhani spolu s poslednim byte offsetem.
SMART chyby se tim samy nevymazou. Systemova cache muze pri opakovanem cteni
obslouzit cast dat bez fyzickeho pristupu k plotnam; explicitni direct-I/O
je budouci rozsireni a vysledky se nesmeji vydavat za primy povrchovy sken.

Rychlosti v grafech pochazeji ze skutecneho cteni. Full Read se zapisuje jako
TestType.FullRead, omezeny rozsah jako TestType.QuickRead. Read-only
test nepocita zapisovane bajty ani overovaci chyby.

## Testy
dotnet test tests/DiskChecker.Tests/DiskChecker.Tests.csproj -c Release

Regresni testy pouzivaji docasne soubory, overuji nemennost obsahu,
ukonceni na EOF, rozsah testu a zruseni. Fyzicke testy na Windows/Linux
jsou zapotrebi pred pouzitim vysledku pro rozhodovani o realnych discich.
