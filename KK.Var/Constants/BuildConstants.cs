namespace KK.Var.Constants
{
    internal static class BuildConstants
    {
        public const string WindowsExecutableExtension = ".exe";

        public const string WindowsCommandExtension = ".cmd";

        public const string WindowsBatchExtension = ".bat";

        public const string PythonPipRelativePath = "/.venv/bin/pip";

        public const string PythonInterpreterRelativePath = "/.venv/bin/python";

        public const string PythonScriptExtension = ".py";

        public const string SourceDirectoryVariable = "KKVAR_SOURCE_DIR";

        public const string OutputDirectoryVariable = "KKVAR_OUTPUT_DIR";

        public const string RuntimeVariable = "KKVAR_RUNTIME";

        public const string ArchitectureVariable = "KKVAR_ARCHITECTURE";

        public const string NinjaExecutable = "ninja";

        public const string PathVariable = "PATH";

        public const string DotNetExecutable = "dotnet";

        public const string GoExecutable = "go";

        public const string CmakeExecutable = "cmake";

        public const string ReleaseConfiguration = "Release";

        public const string LinuxOperatingSystem = "linux";

        public const string GoOperatingSystemVariable = "GOOS";

        public const string GoArchitectureVariable = "GOARCH";

        public const string CgoEnabledVariable = "CGO_ENABLED";

        public const string GoModuleFileName = "go.mod";

        public const string PythonProjectFileName = "pyproject.toml";

        public const string PythonRequirementsFileName = "requirements.txt";

        public const string CmakeProjectFileName = "CMakeLists.txt";

        public const string PythonVirtualEnvironmentDirectory = ".venv";

        public const string DotNetProjectPattern = "*.csproj";

        public const string DotNetSolutionPattern = "*.sln*";

        public const string PythonSourcePattern = "*.py";

        public const string CppSourcePattern = "*.cpp";

        public const string SourcePlaceholder = "{source}";

        public const string OutputPlaceholder = "{output}";

        public const string ArchitecturePlaceholder = "{architecture}";

        public const string RuntimePlaceholder = "{runtime}";

        public const string DotNetAssemblyExtension = ".dll";

        public const string DotNetProjectExtension = ".csproj";

        public const string DotNetSolutionExtension = ".sln";

        public const string DotNetSolutionXmlExtension = ".slnx";

        public const string TextFileExtension = ".txt";
        public const string DotNetPublishCommand = "publish";

        public const string GoBuildCommand = "build";

        public const string DotNetConfigurationOption = "-c";

        public const string DotNetRuntimeOption = "-r";

        public const string SelfContainedOption = "--self-contained";

        public const string OutputOption = "-o";

        public const string CmakeSourceOption = "-S";

        public const string CmakeBuildDirectoryOption = "-B";

        public const string CmakeGeneratorOption = "-G";

        public const string CmakeBuildOption = "--build";

        public const string CmakeConfigurationOption = "--config";

        public const string CmakeBuildDirectoryName = "cmake-build";

        public const string NinjaGenerator = "Ninja";

        public const string CmakeBuildTypePrefix = "-DCMAKE_BUILD_TYPE=";

        public const string CmakeToolchainPrefix = "-DCMAKE_TOOLCHAIN_FILE=";

        public const string CmakeOutputDirectoryPrefix = "-DCMAKE_RUNTIME_OUTPUT_DIRECTORY=";

        public const string CmakeTargetArchitecturePrefix = "-DKKVAR_TARGET_ARCHITECTURE=";

        public const string DotNetListSdksOption = "--list-sdks";

        public const string DotNetOutputTypeProperty = "OutputType";

        public const string DotNetAssemblyNameProperty = "AssemblyName";

        public const string DotNetConsoleOutputType = "Exe";

        public const string DotNetWindowsOutputType = "WinExe";

        public const string SourceUnixPrefix = "{source}/";

        public const string SourceWindowsPrefix = "{source}\\";
    }
}
