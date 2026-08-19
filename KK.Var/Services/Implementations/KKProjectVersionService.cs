using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;
using KK.Var.Repositories;

namespace KK.Var.Services.Implementations;

public sealed class KKProjectVersionService(
    IKKProjectRepository projectRepository,
    IKKProjectVersionRepository versionRepository) : IKKProjectVersionService
{
    private static readonly Regex Sha256Pattern = new(
        "^[0-9a-fA-F]{64}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex CommitShaPattern = new(
        "^[0-9a-fA-F]{40,64}$",
        RegexOptions.CultureInvariant);

    public Task<IReadOnlyList<KKProjectVersion>> GetByProjectIdAsync(
        Guid projectId,
        CancellationToken cancellationToken = default) =>
        versionRepository.GetByProjectIdAsync(projectId, cancellationToken);

    public async Task<KKProjectVersion> CreateAsync(
        KKProjectVersion version,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        _ = await projectRepository.GetByIdAsync(version.KKProjectId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Project '{version.KKProjectId}' was not found.");

        version.Tag = Required(version.Tag, nameof(version.Tag), 200);
        version.ArtifactRelativePath = Required(
            version.ArtifactRelativePath,
            nameof(version.ArtifactRelativePath),
            1024);
        version.ArtifactSha256 = Required(
            version.ArtifactSha256,
            nameof(version.ArtifactSha256),
            64).ToLowerInvariant();
        version.SourceCommitSha = string.IsNullOrWhiteSpace(version.SourceCommitSha)
            ? null
            : version.SourceCommitSha.Trim().ToLowerInvariant();
        version.Description = string.IsNullOrWhiteSpace(version.Description)
            ? null
            : version.Description.Trim();

        ValidateRelativePath(version.ArtifactRelativePath);

        if (!Sha256Pattern.IsMatch(version.ArtifactSha256))
        {
            throw new ArgumentException("Artifact SHA-256 is invalid.");
        }

        if (version.SourceCommitSha is not null &&
            !CommitShaPattern.IsMatch(version.SourceCommitSha))
        {
            throw new ArgumentException("Source commit SHA is invalid.");
        }

        if (version.ArtifactSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version.ArtifactSize),
                "Artifact size cannot be negative.");
        }

        if (await versionRepository.TagExistsAsync(
                version.KKProjectId,
                version.Tag,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Version tag '{version.Tag}' already exists for this project.");
        }

        version.Id = version.Id == Guid.Empty ? Guid.NewGuid() : version.Id;
        version.CreatedAtUtc = DateTimeOffset.UtcNow;

        await versionRepository.AddAsync(version, cancellationToken);
        return version;
    }

    private static string Required(string? value, string parameterName, int maxLength)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrEmpty(normalized))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException(
                $"Value cannot exceed {maxLength} characters.",
                parameterName);
        }

        return normalized;
    }

    private static void ValidateRelativePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            throw new ArgumentException("Artifact path must be relative.");
        }

        foreach (var segment in path.Replace('\\', '/').Split('/'))
        {
            if (segment is "." or "..")
            {
                throw new ArgumentException(
                    "Artifact path cannot contain '.' or '..' segments.");
            }
        }
    }
}
