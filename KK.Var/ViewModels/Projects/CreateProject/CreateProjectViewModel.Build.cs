using System;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using KK.Var.Constants;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    public partial class CreateProjectViewModel
    {
        public string BuildWorkingDirectory
        {
            get => BuildEditor.BuildWorkingDirectory;
            set => BuildEditor.BuildWorkingDirectory = value;
        }

        public string BuildConfiguration
        {
            get => BuildEditor.BuildConfiguration;
            set => BuildEditor.BuildConfiguration = value;
        }

        public string DotNetProjectPath
        {
            get => BuildEditor.DotNetProjectPath;
            set => BuildEditor.DotNetProjectPath = value;
        }

        public bool CgoEnabled
        {
            get => BuildEditor.CgoEnabled;
            set => BuildEditor.CgoEnabled = value;
        }

        public string PythonDependencyFilePath
        {
            get => BuildEditor.PythonDependencyFilePath;
            set => BuildEditor.PythonDependencyFilePath = value;
        }

        public string CmakeGenerator
        {
            get => BuildEditor.CmakeGenerator;
            set => BuildEditor.CmakeGenerator = value;
        }

        public string ToolchainFile
        {
            get => BuildEditor.ToolchainFile;
            set => BuildEditor.ToolchainFile = value;
        }

        public string CustomBuildCommand
        {
            get => BuildEditor.CustomBuildCommand;
            set => BuildEditor.CustomBuildCommand = value;
        }

        public string BuildArgumentsText
        {
            get => BuildEditor.BuildArgumentsText;
            set => BuildEditor.BuildArgumentsText = value;
        }

        public string ConfigureArgumentsText
        {
            get => BuildEditor.ConfigureArgumentsText;
            set => BuildEditor.ConfigureArgumentsText = value;
        }

        public string BuildEnvironmentText
        {
            get => BuildEditor.BuildEnvironmentText;
            set => BuildEditor.BuildEnvironmentText = value;
        }

        public bool IsDotNetBuild => BuildEditor.IsDotNetBuild;

        public bool IsGoBuild => BuildEditor.IsGoBuild;

        public bool IsPythonBuild => BuildEditor.IsPythonBuild;

        public bool IsCppBuild => BuildEditor.IsCppBuild;

        public bool IsCustomBuild => BuildEditor.IsCustomBuild;

        public bool IsAutomaticBuild => BuildEditor.IsAutomaticBuild;

        public bool HasBuildConfiguration => BuildEditor.HasBuildConfiguration;

        public ProjectBuildEditorViewModel BuildEditor { get; }

        private void NotifyBuildFields()
        {
            BuildEditor.Provider = SelectedBuildProvider;
            BuildEditor.NotifyBuildFields();
        }

        private void LoadBuildFields() => BuildEditor.LoadBuildFields();

        private void UpdateBuildConfiguration() => BuildEditor.UpdateBuildConfiguration();

        [RelayCommand]
        private void DetectBuildSettings()
        {
            try
            {
                if (!IsLocalSource || !Directory.Exists(LocalDirectoryPath))
                {
                    ErrorMessage = Texts.SelectAccessibleLocalDirectoryDetectionGitHubSelect;
                    return;
                }

                var root = _buildConfigurationService.ResolveSourcePath(LocalDirectoryPath, BuildWorkingDirectory);
                var provider = _buildConfigurationService.DetectProvider(root);
                if (BuildEditor.LoadFailed)
                {
                    BuildConfigurationJson = "{}";
                    LoadBuildFields();
                }
                SelectedBuildProvider = provider;
                if (IsDotNetBuild && string.IsNullOrWhiteSpace(DotNetProjectPath))
                {
                    var projects = Directory.EnumerateFiles(root, BuildConstants.DotNetProjectPattern, SearchOption.TopDirectoryOnly).ToArray();
                    if (projects.Length == 1)
                    {
                        DotNetProjectPath = Path.GetFileName(projects[0]);
                    }
                }
                if (IsPythonBuild)
                {
                    if (string.IsNullOrWhiteSpace(RemoteExecutableFileName))
                    {
                        var scripts = Directory.EnumerateFiles(root, BuildConstants.PythonSourcePattern).ToArray();
                        if (scripts.Length == 1)
                        {
                            RemoteExecutableFileName = Path.GetFileName(scripts[0]);
                        }
                    }
                    if (string.IsNullOrWhiteSpace(PythonDependencyFilePath))
                    {
                        PythonDependencyFilePath = File.Exists(Path.Combine(root, BuildConstants.PythonRequirementsFileName)) ?
                            BuildConstants.PythonRequirementsFileName : File.Exists(Path.Combine(root, BuildConstants.PythonProjectFileName)) ?
                                BuildConstants.PythonProjectFileName :
                                string.Empty;
                    }
                }
                UpdateBuildConfiguration();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ErrorMessage = Localize(exception.Message);
            }
        }
    }
}
