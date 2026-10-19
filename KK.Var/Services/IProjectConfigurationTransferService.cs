using System;
using System.Threading;
using System.Threading.Tasks;
using KK.Var.Models;

namespace KK.Var.Services;

public interface IProjectConfigurationTransferService
{
    Task<string> ExportAsync(Guid projectId, CancellationToken cancellationToken = default);

    ProjectConfigurationDocument Read(string json);

    string Describe(ProjectConfigurationDocument document);

    Task<KKProject> ImportAsync(ProjectConfigurationDocument document, Guid remoteMachineId, CancellationToken cancellationToken = default);
}
