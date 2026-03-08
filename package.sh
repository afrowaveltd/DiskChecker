#!/bin/bash

# DiskChecker packaging script for Linux distributions

echo "Creating packages for DiskChecker..."

# Get version from project file
VERSION=$(grep -oP '<Version>.*</Version>' DiskChecker.UI.Avalonia/DiskChecker.UI.Avalonia.csproj | sed 's/<[^>]*>//g')

if [ -z "$VERSION" ]; then
    VERSION="1.0.0"
fi

echo "Package version: $VERSION"

# Create directories for packages
mkdir -p ./packages/deb/usr/bin
mkdir -p ./packages/deb/opt/diskchecker
mkdir -p ./packages/deb/usr/share/applications
mkdir -p ./packages/rpm/usr/bin
mkdir -p ./packages/rpm/opt/diskchecker
mkdir -p ./packages/rpm/usr/share/applications

# Copy files to package directories
cp -r ./publish/linux-x64/* ./packages/deb/opt/diskchecker/
cp -r ./publish/linux-x64/* ./packages/rpm/opt/diskchecker/

# Set permissions
chmod +x ./packages/deb/opt/diskchecker/DiskChecker.UI.Avalonia
chmod +x ./packages/rpm/opt/diskchecker/DiskChecker.UI.Avalonia

# Create symbolic links
ln -sf /opt/diskchecker/DiskChecker.UI.Avalonia ./packages/deb/usr/bin/diskchecker
ln -sf /opt/diskchecker/DiskChecker.UI.Avalonia ./packages/rpm/usr/bin/diskchecker

# Create desktop entries
cat > ./packages/deb/usr/share/applications/diskchecker.desktop << EOF
[Desktop Entry]
Name=DiskChecker
Comment=Professional Disk Diagnosis Tool
Exec=/opt/diskchecker/DiskChecker.UI.Avalonia
Icon=/opt/diskchecker/Assets/avalonia-logo.ico
Terminal=false
Type=Application
Categories=Utility;System;
EOF

cat > ./packages/rpm/usr/share/applications/diskchecker.desktop << EOF
[Desktop Entry]
Name=DiskChecker
Comment=Professional Disk Diagnosis Tool
Exec=/opt/diskchecker/DiskChecker.UI.Avalonia
Icon=/opt/diskchecker/Assets/avalonia-logo.ico
Terminal=false
Type=Application
Categories=Utility;System;
EOF

# Create DEB package
echo "Creating DEB package..."
mkdir -p ./packages/deb/DEBIAN
cat > ./packages/deb/DEBIAN/control << EOF
Package: diskchecker
Version: $VERSION
Section: utils
Priority: optional
Architecture: amd64
Depends: libc6 (>= 2.17)
Maintainer: DiskChecker Team <support@diskchecker.cz>
Description: Professional Disk Diagnosis Tool
 A comprehensive tool for diagnosing hard drives and SSDs.
 Features include SMART monitoring, surface testing, and reporting.
EOF

dpkg-deb --build ./packages/deb ./packages/diskchecker-$VERSION.deb

# Create RPM package
echo "Creating RPM package..."
mkdir -p ./packages/rpm/etc/init.d

cat > ./packages/rpm/etc/init.d/diskchecker << 'EOF'
#!/bin/bash
# chkconfig: 35 99 99
# description: DiskChecker service

# Source function library
. /etc/rc.d/init.d/functions

EXEC=/opt/diskchecker/DiskChecker.UI.Avalonia
LOCK_FILE=/var/lock/subsys/diskchecker

case "$1" in
    start)
        echo -n "Starting DiskChecker: "
        daemon $EXEC
        echo
        touch $LOCK_FILE
        ;;
    stop)
        echo -n "Shutting down DiskChecker: "
        killproc $EXEC
        echo
        rm -f $LOCK_FILE
        ;;
    status)
        status $EXEC
        ;;
    restart)
        $0 stop
        $0 start
        ;;
    *)
        echo "Usage: {start|stop|status|restart}"
        exit 1
        ;;
esac

exit 0
EOF

chmod +x ./packages/rpm/etc/init.d/diskchecker

# Create RPM spec file
cat > ./packages/rpm.spec << EOF
Name: diskchecker
Version: $VERSION
Release: 1
Summary: Professional Disk Diagnosis Tool

License: MIT
URL: https://github.com/diskchecker/diskchecker
Source0: diskchecker-%{version}.tar.gz

BuildArch: x86_64
Requires: glibc >= 2.17

%description
A comprehensive tool for diagnosing hard drives and SSDs.
Features include SMART monitoring, surface testing, and reporting.

%prep
%setup -q

%build

%install
rm -rf \$RPM_BUILD_ROOT
mkdir -p \$RPM_BUILD_ROOT/opt/diskchecker
cp -r ./packages/rpm/* \$RPM_BUILD_ROOT/

%files
/opt/diskchecker/*
/usr/bin/diskchecker
/usr/share/applications/diskchecker.desktop
/etc/init.d/diskchecker

%changelog
* $(date +"%a %b %d %Y") DiskChecker Team <support@diskchecker.cz> - $VERSION-1
- Initial package release
EOF

# Build RPM package
rpmbuild -bb ./packages/rpm.spec

echo "Packages created successfully!"
echo "DEB package: ./packages/diskchecker-$VERSION.deb"
echo "RPM package: ./packages/diskchecker-$VERSION.rpm"