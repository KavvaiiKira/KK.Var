using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KK.Var.Enums;
using KK.Var.Services;
using KK.Var.Views.Design;
using Texts = KK.Var.Resources.Strings;

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
                Texts.CouldNotReadSettings :
                Texts.CouldNotOpenDatabase;

            DescriptionText.Text = kind == StartupRecoveryKind.UserSettings ?
                Texts.SettingsDamagedUnavailableCanBackUpOriginal :
                Texts.SQLiteDatabaseDamagedSchemaCannotUpdatedCan;

            ErrorText.Text = localization.Get(exception.Message);
            OpenDataDirectoryButton.Content = Texts.OpenDataFolder;
            CloseButton.Content = Texts.Exit;
            RecoverButton.Content = Texts.BackUpReset;
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
            StatusText.Text = Texts.CreatingBackup;

            try
            {
                var backupDirectory = await _recoveryService.BackupAndResetAsync(_kind);
                StatusText.Text = localization.Format(Texts.Backup, backupDirectory);
                RecoverySucceeded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                ErrorText.Text = localization.Get(exception.Message);
                StatusText.Text = Texts.RecoveryNotCompleted;
                RecoverButton.IsEnabled = true;
            }
        }
    }
}
