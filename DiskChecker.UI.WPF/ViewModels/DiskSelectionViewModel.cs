using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Models;
using DiskChecker.UI.WPF.Services;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// ViewModel pro výběr disku.
/// </summary>
public partial class DiskSelectionViewModel : ViewModelBase
{
   private readonly DiskCheckerService _diskCheckerService;
   private readonly INavigationService _navigationService;

   [ObservableProperty]
   private List<CoreDriveInfo> drives = [];

   [ObservableProperty]
   private CoreDriveInfo? selectedDrive;

   /// <summary>
   /// Initializes a new instance of the <see cref="DiskSelectionViewModel"/> class.
   /// </summary>
   public DiskSelectionViewModel(DiskCheckerService diskCheckerService, INavigationService navigationService)
   {
      _diskCheckerService = diskCheckerService;
      _navigationService = navigationService;
   }

   /// <summary>
   /// Initializes the view model by loading available drives.
   /// </summary>
   public override async Task InitializeAsync()
   {
      IsBusy = true;
      StatusMessage = "📂 Načítám seznam disků...";

      try
      {
         IReadOnlyList<CoreDriveInfo> driveList = await _diskCheckerService.ListDrivesAsync();
         Drives = driveList.ToList();
         StatusMessage = $"✅ Nalezeno {Drives.Count} disků";
      }
      catch(Exception ex)
      {
         StatusMessage = $"❌ Chyba: {ex.Message}";
      }
      finally
      {
         IsBusy = false;
      }
   }

   /// <summary>
   /// Navigates to surface test with selected drive.
   /// </summary>
   [RelayCommand]
   public void NavigateToSurfaceTest()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Prosím vyber disk!";
         return;
      }

      StatusMessage = $"🔄 Přecházím na test povrchu: {SelectedDrive.Name}";
      _navigationService.NavigateTo<SurfaceTestViewModel>();

      // Pass selected drive to SurfaceTestViewModel
      // TODO: Pass parameter through navigation
   }

   /// <summary>
   /// Navigates to SMART check with selected drive.
   /// </summary>
   [RelayCommand]
   public void NavigateToSmartCheck()
   {
      if(SelectedDrive == null)
      {
         StatusMessage = "❌ Prosím vyber disk!";
         return;
      }

      StatusMessage = $"🔄 Přecházím na SMART check: {SelectedDrive.Name}";
      _navigationService.NavigateTo<SmartCheckViewModel>();
   }
}
