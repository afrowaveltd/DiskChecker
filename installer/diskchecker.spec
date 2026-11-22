Name:           diskchecker
Version:        1.0.0
Release:        1%{?dist}
Summary:        Disk health analysis and surface tests
License:        MIT
URL:            https://github.com/afrowaveltd/DiskChecker
BuildArch:      x86_64
Requires:       smartmontools
Requires:       polkit

%description
Self-contained .NET DiskChecker GUI with SMART monitoring, surface testing,
and PDF disk certificates. Root rights are required for raw-disk operations.

%install
install -d %{buildroot}/opt/diskchecker
install -d %{buildroot}/usr/bin
install -d %{buildroot}/usr/lib/diskchecker
install -d %{buildroot}/usr/share/applications
install -d %{buildroot}/usr/share/polkit-1/actions
install -d %{buildroot}/usr/share/icons/hicolor/scalable/apps
cp -a %{_diskchecker_source}/publish/linux-x64/. %{buildroot}/opt/diskchecker/
chmod 755 %{buildroot}/opt/diskchecker/DiskChecker.UI.Avalonia
install -m 755 %{_diskchecker_source}/installer/diskchecker-launcher %{buildroot}/usr/bin/diskchecker
install -m 755 %{_diskchecker_source}/installer/diskchecker-elevated %{buildroot}/usr/lib/diskchecker/diskchecker-elevated
install -m 644 %{_diskchecker_source}/installer/diskchecker.desktop %{buildroot}/usr/share/applications/diskchecker.desktop
install -m 644 %{_diskchecker_source}/installer/org.afrowave.diskchecker.policy %{buildroot}/usr/share/polkit-1/actions/org.afrowave.diskchecker.policy
install -m 644 %{_diskchecker_source}/DiskChecker.UI.Avalonia/Assets/disk-checker-icon.svg %{buildroot}/usr/share/icons/hicolor/scalable/apps/diskchecker.svg

%files
/opt/diskchecker
/usr/bin/diskchecker
%dir /usr/lib/diskchecker
/usr/lib/diskchecker/diskchecker-elevated
/usr/share/applications/diskchecker.desktop
/usr/share/polkit-1/actions/org.afrowave.diskchecker.policy
/usr/share/icons/hicolor/scalable/apps/diskchecker.svg

%changelog
* Sat Nov 21 2026 Afrowave Community <noreply@afrowave.ltd> - 1.0.0-1
- Prepare test installation packages, no data-directory deletion on uninstall.
