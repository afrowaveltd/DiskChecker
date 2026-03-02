using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Základní třída pro všechny ViewModely.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>
    /// Inicializuje ViewModel s možností asynchronní inicializace.
    /// </summary>
    public virtual Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Čistí prostředky když je ViewModel zničen.
    /// </summary>
    public virtual void Cleanup()
    {
    }
}
