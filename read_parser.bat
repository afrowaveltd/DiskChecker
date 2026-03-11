@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Content 'E:\C#\DiskChecker\DiskChecker.Infrastructure\Hardware\SmartctlJsonParser.cs' | Select-Object -Skip 545 -First 30"