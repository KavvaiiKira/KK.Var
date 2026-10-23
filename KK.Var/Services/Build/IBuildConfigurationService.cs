using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services
{
    public interface IBuildConfigurationService
    {
        ProjectBuildConfiguration Read(string json);

        ProjectBuildConfiguration ReadAndValidate(
            string json,
            ProjectBuildProvider provider,
            string? sourceRoot = null);

        string ResolveSourcePath(string root, string relativePath);

        void Validate(
            ProjectBuildConfiguration configuration,
            ProjectBuildProvider provider,
            string? sourceRoot = null);

        ProjectBuildProvider DetectProvider(string directory);
    }
}
