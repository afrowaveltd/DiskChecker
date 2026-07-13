using CommunityToolkit.Mvvm.ComponentModel;
using DiskChecker.UI.Avalonia.Services;

namespace DiskChecker.UI.Avalonia.ViewModels
{
    public class ViewModelBase : ObservableObject
    {
        private LocaleService? _localeService;
        
        public ViewModelBase()
        {
            // Subscribe to locale changes to refresh XAML bindings
            var ls = App.GetService<LocaleService>();
            if (ls != null)
            {
                _localeService = ls;
                ls.LocaleChanged += OnLocaleChanged;
            }
        }
        
        protected virtual void OnLocaleChanged()
        {
            // Notify that L property changed so all {Binding L[...]} refresh
            OnPropertyChanged(nameof(L));
        }
        
        /// <summary>
        /// Localization service for XAML bindings. Usage: {Binding L[KeyName]}
        /// </summary>
        public LocaleService L
        {
            get
            {
                if (_localeService == null)
                {
                    _localeService = App.GetService<LocaleService>() ?? new LocaleService();
                }
                return _localeService;
            }
        }
    }
}
