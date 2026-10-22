using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels
{
    public partial class ProjectEnvironmentEditorViewModel(
        IKKProjectEnvironmentService _projectEnvironmentService,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        private const string JsonEnvironmentFormat = "JSON";

        private const string DotEnvEnvironmentFormat = ".env";

        private const string ShellEnvironmentFormat = "Shell (export)";

        private const string YamlEnvironmentFormat = "YAML";

        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        private CancellationTokenSource? _environmentAutoSaveCancellation;

        private bool _isLoadingEnvironment;

        private long _environmentChangeVersion;

        public ObservableCollection<EnvironmentVariableRowViewModel> EnvironmentVariables { get; } = [];

        public IReadOnlyList<string> EnvironmentFileFormats { get; } =
            [JsonEnvironmentFormat, DotEnvEnvironmentFormat, ShellEnvironmentFormat, YamlEnvironmentFormat];

        [ObservableProperty]
        public partial bool IsEnvironmentSaving { get; private set; }

        [ObservableProperty]
        public partial string SelectedEnvironmentFileFormat { get; set; } = JsonEnvironmentFormat;

        [ObservableProperty]
        public partial bool HasUnsavedEnvironmentChanges { get; private set; }

        [ObservableProperty]
        public partial string EnvironmentSaveStatus { get; private set; } = localizationService.Get("Все изменения сохранены");

        public void AddEnvironmentVariable()
        {
            var variable = new EnvironmentVariableRowViewModel();

            variable.PropertyChanged += EnvironmentVariable_OnPropertyChanged;
            EnvironmentVariables.Add(variable);
            MarkEnvironmentChanged();
        }

        public void RemoveEnvironmentVariable(EnvironmentVariableRowViewModel variable)
        {
            variable.PropertyChanged -= EnvironmentVariable_OnPropertyChanged;
            EnvironmentVariables.Remove(variable);
            MarkEnvironmentChanged();
        }

        public async Task<bool> SaveEnvironmentVariablesAsync()
        {
            _environmentAutoSaveCancellation?.Cancel();
            _environmentAutoSaveCancellation?.Dispose();
            _environmentAutoSaveCancellation = null;

            return await SaveEnvironmentVariablesCoreAsync(
                showNotification: true,
                CancellationToken.None);
        }

        private async Task<bool> SaveEnvironmentVariablesCoreAsync(
            bool showNotification,
            CancellationToken cancellationToken)
        {
            var project = SelectedProject;
            await _saveLock.WaitAsync(cancellationToken);

            try
            {
                if (project is null || !ReferenceEquals(SelectedProject, project))
                {
                    return false;
                }

                IsEnvironmentSaving = true;
                EnvironmentSaveStatus = Localize("Сохранение...");
                var changeVersion = _environmentChangeVersion;

                try
                {
                    if (EnvironmentVariables.Any(item => string.IsNullOrWhiteSpace(item.Name)))
                    {
                        EnvironmentSaveStatus = Localize("Заполните имя каждой переменной");
                        return false;
                    }

                    var variables = EnvironmentVariables
                        .Select(item => new KeyValuePair<string, string>(
                            item.Name.Trim(),
                            item.Value))
                        .ToArray();

                    var format = MapEnvironmentFileFormat(
                        SelectedEnvironmentFileFormat);

                    await _projectEnvironmentService.ReplaceAsync(
                        project.Id,
                        format,
                        variables,
                        cancellationToken);

                    project.EnvironmentFileFormat = format;

                    if (!ReferenceEquals(SelectedProject, project))
                    {
                        return true;
                    }

                    if (changeVersion == _environmentChangeVersion)
                    {
                        HasUnsavedEnvironmentChanges = false;
                        EnvironmentSaveStatus = Localize("Все изменения сохранены");
                    }

                    if (showNotification)
                    {
                        PublishNotification(
                            Localize("Формат и переменные окружения сохранены"),
                            isError: false);
                    }

                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception exception)
                {
                    if (!ReferenceEquals(SelectedProject, project))
                    {
                        return false;
                    }

                    EnvironmentSaveStatus = Localize(exception.Message);

                    if (showNotification)
                    {
                        PublishNotification(Localize(exception.Message), isError: true);
                    }

                    return false;
                }
                finally
                {
                    IsEnvironmentSaving = false;
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private void EnvironmentVariable_OnPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e) =>
            MarkEnvironmentChanged();

        private void MarkEnvironmentChanged()
        {
            if (_isLoadingEnvironment)
            {
                return;
            }

            HasUnsavedEnvironmentChanges = true;
            InvalidationRequested?.Invoke(SelectedProject?.Id);
            _environmentChangeVersion++;
            EnvironmentSaveStatus = Localize("Есть несохранённые изменения");
            _environmentAutoSaveCancellation?.Cancel();
            _environmentAutoSaveCancellation?.Dispose();
            _environmentAutoSaveCancellation = new CancellationTokenSource();
            _ = AutoSaveEnvironmentAsync(_environmentAutoSaveCancellation.Token);
        }

        private async Task AutoSaveEnvironmentAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(800, cancellationToken);

                if (EnvironmentVariables.Any(item => string.IsNullOrWhiteSpace(item.Name)))
                {
                    EnvironmentSaveStatus = Localize("Заполните имя каждой переменной");
                    return;
                }

                await SaveEnvironmentVariablesCoreAsync(
                    showNotification: false,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        public async Task DiscardEnvironmentChangesAsync()
        {
            var project = SelectedProject;
            _environmentAutoSaveCancellation?.Cancel();
            await _saveLock.WaitAsync();

            try
            {
                if (project is not null && ReferenceEquals(SelectedProject, project) && ReloadRequested is { } reload)
                {
                    await reload(project);
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        partial void OnSelectedEnvironmentFileFormatChanged(string value) =>
            MarkEnvironmentChanged();

        private static string MapEnvironmentFileFormat(EnvironmentFileFormat format) =>
            format switch
            {
                EnvironmentFileFormat.DotEnv => DotEnvEnvironmentFormat,
                EnvironmentFileFormat.Json => JsonEnvironmentFormat,
                EnvironmentFileFormat.Shell => ShellEnvironmentFormat,
                EnvironmentFileFormat.Yaml => YamlEnvironmentFormat,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
            };

        private static EnvironmentFileFormat MapEnvironmentFileFormat(string format) =>
            format switch
            {
                DotEnvEnvironmentFormat => EnvironmentFileFormat.DotEnv,
                JsonEnvironmentFormat => EnvironmentFileFormat.Json,
                ShellEnvironmentFormat => EnvironmentFileFormat.Shell,
                YamlEnvironmentFormat => EnvironmentFileFormat.Yaml,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
            };

        private KKProject? SelectedProject { get; set; }

        public void SelectProject(KKProject? project)
        {
            _environmentAutoSaveCancellation?.Cancel();
            _environmentChangeVersion++;
            SelectedProject = project;
        }

        public override void RefreshLocalization()
        {
            EnvironmentSaveStatus = HasUnsavedEnvironmentChanges ?
                Localize("Есть несохранённые изменения") :
                Localize("Все изменения сохранены");
            base.RefreshLocalization();
        }

        public event Action<Guid?>? InvalidationRequested;

        public event Func<KKProject, Task>? ReloadRequested;

        public Task<IReadOnlyList<KeyValuePair<string, string>>> ReadAsync(Guid projectId) =>
            _projectEnvironmentService.GetAsync(projectId);

        public void Load(KKProject project, IReadOnlyList<KeyValuePair<string, string>> variables)
        {
            if (!ReferenceEquals(SelectedProject, project))
            {
                return;
            }

            _isLoadingEnvironment = true;
            _environmentAutoSaveCancellation?.Cancel();
            _environmentAutoSaveCancellation?.Dispose();
            _environmentAutoSaveCancellation = null;
            SelectedEnvironmentFileFormat = MapEnvironmentFileFormat(
                project.EnvironmentFileFormat);

            foreach (var variable in EnvironmentVariables)
            {
                variable.PropertyChanged -= EnvironmentVariable_OnPropertyChanged;
            }

            EnvironmentVariables.Clear();

            foreach (var variable in variables)
            {
                var row = new EnvironmentVariableRowViewModel
                {
                    Name = variable.Key,
                    Value = variable.Value,
                };

                row.PropertyChanged += EnvironmentVariable_OnPropertyChanged;
                EnvironmentVariables.Add(row);
            }

            HasUnsavedEnvironmentChanges = false;
            _environmentChangeVersion++;
            EnvironmentSaveStatus = Localize("Все изменения сохранены");
            _isLoadingEnvironment = false;
        }
    }
}
