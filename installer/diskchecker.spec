Name:           diskchecker
Version:        1.0.0
Release:        1%{?dist}
Summary:        Cross-platform disk diagnostics and SMART analysis tool
License:        MIT
URL:            https://github.com/diskchecker/diskchecker
BuildArch:      x86_64

Requires:       glibc >= 2.28
Requires:       libicu
Requires:       openssl-libs >= 3.0.0
Requires:       smartmontools

%description
DiskChecker is a comprehensive disk diagnostics tool that provides:
- SMART data analysis and health monitoring
- Surface testing with performance benchmarking
- Disk sanitization with verification
- Detailed disk information and quality ratings
- Export reports in multiple formats (PDF, CSV, JSON)
- Beautiful cross-platform UI (Windows, Linux)

This package contains the Avalonia-based Linux application.

%install
mkdir -p %{buildroot}/opt/diskchecker
mkdir -p %{buildroot}/var/lib/diskchecker
mkdir -p %{buildroot}/usr/bin
mkdir -p %{buildroot}/usr/share/applications
mkdir -p %{buildroot}/usr/share/icons/hicolor/256x256/apps

cp -r publish/linux-x64/* %{buildroot}/opt/diskchecker/
ln -s /opt/diskchecker/DiskChecker.UI.Avalonia %{buildroot}/usr/bin/diskchecker
cp installer/diskchecker.desktop %{buildroot}/usr/share/applications/
chmod 755 %{buildroot}/opt/diskchecker/DiskChecker.UI.Avalonia
chmod 755 %{buildroot}/var/lib/diskchecker

%files
%defattr(-,root,root,-)
/opt/diskchecker
/usr/bin/diskchecker
/usr/share/applications/diskchecker.desktop
%dir /var/lib/diskchecker

%post
echo "DiskChecker installed successfully!"
echo "Run with: sudo diskchecker (requires root for disk access)"

%postun
rm -rf /var/lib/diskchecker 2>/dev/null || true
rm -f /usr/bin/diskchecker 2>/dev/null || true

%changelog
* Sat Mar 08 2025 DiskChecker <info@diskchecker.org> - 1.0.0-1
- Initial release