using DiskChecker.Core.Models;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Partial class pro inicializaci a načítání disků v SurfaceTestViewModel.
/// </summary>
public partial class SurfaceTestViewModel
{
   /// <summary>
   /// Inicializuje ViewModel - načte dostupné disky.
   /// </summary>
   public override async Task InitializeAsync()
   {
      await ReloadAvailableDrivesAsync();
      
      // Pokud je disk vybraný, načti pro něj základní info
      if(SelectedDrive != null)
      {
         StatusMessage = $"Připraveno k testu: {SelectedDrive.Name}";
      }
      else
      {
         StatusMessage = "Vyber disk pro test povrchu.";
      }
   }

   /// <summary>
   /// Načte seznam dostupných disků.
   /// </summary>
   [RelayCommand]
   public async Task ReloadAvailableDrivesAsync()
   {
      IsBusy = true;
      StatusMessage = "Načítám seznam disků...";

      try
      {
         var drives = await _diskCheckerService.ListDrivesAsync();
         AvailableDrives = new ObservableCollection<CoreDriveInfo>(drives);

         // Pokud není vybraný disk nebo už neexistuje, vyber první
         if(SelectedDrive == null || !AvailableDrives.Any(d => d.Path == SelectedDrive.Path))
         {
            SelectedDrive = AvailableDrives.FirstOrDefault();
         }

         StatusMessage = $"✅ Načteno {AvailableDrives.Count} disků.";
      }
      catch(Exception ex)
      {
         StatusMessage = $"❌ Chyba při načítání disků: {ex.Message}";
      }
      finally
      {
         IsBusy = false;
      }
   }
}
