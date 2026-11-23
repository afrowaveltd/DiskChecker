using CommunityToolkit.Mvvm.ComponentModel;
using DiskChecker.Core.Models;

namespace DiskChecker.UI.Avalonia.ViewModels;

/// <summary>
/// UI-only checked row for a saved certificate. Selection is not persisted and
/// never changes the certificate's identity, test session or assessment.
/// </summary>
public sealed class CertificateSelectionItem : ObservableObject
{
    private bool _isSelected;

    public CertificateSelectionItem(DiskCertificate certificate)
    {
        Certificate = certificate;
    }

    public DiskCertificate Certificate { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
