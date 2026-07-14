@echo off
cd /d D:\DiskChecker\DiskChecker.Infrastructure\Services
type CertificateGenerator.cs > %TEMP%\cg_output.txt 2>&1
echo DONE
