using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels
{
    public partial class ProjectBuildEditorViewModel(
        IBuildConfigurationService buildConfigurationService,
        ILocalizationService localizationService) : LocalizedViewModel(localizationService)
    {
        private bool _isLoadingBuild;

        private bool _buildLoadFailed;

        private ProjectBuildConfiguration _buildConfiguration = new ProjectBuildConfiguration();

        private string _buildEditorError = string.Empty;

        private static readonly JsonSerializerOptions BuildJsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        [ObservableProperty]
        public partial string BuildWorkingDirectory { get; set; } = ".";

        [ObservableProperty]
        public partial string BuildConfiguration { get; set; } = BuildConstants.ReleaseConfiguration;

        [ObservableProperty]
        public partial string DotNetProjectPath { get; set; } = string.Empty;

        [ObservableProperty]
        public partial bool CgoEnabled { get; set; } = false;

        [ObservableProperty]
        public partial string PythonDependencyFilePath { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CmakeGenerator { get; set; } = BuildConstants.NinjaGenerator;

        [ObservableProperty]
        public partial string ToolchainFile { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string CustomBuildCommand { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string BuildArgumentsText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string ConfigureArgumentsText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string BuildEnvironmentText { get; set; } = string.Empty;

        public bool IsDotNetBuild => Provider == ProjectBuildProvider.DotNet;

        public bool IsGoBuild => Provider == ProjectBuildProvider.Go;

        public bool IsPythonBuild => Provider == ProjectBuildProvider.Python;

        public bool IsCppBuild => Provider == ProjectBuildProvider.Cpp;

        public bool IsCustomBuild => Provider == ProjectBuildProvider.Custom;

        public bool IsAutomaticBuild => Provider == ProjectBuildProvider.Unknown;

        public bool HasBuildConfiguration => IsDotNetBuild || IsCppBuild;

        public void NotifyBuildFields()
        {
            OnPropertyChanged(nameof(IsDotNetBuild));
            OnPropertyChanged(nameof(IsGoBuild));
            OnPropertyChanged(nameof(IsPythonBuild));
            OnPropertyChanged(nameof(IsCppBuild));
            OnPropertyChanged(nameof(IsCustomBuild));
            OnPropertyChanged(nameof(IsAutomaticBuild));
            OnPropertyChanged(nameof(HasBuildConfiguration));
        }

        public void LoadBuildFields()
        {
            if (_isLoadingBuild)
            {
                return;
            }

            _isLoadingBuild = true;
            try
            {
                _buildConfiguration = buildConfigurationService.Read(BuildConfigurationJson);
                BuildWorkingDirectory = _buildConfiguration.WorkingDirectory ?? ".";
                BuildConfiguration = _buildConfiguration.Configuration ?? BuildConstants.ReleaseConfiguration;
                DotNetProjectPath = _buildConfiguration.DotNetProjectPath ?? string.Empty;
                CgoEnabled = _buildConfiguration.CgoEnabled ??
                    _buildConfiguration.Environment?.GetValueOrDefault(BuildConstants.CgoEnabledVariable) == "1";
                PythonDependencyFilePath = _buildConfiguration.PythonDependencyFilePath ?? string.Empty;
                CmakeGenerator = _buildConfiguration.CmakeGenerator ?? BuildConstants.NinjaGenerator;
                ToolchainFile = _buildConfiguration.ToolchainFile ?? string.Empty;
                CustomBuildCommand = _buildConfiguration.Command ?? string.Empty;
                BuildArgumentsText = string.Join(
                    Environment.NewLine,
                    (_buildConfiguration.BuildArguments ?? []).Select(EncodeBuildValue));
                ConfigureArgumentsText = string.Join(
                    Environment.NewLine,
                    (_buildConfiguration.ConfigureArguments ?? []).Select(EncodeBuildValue));
                BuildEnvironmentText = string.Join(
                    Environment.NewLine,
                    (_buildConfiguration.Environment ?? []).Select(pair => pair.Value is null ?
                        pair.Key :
                        pair.Key + "=" + EncodeBuildValue(pair.Value)));
                _buildEditorError = string.Empty;
                _buildLoadFailed = false;
            }
            catch (JsonException)
            {
                _buildEditorError = Localize("Параметры сборки содержат некорректный JSON.");
                _buildLoadFailed = true;
            }
            finally
            {
                _isLoadingBuild = false;
                NotifyBuildFields();
            }
        }

        public void UpdateBuildConfiguration()
        {
            if (_isLoadingBuild || IsResetting || _buildLoadFailed)
            {
                return;
            }

            Changed?.Invoke(this, EventArgs.Empty);
            try
            {
                var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
                foreach (var line in BuildEnvironmentText.Replace("\r", string.Empty).Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }
                    var separator = line.IndexOf('=');
                    var name = (separator < 0 ? line : line[..separator]).Trim();
                    if (name.Length == 0 || name.Any(char.IsControl) ||
                        !environment.TryAdd(
                            name,
                            separator < 0 ? null : DecodeBuildValue(line[(separator + 1)..])))
                    {
                        throw new InvalidDataException(Localize("Проверьте имена переменных сборки: пустые и повторяющиеся имена недопустимы."));
                    }
                }

                _buildConfiguration.WorkingDirectory = BuildWorkingDirectory.Trim();
                _buildConfiguration.Configuration = BuildConfiguration.Trim();
                _buildConfiguration.DotNetProjectPath = DotNetProjectPath.Trim();
                _buildConfiguration.CgoEnabled = CgoEnabled;
                _buildConfiguration.PythonDependencyFilePath = PythonDependencyFilePath.Trim();
                _buildConfiguration.CmakeGenerator = CmakeGenerator.Trim();
                _buildConfiguration.ToolchainFile = ToolchainFile.Trim();
                _buildConfiguration.Command = CustomBuildCommand.Trim();
                _buildConfiguration.BuildArguments = ParseBuildArguments(BuildArgumentsText);
                _buildConfiguration.ConfigureArguments = ParseBuildArguments(ConfigureArgumentsText);
                _buildConfiguration.Environment = environment;
                _isLoadingBuild = true;
                BuildConfigurationJson = JsonSerializer.Serialize(_buildConfiguration, BuildJsonOptions);
                _buildEditorError = string.Empty;
            }
            catch (InvalidDataException exception)
            {
                _buildEditorError = Localize(exception.Message);
            }
            finally
            {
                _isLoadingBuild = false;
            }
        }

        private static List<string> ParseBuildArguments(string text) =>
            text.Length == 0 ? [] : text.Replace("\r", string.Empty).Split('\n')
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(DecodeBuildValue).ToList();

        private static string EncodeBuildValue(string value) =>
            value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");

        private static string DecodeBuildValue(string value)
        {
            var result = new System.Text.StringBuilder();
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] == '\\' && index + 1 < value.Length &&
                    value[index + 1] is 'n' or 'r' or '\\')
                {
                    result.Append(value[++index] switch { 'n' => '\n', 'r' => '\r', _ => '\\' });
                }
                else
                {
                    result.Append(value[index]);
                }
            }
            return result.ToString();
        }

        partial void OnBuildWorkingDirectoryChanged(string value) => UpdateBuildConfiguration();

        partial void OnBuildConfigurationChanged(string value) => UpdateBuildConfiguration();

        partial void OnDotNetProjectPathChanged(string value) => UpdateBuildConfiguration();

        partial void OnCgoEnabledChanged(bool value) => UpdateBuildConfiguration();

        partial void OnPythonDependencyFilePathChanged(string value) => UpdateBuildConfiguration();

        partial void OnCmakeGeneratorChanged(string value) => UpdateBuildConfiguration();

        partial void OnToolchainFileChanged(string value) => UpdateBuildConfiguration();

        partial void OnCustomBuildCommandChanged(string value) => UpdateBuildConfiguration();

        partial void OnBuildArgumentsTextChanged(string value) => UpdateBuildConfiguration();

        partial void OnConfigureArgumentsTextChanged(string value) => UpdateBuildConfiguration();

        partial void OnBuildEnvironmentTextChanged(string value) => UpdateBuildConfiguration();

        public event EventHandler? Changed;

        public bool IsResetting { get; set; }

        public string Error => _buildEditorError;

        public bool LoadFailed => _buildLoadFailed;

        [ObservableProperty]
        public partial string BuildConfigurationJson { get; set; } = "{}";

        [ObservableProperty]
        public partial ProjectBuildProvider Provider { get; set; }

        partial void OnBuildConfigurationJsonChanged(string value) => LoadBuildFields();

        partial void OnProviderChanged(ProjectBuildProvider value) => NotifyBuildFields();
    }
}
