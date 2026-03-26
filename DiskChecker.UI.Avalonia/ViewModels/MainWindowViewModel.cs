using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Core.Interfaces;
using DiskChecker.UI.Avalonia.Services.Interfaces;

namespace DiskChecker.UI.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private ViewModelBase? _currentContent;
    private string _statusMessage = "Připraven";
    private Type? _currentViewModelType;
    
    // System info properties
    private string _osName = "Načítám...";
    private string _kernelVersion = "";
    private string _cpuInfo = "Načítám...";
    private string _ramInfo = "Načítám...";
    private string _runtimeInfo = ".NET 10";
    private string _appVersion = "v1.0.0";
    private bool _isLinux;
    private bool _isWindows;
    
    // Theme properties
    private bool _isDarkTheme;

    public MainWindowViewModel(INavigationService navigationService, ISettingsService settingsService)
    {
        _navigationService = navigationService;
        _settingsService = settingsService;
        _navigationService.Navigated += OnNavigated;
        
        // Load theme preference
        _ = LoadThemePreferenceAsync();
        
        // Load system information
        LoadSystemInfo();
        
        // Navigate to initial view
        _navigationService.NavigateTo<DiskSelectionViewModel>();
    }
    
    private async System.Threading.Tasks.Task LoadThemePreferenceAsync()
    {
        _isDarkTheme = await _settingsService.GetIsDarkThemeAsync();
        ApplyTheme();
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }
    
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (SetProperty(ref _isDarkTheme, value))
            {
                _ = SaveThemePreferenceAsync(value);
                ApplyTheme();
                OnPropertyChanged(nameof(IsLightTheme));
            }
        }
    }
    
    public bool IsLightTheme => !_isDarkTheme;
    
    // Icon opacities for smooth transition
    public double LightIconOpacity => IsDarkTheme ? 0.4 : 1.0;
    public double DarkIconOpacity => IsDarkTheme ? 1.0 : 0.4;
    
    private async System.Threading.Tasks.Task SaveThemePreferenceAsync(bool isDark)
    {
        await _settingsService.SetIsDarkThemeAsync(isDark);
        StatusMessage = isDark ? "Tmavý motiv aktivován" : "Světlý motiv aktivován";
    }
    
    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !_isDarkTheme;
    }
    
    private void ApplyTheme()
    {
        var app = global::Avalonia.Application.Current;
        if (app != null)
        {
            app.RequestedThemeVariant = _isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
            
            // Update our custom brush resources dynamically
            UpdateThemeResources(app.Resources, _isDarkTheme);
        }
        OnPropertyChanged(nameof(LightIconOpacity));
        OnPropertyChanged(nameof(DarkIconOpacity));
        OnPropertyChanged(nameof(IsLightTheme));
    }
    
    private void UpdateThemeResources(global::Avalonia.Controls.IResourceDictionary resources, bool isDark)
    {
        // Define light and dark color values
        var colors = isDark ? new Dictionary<string, string>
        {
            // Dark Theme Colors
            ["ThemeBackgroundBrush"] = "#0D1117",
            ["ThemeSecondaryBackgroundBrush"] = "#161B22",
            ["ThemeCardBackgroundBrush"] = "#21262D",
            ["ThemePanelBackgroundBrush"] = "#161B22",
            ["ThemeBorderBrush"] = "#30363D",
            ["ThemeBorderLightBrush"] = "#21262D",
            ["ThemePrimaryBrush"] = "#58A6FF",
            ["ThemePrimaryHoverBrush"] = "#79B8FF",
            ["ThemePrimaryLightBrush"] = "#388BFD20",
            ["ThemeTextPrimaryBrush"] = "#F0F6FC",
            ["ThemeTextSecondaryBrush"] = "#C9D1D9",
            ["ThemeTextMutedBrush"] = "#8B949E",
            ["ThemeNavActiveBrush"] = "#58A6FF",
            ["ThemeNavInactiveTextBrush"] = "#C9D1D9",
            ["ThemeHeaderGradient1"] = "#0D419D",
            ["ThemeHeaderGradient2"] = "#196C2E",
            ["ThemeSuccessBrush"] = "#3FB950",
            ["ThemeWarningBrush"] = "#D29922",
            ["ThemeDangerBrush"] = "#F85149",
            ["ThemeGraphBackgroundBrush"] = "#0A0F18",
        } : new Dictionary<string, string>
        {
            // Light Theme Colors
            ["ThemeBackgroundBrush"] = "#FFFFFF",
            ["ThemeSecondaryBackgroundBrush"] = "#F8F9FA",
            ["ThemeCardBackgroundBrush"] = "#FFFFFF",
            ["ThemePanelBackgroundBrush"] = "#F0F2F5",
            ["ThemeBorderBrush"] = "#DEE2E6",
            ["ThemeBorderLightBrush"] = "#E9ECEF",
            ["ThemePrimaryBrush"] = "#004B93",
            ["ThemePrimaryHoverBrush"] = "#003B7A",
            ["ThemePrimaryLightBrush"] = "#E3F2FD",
            ["ThemeTextPrimaryBrush"] = "#1A1A1A",
            ["ThemeTextSecondaryBrush"] = "#495057",
            ["ThemeTextMutedBrush"] = "#6C757D",
            ["ThemeNavActiveBrush"] = "#004B93",
            ["ThemeNavInactiveTextBrush"] = "#495057",
            ["ThemeHeaderGradient1"] = "#1F6FEB",
            ["ThemeHeaderGradient2"] = "#238636",
            ["ThemeSuccessBrush"] = "#27AE60",
            ["ThemeWarningBrush"] = "#F39C12",
            ["ThemeDangerBrush"] = "#E74C3C",
            ["ThemeGraphBackgroundBrush"] = "#0F1F33",
        };
        
        foreach (var kvp in colors)
        {
            if (Color.TryParse(kvp.Value, out var color))
            {
                resources[kvp.Key] = new SolidColorBrush(color);
            }
        }
    }

    private void LoadSystemInfo()
    {
        try
        {
            // Detect OS
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                IsLinux = true;
                IsWindows = false;
                OsName = "Linux";
                
                // Get Linux distribution info
                _ = GetLinuxSystemInfo();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                IsLinux = false;
                IsWindows = true;
                OsName = "Windows";
                
                // Get Windows version
                _ = GetWindowsSystemInfo();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                IsLinux = false;
                IsWindows = false;
                OsName = "macOS";
                KernelVersion = Environment.OSVersion.Version.ToString();
            }
            else
            {
                OsName = RuntimeInformation.OSDescription;
                KernelVersion = RuntimeInformation.FrameworkDescription;
            }
            
            // Get CPU info (works on all platforms)
            CpuInfo = GetCpuInfo();
            
            // Get RAM info
            RamInfo = GetRamInfo();
            
            // Runtime info
            RuntimeInfo = ".NET 10";
            
            // App version
            AppVersion = $"v{GetAppVersion()}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading system info: {ex.Message}");
            OsName = "Neznámý OS";
            KernelVersion = "";
            CpuInfo = "N/A";
            RamInfo = "N/A";
        }
    }
    
    private async System.Threading.Tasks.Task GetLinuxSystemInfo()
    {
        try
        {
            // Get kernel version
            var kernelVersion = await ExecuteCommandAsync("uname", "-r");
            KernelVersion = !string.IsNullOrEmpty(kernelVersion) ? kernelVersion.Trim() : "Linux";
            
            // Get distribution name
            var distroInfo = await ExecuteCommandAsync("cat", "/etc/os-release");
            if (!string.IsNullOrEmpty(distroInfo))
            {
                foreach (var line in distroInfo.Split('\n'))
                {
                    if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))
                    {
                        var name = line.Substring("PRETTY_NAME=".Length).Trim('"');
                        if (!string.IsNullOrEmpty(name))
                        {
                            OsName = name.Length > 30 ? name.Substring(0, 27) + "..." : name;
                        }
                        break;
                    }
                }
            }
        }
        catch
        {
            KernelVersion = "Linux";
        }
    }
    
    private async System.Threading.Tasks.Task GetWindowsSystemInfo()
    {
        try
        {
            // Get Windows version
            var versionOutput = await ExecuteCommandAsync("powershell", 
                "-NoProfile -Command \"[System.Environment]::OSVersion.Version.ToString()\"");
            
            if (!string.IsNullOrEmpty(versionOutput))
            {
                var version = versionOutput.Trim();
                
                // Try to get friendly name
                var buildNumber = version.Split('.');
                if (buildNumber.Length >= 2)
                {
                    var major = int.Parse(buildNumber[0]);
                    var minor = buildNumber.Length > 1 ? int.Parse(buildNumber[1]) : 0;
                    
                    OsName = major switch
                    {
                        10 => "Windows 10/11",
                        6 => minor switch
                        {
                            3 => "Windows 8.1",
                            2 => "Windows 8",
                            1 => "Windows 7",
                            _ => "Windows"
                        },
                        _ => "Windows"
                    };
                }
                
                KernelVersion = $"Build {version}";
            }
        }
        catch
        {
            OsName = "Windows";
            KernelVersion = "";
        }
    }
    
    private static async System.Threading.Tasks.Task<string?> ExecuteCommandAsync(string command, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(psi);
            if (process == null) return null;
            
            // Read output and wait for exit in parallel to avoid deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            var output = await outputTask;
            
            return output;
        }
        catch
        {
            return null;
        }
    }
    
    private static string GetCpuInfo()
    {
        try
        {
            var cpuCount = Environment.ProcessorCount;
            var cpuCores = $"{cpuCount} jader";
            
            // Try to get CPU name on Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    if (System.IO.File.Exists("/proc/cpuinfo"))
                    {
                        var cpuInfo = System.IO.File.ReadAllLines("/proc/cpuinfo");
                        foreach (var line in cpuInfo)
                        {
                            if (line.StartsWith("model name", StringComparison.Ordinal))
                            {
                                var name = line.Split(':')[1].Trim();
                                // Shorten the name if too long
                                if (name.Length > 25)
                                {
                                    name = name.Substring(0, 22) + "...";
                                }
                                return $"{name} ({cpuCores})";
                            }
                        }
                    }
                }
                catch { }
            }
            
            return cpuCores;
        }
        catch
        {
            return "N/A";
        }
    }
    
    private static string GetRamInfo()
    {
        try
        {
            // Use GC to get memory info
            var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var totalGb = totalMemory / (1024.0 * 1024.0 * 1024.0);
            
            // Try to get system memory on Linux
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    if (System.IO.File.Exists("/proc/meminfo"))
                    {
                        var memInfo = System.IO.File.ReadAllLines("/proc/meminfo");
                        foreach (var line in memInfo)
                        {
                            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                            {
                                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2 && long.TryParse(parts[1], out var memKb))
                                {
                                    var memGb = memKb / (1024.0 * 1024.0);
                                    return $"{memGb:F1} GB RAM";
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            
            return totalGb > 0 ? $"{totalGb:F1} GB RAM" : "N/A";
        }
        catch
        {
            return "N/A";
        }
    }
    
    private static string GetAppVersion()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    public ViewModelBase? CurrentContent
    {
        get => _currentContent;
        set => SetProperty(ref _currentContent, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    
    /// <summary>
    /// Operating system name (e.g., "Windows 10", "Ubuntu 22.04").
    /// </summary>
    public string OsName
    {
        get => _osName;
        set => SetProperty(ref _osName, value);
    }
    
    /// <summary>
    /// Kernel version (Linux kernel version or Windows build number).
    /// </summary>
    public string KernelVersion
    {
        get => _kernelVersion;
        set => SetProperty(ref _kernelVersion, value);
    }
    
    /// <summary>
    /// CPU information (model name and core count).
    /// </summary>
    public string CpuInfo
    {
        get => _cpuInfo;
        set => SetProperty(ref _cpuInfo, value);
    }
    
    /// <summary>
    /// RAM information (total memory).
    /// </summary>
    public string RamInfo
    {
        get => _ramInfo;
        set => SetProperty(ref _ramInfo, value);
    }
    
    /// <summary>
    /// Runtime information (.NET version).
    /// </summary>
    public string RuntimeInfo
    {
        get => _runtimeInfo;
        set => SetProperty(ref _runtimeInfo, value);
    }
    
    /// <summary>
    /// Application version string.
    /// </summary>
    public string AppVersion
    {
        get => _appVersion;
        set => SetProperty(ref _appVersion, value);
    }
    
    /// <summary>
    /// Whether the current OS is Linux.
    /// </summary>
    public bool IsLinux
    {
        get => _isLinux;
        set => SetProperty(ref _isLinux, value);
    }
    
    /// <summary>
    /// Whether the current OS is Windows.
    /// </summary>
    public bool IsWindows
    {
        get => _isWindows;
        set => SetProperty(ref _isWindows, value);
    }

    // Properties for active navigation button tracking
    public bool IsOnDiskSelection => _currentViewModelType == typeof(DiskSelectionViewModel);
    public bool IsOnDiskCards => _currentViewModelType == typeof(DiskCardsViewModel);
    public bool IsOnSurfaceTest => _currentViewModelType == typeof(SurfaceTestViewModel);
    public bool IsOnSmartCheck => _currentViewModelType == typeof(SmartCheckViewModel);
    public bool IsOnAnalysis => _currentViewModelType == typeof(AnalysisViewModel);
    public bool IsOnDiskComparison => _currentViewModelType == typeof(DiskComparisonViewModel);
    public bool IsOnReport => _currentViewModelType == typeof(ReportViewModel);
    public bool IsOnHistory => _currentViewModelType == typeof(HistoryViewModel);
    public bool IsOnSettings => _currentViewModelType == typeof(SettingsViewModel);

    [RelayCommand]
    private void NavigateToDiskSelection()
    {
        _navigationService.NavigateTo<DiskSelectionViewModel>();
        StatusMessage = "Naviguji na výběr disků...";
    }

    [RelayCommand]
    private void NavigateToDiskCards()
    {
        _navigationService.NavigateTo<DiskCardsViewModel>();
        StatusMessage = "Naviguji na karty disků...";
    }

    [RelayCommand]
    private void NavigateToSurfaceTest()
    {
        _navigationService.NavigateTo<SurfaceTestViewModel>();
        StatusMessage = "Naviguji na test povrchu...";
    }

    [RelayCommand]
    private void NavigateToSmartCheck()
    {
        _navigationService.NavigateTo<SmartCheckViewModel>();
        StatusMessage = "Naviguji na SMART kontrolu...";
    }

    [RelayCommand]
    private void NavigateToAnalysis()
    {
        _navigationService.NavigateTo<AnalysisViewModel>();
        StatusMessage = "Naviguji na analýzu...";
    }

    [RelayCommand]
    private void NavigateToDiskComparison()
    {
        _navigationService.NavigateTo<DiskComparisonViewModel>();
        StatusMessage = "Naviguji na porovnání disků...";
    }

    [RelayCommand]
    private void NavigateToReport()
    {
        _navigationService.NavigateTo<ReportViewModel>();
        StatusMessage = "Naviguji na report...";
    }

    [RelayCommand]
    private void NavigateToHistory()
    {
        _navigationService.NavigateTo<HistoryViewModel>();
        StatusMessage = "Naviguji na historii...";
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.NavigateTo<SettingsViewModel>();
        StatusMessage = "Naviguji na nastavení...";
    }

    private void OnNavigated(object? sender, NavigationEventArgs e)
    {
        CurrentContent = e.ViewModel;
        _currentViewModelType = e.ViewModel?.GetType();
        
        // Notify all IsOn* properties changed
        OnPropertyChanged(nameof(IsOnDiskSelection));
        OnPropertyChanged(nameof(IsOnDiskCards));
        OnPropertyChanged(nameof(IsOnSurfaceTest));
        OnPropertyChanged(nameof(IsOnSmartCheck));
        OnPropertyChanged(nameof(IsOnAnalysis));
        OnPropertyChanged(nameof(IsOnDiskComparison));
        OnPropertyChanged(nameof(IsOnReport));
        OnPropertyChanged(nameof(IsOnHistory));
        OnPropertyChanged(nameof(IsOnSettings));
    }
}