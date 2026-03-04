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

public class HistoryListItem
{
   public Guid TestId { get; set; }
   public DateTime TestDate { get; set; }
   public string DriveName { get; set; } = string.Empty;
   public string TestType { get; set; } = string.Empty;
   public string Grade { get; set; } = string.Empty;
   public double Score { get; set; }
   public int ErrorCount { get; set; }
}
