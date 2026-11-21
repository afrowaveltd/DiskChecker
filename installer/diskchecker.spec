Name:           diskchecker
Version:        1.0.0
Release:        1%{?dist}
Summary:        Disk health analysis and surface tests
License:        MIT
URL:            https://github.com/afrowaveltd/DiskChecker
BuildArch:      x86_64
Requires:       smartmontools

%description
Self-contained .NET DiskChecker GUI with SMART monitoring, surface testing,
and PDF disk certificates. Root rights are required for raw-disk operations.

%install
install -d %{buildroot}/opt/diskchecker
install -d %{buildroot}/usr/bin
install -d %{buildroot}/usr/share/applications
cp -a %{_diskchecker_source}/publish/linux-x64/. %{buildroot}/opt/diskchecker/
chmod 755 %{buildroot}/opt/diskchecker/DiskChecker.UI.Avalonia
install -m 755 %{_diskchecker_source}/installer/diskchecker-launcher %{buildroot}/usr/bin/diskchecker
install -m 644 %{_diskchecker_source}/installer/diskchecker.desktop %{buildroot}/usr/share/applications/diskchecker.desktop

%files
/opt/diskchecker
/usr/bin/diskchecker
/usr/share/applications/diskchecker.desktop

%changelog
* Sat Nov 21 2026 Afrowave Community <noreply@afrowave.ltd> - 1.0.0-1
- Prepare test installation packages, no data-directory deletion on uninstall.
