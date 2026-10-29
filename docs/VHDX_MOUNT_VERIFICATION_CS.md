# Automatická kontrola připojitelnosti záloh (Windows / Debian)

Po vytvoření souboru VHDX v běžném zálohovacím workflow DiskChecker:

1. Dokončí zápis, flush a kontrolu interní struktury (`VhdxImageUtility`).
2. Nezávisle se pokusí **připojit obraz pouze pro čtení** prostřednictvím OS.
3. Pokud se připojení podaří, zobrazí dialog:
   - **ANO:** ponechat obraz připojený pro čtení zachráněných dat.
   - **NE:** odpojit obraz; samotný soubor zálohy zůstává beze změny.
4. Zapíše výsledek do souboru `<jméno_obrazu>.verification.json`
   ve stejné složce jako záloha. Neúspěch validace **nemaže** obraz,
   ale vyvolá výrazné upozornění; systém nepovažuje připojení za ověřené.
5. Pokud během kopírování byly některé bloky zdrojového média nečitelné,
   jejich nahrazení nulami je evidováno samostatně, i když je obraz připojitelný.

## Windows

Používá se vestavěný `Mount-DiskImage -Access ReadOnly`, dotaz `Get-DiskImage`
a následně případně `Dismount-DiskImage`; je vyžadováno spuštění jako
správce. Je-li VHDX již připojen jiným programem, DiskChecker ho **neodpojuje**.
Pokud je připojen v read-write režimu, kontrola ho neoznačí za bezpečnou
read-only kopii.

Ponechaný obraz najdeš podle písmene oddílu v Průzkumníku nebo v
**Správě disků** (pokud oddíl nemá přidělené písmeno).
Pro ruční odpojení lze použít `Dismount-DiskImage -ImagePath "C:\\...\\disk_image.vhdx"`.

Běžný `.img` není tímto Windows mechanismem podporován; nejprve je možné
použít funkci DiskCheckeru **RAW → VHDX**.

## Debian / Linux

Používá `qemu-nbd --read-only --format=vhdx --connect=/dev/nbdX`
(pro RAW IMG `--format=raw`), kontroluje, že vzniklé blokové zařízení
má nenulovou virtuální kapacitu, a pokusí se připojit rozpoznané oddíly
se souborovým systémem v read-only režimu. Pokud oddíly nelze připojit
(BitLocker, neznámý FS, poškozený FS), zůstane alespoň read-only
blokové zařízení `/dev/nbdX`.

Požadavky:

```bash
sudo apt install qemu-utils util-linux kmod parted
sudo modprobe nbd max_part=16
```

Aplikace potřebuje root práva pro NBD, typicky `sudo`.

Ponechané připojení je nutné později ručně odpojit, například:

```bash
sudo umount /cesta/k/zaloze/disk_image_readonly/volume1
sudo qemu-nbd --disconnect /dev/nbd0
```

Použij přesný mountpoint a NBD zařízení oznámené aplikací.
Nevypínej NBD, dokud existuje připojený oddíl, který se používá.

## Co ověření prokazuje / neprokazuje

Úspěšné připojení prokazuje, že operační systém rozpoznal obraz
jako virtuální disk. **Neprokazuje** shodu každého bajtu s původním
zařízením ani integritu všech souborů. Při podezření na poškození média
je nadále potřeba kontrola obsahu, souborových systémů a záložních kopií.

Kontrola read-only chrání data v souboru zálohy před zápisem z nově
připojeného virtuálního disku. U obrazů neznámého původu ale nadále
existují bezpečnostní rizika při práci s nedůvěryhodným souborovým
systémem, zvláště při připojování přes kernelový NBD/mount.

## Testovací poznámky

Automatické unit testy neprovádějí privilegovaný mount hostitelského
stroje: testují bezpečné scénáře s neexistujícím souborem a s obrazem,
který už připojil jiný program. Skutečný mount/detach je integrační
operace závislá na OS, administrátorských oprávněních a dostupnosti
systémových nástrojů a vyžaduje ruční ověření na obou platformách.
