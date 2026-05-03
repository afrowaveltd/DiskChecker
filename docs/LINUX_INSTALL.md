# DiskChecker – Instalace a použití na Linuxu

## Požadavky

- **OS**: Ubuntu 20.04+, Debian 11+, Fedora 33+, nebo jakákoliv moderní distribuce
- **Architektura**: x64 (amd64) nebo ARM64
- **.NET Runtime**: Není vyžadován (self-contained build)
- **Práva**: Root/sudo pro přístup k diskům

## Závislosti

```bash
# Ubuntu/Debian
sudo apt update && sudo apt install smartmontools

# Fedora/RHEL/CentOS
sudo dnf install smartmontools

# Arch Linux
sudo pacman -S smartmontools
```

## Instalace

### 1. Build ze zdrojového kódu

```bash
git clone https://github.com/afrowaveltd/DiskChecker.git
cd DiskChecker
dotnet publish DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj \
  -c Release -r linux-x64 --self-contained true -o ./publish/linux-x64
```

### 2. Instalační skript

```bash
sudo ./installer/install-linux.sh
```

Aplikace se nainstaluje do `/opt/diskchecker` a vytvoří symlink `/usr/local/bin/diskchecker`.

### 3. DEB balíček

```bash
./package.sh          # Vytvoří DEB balíček v packages/
sudo dpkg -i packages/diskchecker-1.0.0-linux-x64.deb
```

## Spuštění

```bash
# Přes symlink
sudo diskchecker

# Nebo přímo
sudo /opt/diskchecker/DiskChecker.UI.Avalonia
```

## Práva

Aplikace vyžaduje **root práva** pro:
- Čtení SMART dat (`/dev/sd*`, `/dev/nvme*`)
- Přímý přístup k disku (povrchové testy)
- Zápis na úrovni sektorů (sanitzace)

### Alternativy ke spouštění přes sudo

```bash
# Přidání uživatele do skupiny disk (vyžaduje odhlášení/refresh)
sudo usermod -a -G disk $USER

# Nastavení capabilities (bezpečnější než plný root)
sudo setcap cap_sys_rawio+ep /opt/diskchecker/DiskChecker.UI.Avalonia
```

## Řešení problémů

| Problém | Řešení |
|---------|--------|
| `smartctl: command not found` | `sudo apt install smartmontools` |
| `Permission denied` na `/dev/sda` | Spusťte s `sudo` nebo přidejte uživatele do skupiny `disk` |
| Nebyly nalezeny žádné disky | Ověřte: `lsblk -d -n -o NAME,SIZE,MODEL` |
| SMART data nejsou dostupná | Ověřte: `sudo smartctl -i /dev/sda` |
| Aplikace se zasekává na 3. disku | Opraveno – jednotlivé timeouty (15–30 s) na nereagující disky |

## Kompatibilita

| Funkce | Linux | Windows |
|--------|-------|---------|
| SMART data | ✅ `smartctl` | ✅ WMI/smartctl |
| Výpis disků | ✅ `lsblk` / `/sys/block` | ✅ Win32_DiskDrive |
| Povrchový test | ✅ `O_DIRECT` | ✅ Win32 NO_BUFFERING |
| Sanitzace | ✅ Raw `/dev/sdX` | ✅ `\\.\PHYSICALDRIVE` |
| PDF export | ✅ | ✅ |
| Email reporty | ✅ SMTP | ✅ SMTP |

## Podpora

- **Issues**: [GitHub Issues](https://github.com/afrowaveltd/DiskChecker/issues)
- **Licence**: viz [LICENSE](../LICENSE)