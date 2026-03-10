using CommunityToolkit.Mvvm.ComponentModel;

namespace DiskChecker.UI.WPF.ViewModels;

/// <summary>
/// Represents the status of a single disk block during surface testing.
/// Observable version for real-time UI updates.
/// </summary>
public partial class BlockStatus : ObservableObject
{
   /// <summary>
   /// Block index in the disk.
   /// </summary>
   [ObservableProperty]
   private int index;

   /// <summary>
   /// Status: 0 = untested, 1 = writing, 2 = write ok, 3 = read ok, 4 = error
   /// </summary>
   [ObservableProperty]
   private int status;

   /// <summary>
   /// Indicates whether this visual block is assigned to tested disk capacity.
   /// </summary>
   [ObservableProperty]
   private bool isAllocated = true;

   /// <summary>
   /// Error message if status is error.
   /// </summary>
   [ObservableProperty]
   private string? errorMessage;
}
