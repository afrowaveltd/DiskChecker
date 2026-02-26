# 🐧 DiskChecker Linux Installation & Usage

## ✅ System Requirements

- **OS**: Ubuntu 20.04+, Debian 11+, Fedora 33+, CentOS 8+, or any modern Linux distro
- **Architecture**: x64 (amd64) or ARM64
- **.NET Runtime**: Not required (self-contained build)
- **Privileges**: Root/sudo access for disk operations

## 📦 Dependencies

Install required tools:

```bash
# Ubuntu/Debian
sudo apt update
sudo apt install smartmontools

# Fedora/RHEL/CentOS
sudo dnf install smartmontools

# Arch Linux
sudo pacman -S smartmontools
```

## 🚀 Quick Start

### Option 1: Download Release

1. Download `DiskChecker-linux-x64` from [Releases](https://github.com/afrowaveltd/DiskChecker/releases)
2. Make executable:
   ```bash
   chmod +x DiskChecker-linux-x64
   ```
3. Run with sudo:
   ```bash
   sudo ./DiskChecker-linux-x64
   ```

### Option 2: Build from Source

```bash
# Clone repository
git clone https://github.com/afrowaveltd/DiskChecker.git
cd DiskChecker

# Build for x64
./scripts/build-linux.sh x64

# OR build for ARM64
./scripts/build-linux.sh arm64

# Run
sudo ./publish/linux-x64/DiskChecker.UI
```

## 📖 Usage

### List Available Disks

```bash
sudo ./DiskChecker-linux-x64
# Select option: 1) Zkontrolovat disk
```

### Check Disk Health

```bash
sudo ./DiskChecker-linux-x64
# 1. Select disk (e.g., /dev/sda)
# 2. View SMART data
```

### Full Surface Test

```bash
sudo ./DiskChecker-linux-x64
# Select: 2) Úplný test disku
# Choose test profile
```

### Disk Sanitization

```bash
sudo ./DiskChecker-linux-x64
# Select sanitization profile
# ⚠️ WARNING: This ERASES ALL DATA!
```

## 🔒 Permissions

DiskChecker requires **root privileges** for:
- Reading SMART data (`/dev/sd*`)
- Direct disk access (surface testing)
- Raw sector write (sanitization)

### Running without `sudo` each time:

```bash
# Add yourself to disk group (logout/login required)
sudo usermod -a -G disk $USER

# Set capabilities (safer than full root)
sudo setcap cap_sys_rawio+ep ./DiskChecker-linux-x64
```

## 🔍 Troubleshooting

### "smartctl: command not found"

```bash
sudo apt install smartmontools  # Ubuntu/Debian
```

### "Permission denied" on /dev/sda

```bash
# Run with sudo
sudo ./DiskChecker-linux-x64

# OR add user to disk group
sudo usermod -a -G disk $USER
# Then logout and login again
```

### "No disks found"

```bash
# Check if lsblk works
lsblk -d -n -o NAME,PATH,SIZE,MODEL

# Check permissions
ls -l /dev/sd*
```

### SMART data not available

```bash
# Check if smartctl works
sudo smartctl -i /dev/sda

# Enable SMART on disk
sudo smartctl -s on /dev/sda
```

## 🆚 Feature Parity: Linux vs Windows

| Feature | Linux | Windows |
|---------|-------|---------|
| SMART data | ✅ `smartctl` | ✅ WMI/smartctl |
| Disk listing | ✅ `lsblk` | ✅ WMIC/WMI |
| Surface test | ✅ `O_DIRECT` | ✅ Win32 NO_BUFFERING |
| Sanitization | ✅ Raw `/dev/sdX` | ✅ `\\.\PHYSICALDRIVE` |
| Terminal UI | ✅ Spectre.Console | ✅ Spectre.Console |
| Email reports | ✅ SMTP | ✅ SMTP |
| PDF export | ✅ | ✅ |

**100% functional parity!** 🎉

## 📝 Configuration

Config file: `appsettings.json` (same directory as executable)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "EmailSettings": {
    "SmtpServer": "smtp.example.com",
    "SmtpPort": 587,
    "UseSsl": true
  }
}
```

## 🐛 Known Issues

- **ARM64**: Tested on Raspberry Pi 4 with Ubuntu 22.04 ✅
- **NVMe drives**: Supported via `smartctl` (nvme-cli not required)
- **RAID arrays**: Individual disks accessible, RAID controller support varies

## 💡 Tips

- **Performance**: Surface testing is I/O intensive - expect 100-200 MB/s
- **Sanitization**: Full 500GB disk takes ~30-60 minutes
- **Background running**: Use `screen` or `tmux` for long operations

```bash
# Install screen
sudo apt install screen

# Run in background
screen -S diskcheck
sudo ./DiskChecker-linux-x64
# Press Ctrl+A then D to detach

# Reattach later
screen -r diskcheck
```

## 🤝 Support

- **Issues**: [GitHub Issues](https://github.com/afrowaveltd/DiskChecker/issues)
- **Docs**: [Full Documentation](https://github.com/afrowaveltd/DiskChecker/wiki)

## 📜 License

[Insert License Here]
