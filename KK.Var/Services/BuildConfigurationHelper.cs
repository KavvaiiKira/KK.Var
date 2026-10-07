using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services;

public static class BuildConfigurationHelper
{
    public static string ResolveSourcePath(string root, string relativePath)
    {
        var value = relativePath.Replace("{source}/", string.Empty, StringComparison.Ordinal)
            .Replace("{source}\\", string.Empty, StringComparison.Ordinal);
        if (value == "{source}")
        {
            value = ".";
        }

        if (Path.IsPathRooted(value) || value.Contains(':') || value.Any(char.IsControl))
        {
            throw new InvalidDataException("Пути сборки должны оставаться внутри исходного каталога.");
        }

        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(rootPath, value.Replace('/', Path.DirectorySeparatorChar)));
        if (path != rootPath && !path.StartsWith(rootPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Пути сборки должны оставаться внутри исходного каталога.");
        }

        var current = path;
        while (current.Length >= rootPath.Length)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Пути сборки должны оставаться внутри исходного каталога.");
            }

            if (string.Equals(current, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.GetDirectoryName(current) ?? rootPath;
        }

        return path;
    }

    public static void Validate(
        ProjectBuildConfiguration configuration,
        ProjectBuildProvider provider,
        string? sourceRoot = null)
    {
        var root = sourceRoot ?? Path.Combine(Path.GetTempPath(), "KK.Var", "validation-source");
        var working = ResolveSourcePath(root, configuration.WorkingDirectory ?? ".");
        var files = provider switch
        {
            ProjectBuildProvider.DotNet => new[] { configuration.DotNetProjectPath },
            ProjectBuildProvider.Cpp => new[] { configuration.ToolchainFile },
            ProjectBuildProvider.Python => new[] { configuration.PythonDependencyFilePath },
            _ => Array.Empty<string?>(),
        };
        foreach (var file in files)
        {
            if (!string.IsNullOrWhiteSpace(file))
            {
                var path = ResolveSourcePath(working, file.Replace("{architecture}", "x86_64")
                    .Replace("{runtime}", "linux-x64"));
                if (sourceRoot is not null && !file.Contains('{') && !File.Exists(path))
                {
                    throw new InvalidDataException("Указанный файл сборки не найден.");
                }
            }
        }

        if (provider == ProjectBuildProvider.DotNet &&
            !string.IsNullOrWhiteSpace(configuration.DotNetProjectPath) &&
            !new[] { ".csproj", ".sln", ".slnx" }.Contains(
                Path.GetExtension(configuration.DotNetProjectPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Выберите файл .csproj, .sln или .slnx.");
        }

        if (provider == ProjectBuildProvider.Python &&
            !string.IsNullOrWhiteSpace(configuration.PythonDependencyFilePath) &&
            !configuration.PythonDependencyFilePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFileName(configuration.PythonDependencyFilePath).Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Укажите requirements-файл .txt или pyproject.toml.");
        }

        if (sourceRoot is not null && !Directory.Exists(working))
        {
            throw new InvalidDataException("Рабочий каталог сборки не найден.");
        }

        if (provider == ProjectBuildProvider.Custom && string.IsNullOrWhiteSpace(configuration.Command))
        {
            throw new InvalidDataException("Укажите команду пользовательской сборки.");
        }

        if (provider == ProjectBuildProvider.Cpp && string.IsNullOrWhiteSpace(configuration.ToolchainFile))
        {
            throw new InvalidDataException("Укажите toolchainFile для сборки C++ под Linux.");
        }

        if ((configuration.BuildArguments ?? []).Concat(configuration.ConfigureArguments ?? [])
            .Any(argument => string.IsNullOrWhiteSpace(argument) || argument.Contains('\0')) ||
            (configuration.Environment ?? []).Any(pair => string.IsNullOrWhiteSpace(pair.Key) ||
                pair.Key.Contains('=') || pair.Key.Any(char.IsControl) || pair.Value?.Contains('\0') == true))
        {
            throw new InvalidDataException("Некорректные аргументы или переменные процесса сборки.");
        }

        if (provider == ProjectBuildProvider.Python &&
            configuration.PythonDependencyFilePath?.Contains('{') == true)
        {
            throw new InvalidDataException("Для Python укажите относительный путь без подстановок.");
        }
    }

    public static ProjectBuildProvider DetectProvider(string directory)
    {
        var providers = new List<ProjectBuildProvider>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = true,
        };
        if (Directory.EnumerateFiles(directory, "*.csproj", options).Any() ||
            Directory.EnumerateFiles(directory, "*.sln*", SearchOption.TopDirectoryOnly).Any())
        {
            providers.Add(ProjectBuildProvider.DotNet);
        }
        if (File.Exists(Path.Combine(directory, "go.mod")))
        {
            providers.Add(ProjectBuildProvider.Go);
        }
        if (File.Exists(Path.Combine(directory, "pyproject.toml")) ||
            File.Exists(Path.Combine(directory, "requirements.txt")) ||
            Directory.EnumerateFiles(directory, "*.py").Any())
        {
            providers.Add(ProjectBuildProvider.Python);
        }
        if (File.Exists(Path.Combine(directory, "CMakeLists.txt")) ||
            Directory.EnumerateFiles(directory, "*.cpp", options).Any())
        {
            providers.Add(ProjectBuildProvider.Cpp);
        }
        return providers.Count switch
        {
            1 => providers[0],
            0 => throw new InvalidDataException("Не удалось автоматически определить способ сборки проекта."),
            _ => throw new InvalidDataException("Найдено несколько способов сборки. Выберите нужный в настройках проекта."),
        };
    }
}
