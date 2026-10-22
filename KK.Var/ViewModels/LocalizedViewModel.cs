using System;
using KK.Var.Services;

namespace KK.Var.ViewModels
{
    public abstract class LocalizedViewModel(ILocalizationService localizationService) : ViewModelBase
    {
        protected readonly ILocalizationService _localizationService = localizationService;

        public event Action<string, bool>? NotificationRequested;

        public virtual void RefreshLocalization() => OnPropertyChanged(string.Empty);

        protected string Localize(string key) => _localizationService.Get(key);

        protected string LocalizeFormat(string key, params object?[] arguments) =>
            _localizationService.Format(key, arguments);

        protected void PublishNotification(string message, bool isError) =>
            NotificationRequested?.Invoke(message, isError);
    }
}
