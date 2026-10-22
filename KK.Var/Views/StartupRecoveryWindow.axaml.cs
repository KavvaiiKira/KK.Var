using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KK.Var.Enums;
using KK.Var.Services;
using KK.Var.Views.Design;

namespace KK.Var.Views
{
    public partial class StartupRecoveryWindow : Window
    {
        private const string RecoveryPreviewMessage = "Startup recovery preview.";

        private readonly StartupRecoveryKind _kind;

        private readonly IStartupRecoveryService _recoveryService;

        private readonly ILocalizationService _localizationService;

        public StartupRecoveryWindow()
            : this(
                StartupRecoveryKind.UserSettings,
                new InvalidOperationException(RecoveryPreviewMessage),
                new DesignStartupRecoveryService(),
                DesignServices.Localization)
        {
        }

        public StartupRecoveryWindow(
            StartupRecoveryKind kind,
            Exception exception,
            IStartupRecoveryService recoveryService,
            ILocalizationService localizationService)
        {
            _kind = kind;
            _recoveryService = recoveryService;
            _localizationService = localizationService;
            InitializeComponent();

            var localization = _localizationService;

            TitleText.Text = kind == StartupRecoveryKind.UserSettings ?
                localization.Get("Не удалось прочитать настройки") :
                localization.Get("Не удалось открыть базу данных");

            DescriptionText.Text = kind == StartupRecoveryKind.UserSettings ?
                localization.Get("Настройки повреждены или недоступны. Можно сохранить исходный файл в резервной копии и начать с чистых настроек.") :
                localization.Get("SQLite-база повреждена или её схема не может быть обновлена. Можно сохранить все файлы базы в резервной копии и создать новую базу.");

            ErrorText.Text = localization.Get(exception.Message);
            OpenDataDirectoryButton.Content = localization.Get("Открыть папку данных");
            CloseButton.Content = localization.Get("Завершить");
            RecoverButton.Content = localization.Get("Создать копию и сбросить");
        }

        public event EventHandler? RecoverySucceeded;

        private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void OpenDataDirectoryButton_OnClick(object? sender, RoutedEventArgs e) =>
            _recoveryService.OpenUserDataDirectory();

        private void CloseButton_OnClick(object? sender, RoutedEventArgs e) =>
            Close();

        private async void RecoverButton_OnClick(object? sender, RoutedEventArgs e)
        {
            RecoverButton.IsEnabled = false;
            var localization = _localizationService;
            StatusText.Text = localization.Get("Создание резервной копии...");

            try
            {
                var backupDirectory = await _recoveryService.BackupAndResetAsync(_kind);
                StatusText.Text = localization.Format("Резервная копия: {0}", backupDirectory);
                RecoverySucceeded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                ErrorText.Text = localization.Get(exception.Message);
                StatusText.Text = localization.Get("Восстановление не выполнено.");
                RecoverButton.IsEnabled = true;
            }
        }
    }
}
