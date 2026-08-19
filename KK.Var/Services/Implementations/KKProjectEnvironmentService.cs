using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class KKProjectEnvironmentService(
    IKKProjectRepository projectRepository,
    IKKProjectEnvironmentVariableRepository variableRepository)
    : IKKProjectEnvironmentService
{
    private static readonly Regex VariableNamePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex JsonNumberPattern = new(
        "^-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$",
        RegexOptions.CultureInvariant);

    public async Task<IReadOnlyDictionary<string, string>> GetAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var variables = await variableRepository.GetByProjectIdAsync(
            projectId,
            cancellationToken);

        return variables.ToDictionary(
            variable => variable.Name,
            variable => variable.Value,
            StringComparer.Ordinal);
    }

    public async Task ReplaceAsync(
        Guid projectId,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(variables);

        _ = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

        var entities = new List<KKProjectEnvironmentVariable>(variables.Count);

        foreach (var pair in variables)
        {
            var name = pair.Key.Trim();

            if (!VariableNamePattern.IsMatch(name))
            {
                throw new ArgumentException(
                    $"Environment variable name '{pair.Key}' is invalid.",
                    nameof(variables));
            }

            entities.Add(new KKProjectEnvironmentVariable
            {
                Id = Guid.NewGuid(),
                KKProjectId = projectId,
                Name = name,
                Value = pair.Value ?? string.Empty,
            });
        }

        await variableRepository.ReplaceAsync(projectId, entities, cancellationToken);
    }

    public async Task<string> GenerateJsonAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var variables = await variableRepository.GetByProjectIdAsync(
            projectId,
            cancellationToken);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true,
               }))
        {
            writer.WriteStartObject();

            foreach (var variable in variables.OrderBy(variable => variable.Name))
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

    public async Task<string> WriteJsonFileAsync(
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

        var project = await projectRepository.GetByIdAsync(projectId, cancellationToken)
            ?? throw new KeyNotFoundException($"Project '{projectId}' was not found.");

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

        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException(
                "Environment file directory could not be determined.");

        Directory.CreateDirectory(targetDirectory);

        var json = await GenerateJsonAsync(projectId, cancellationToken);
        await File.WriteAllTextAsync(
            targetPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        return targetPath;
    }
}
