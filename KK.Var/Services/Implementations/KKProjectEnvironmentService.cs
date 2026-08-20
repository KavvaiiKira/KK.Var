using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class KKProjectEnvironmentService(
    IKKProjectRepository projectRepository,
    IKKProjectEnvironmentVariableRepository variableRepository)
    : IKKProjectEnvironmentService
{
    private static readonly Regex VariableNamePattern = new Regex(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex JsonNumberPattern = new Regex(
        "^-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$",
        RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<KeyValuePair<string, string>>> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var variables = await variableRepository.GetByProjectIdAsync(
            projectId,
            cancellationToken);

        return variables
            .Select(variable => new KeyValuePair<string, string>(
                variable.Name,
                variable.Value))
            .ToArray();
    }

    public async Task ReplaceAsync(
        Guid projectId,
        EnvironmentFileFormat format,
        IReadOnlyList<KeyValuePair<string, string>> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variables);

        _ = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
            throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var entities = new List<KKProjectEnvironmentVariable>(variables.Count);
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < variables.Count; index++)
        {
            var pair = variables[index];
            var name = pair.Key.Trim();

            if (!VariableNamePattern.IsMatch(name))
            {
                throw new ArgumentException(
                    $"Environment variable name '{pair.Key}' is invalid.",
                    nameof(variables));
            }

            if (!names.Add(name))
            {
                throw new ArgumentException(
                    $"Environment variable name '{name}' is duplicated.",
                    nameof(variables));
            }

            entities.Add(new KKProjectEnvironmentVariable
            {
                Id = Guid.NewGuid(),
                KKProjectId = projectId,
                Name = name,
                Value = pair.Value ?? string.Empty,
                SortOrder = index,
            });
        }

        await variableRepository.ReplaceAsync(
            projectId,
            format,
            entities,
            cancellationToken);
    }

    public async Task<string> GenerateAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
            throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var variables = await variableRepository.GetByProjectIdAsync(
            projectId,
            cancellationToken);

        return project.EnvironmentFileFormat switch
        {
            EnvironmentFileFormat.Json => GenerateJson(variables),
            EnvironmentFileFormat.DotEnv => GenerateDotEnv(variables),
            EnvironmentFileFormat.Shell => GenerateShell(variables),
            EnvironmentFileFormat.Yaml => GenerateYaml(variables),
            _ => throw new ArgumentOutOfRangeException(
                nameof(project.EnvironmentFileFormat),
                project.EnvironmentFileFormat,
                "Unsupported environment file format."),
        };
    }

    private static string GenerateJson(
        IReadOnlyList<KKProjectEnvironmentVariable> variables)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true,
               }))
        {
            writer.WriteStartObject();

            foreach (var variable in variables)
            {
                writer.WritePropertyName(variable.Name);

                if (JsonNumberPattern.IsMatch(variable.Value))
                {
                    writer.WriteRawValue(variable.Value, skipInputValidation: false);
                }
                else
                {
                    writer.WriteStringValue(variable.Value);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string GenerateDotEnv(
        IReadOnlyList<KKProjectEnvironmentVariable> variables) =>
        string.Join(
            '\n',
            variables.Select(variable =>
                    $"{variable.Name}={FormatJsonLikeScalar(variable.Value)}"));

    private static string GenerateShell(
        IReadOnlyList<KKProjectEnvironmentVariable> variables) =>
        string.Join(
            '\n',
            variables.Select(variable =>
                    $"export {variable.Name}='{variable.Value.Replace("'", "'\"'\"'")}'"));

    private static string GenerateYaml(
        IReadOnlyList<KKProjectEnvironmentVariable> variables) =>
        string.Join(
            '\n',
            variables.Select(variable =>
                    $"{variable.Name}: {FormatJsonLikeScalar(variable.Value)}"));

    private static string FormatJsonLikeScalar(string value) =>
        JsonNumberPattern.IsMatch(value) ?
            value :
        JsonSerializer.Serialize(value);

    public async Task<string> WriteFileAsync(
        Guid projectId,
        string projectRootDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRootDirectory))
        {
            throw new ArgumentException(
                "Project root directory is required.",
                nameof(projectRootDirectory));
        }

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken) ??
            throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var rootPath = Path.GetFullPath(projectRootDirectory);

        var relativePath = project.ProjectEnvironmentFilePath.Replace(
            '/',
            Path.DirectorySeparatorChar);

        var targetPath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var pathFromRoot = Path.GetRelativePath(rootPath, targetPath);

        if (Path.IsPathRooted(pathFromRoot) ||
            pathFromRoot.Equals("..", StringComparison.Ordinal) ||
            pathFromRoot.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Environment file path points outside the project root directory.");
        }

        var targetDirectory = Path.GetDirectoryName(targetPath) ??
            throw new InvalidOperationException("Environment file directory could not be determined.");

        Directory.CreateDirectory(targetDirectory);

        var content = await GenerateAsync(projectId, cancellationToken);

        await File.WriteAllTextAsync(
            targetPath,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return targetPath;
    }
}
