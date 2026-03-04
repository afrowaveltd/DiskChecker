using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskChecker.Application.Services;
using DiskChecker.Core.Interfaces;
using DiskChecker.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace DiskChecker.UI.WPF.ViewModels;
public partial class HistoryViewModel : ViewModelBase
{
   private readonly HistoryService _historyService;

   [ObservableProperty]
   private ObservableCollection<HistoryListItem> historyItems = [];

   [ObservableProperty]
   private HistoryListItem? selectedItem;

   [ObservableProperty]
   private int totalItems;

   /// <summary>
   /// Initializes a new instance of the <see cref="HistoryViewModel"/> class.
   /// </summary>
   public HistoryViewModel(HistoryService historyService)
   {
      _historyService = historyService;
      StatusMessage = "Historie testů připravena.";
   }

   /// <summary>
   /// Načte historii testů.
   /// </summary>
   [RelayCommand]
   public async Task RefreshHistoryAsync()
   {
      IsBusy = true;
      StatusMessage = "📚 Načítám historii testů...";

      var page = await _historyService.GetHistoryAsync(pageSize: 100, pageIndex: 0);

      HistoryItems = new ObservableCollection<HistoryListItem>(page.Items.Select(i => new HistoryListItem
      {
         TestId = i.TestId,
         TestDate = i.TestDate,
         DriveName = i.DriveName,
         TestType = i.TestType,
         Grade = i.Grade.ToString(),
         Score = i.Score,
         ErrorCount = i.ErrorCount
      }));

      TotalItems = page.TotalItems;
      StatusMessage = page.TotalItems == 0
          ? "Historie je zatím prázdná."
          : $"✅ Načteno {HistoryItems.Count} položek historie.";
      IsBusy = false;
   }

   /// <summary>
   /// Initializes the view model asynchronously.
   /// </summary>
   public override async Task InitializeAsync()
   {
      await RefreshHistoryAsync();
   }
}

/// <summary>
/// ViewModel pro nastavení aplikace.
/// </summary>
