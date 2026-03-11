@echo off
powershell -NoProfile -ExecutionPolicy Bypass -Command "Get-Content 'E:\C#\DiskChecker\DiskChecker.UI.Avalonia\ViewModels\SmartCheckViewModel.cs' | Select-Object -Skip 1145 -First 130"