using System.IO;
using KK.Var.Configuration;

namespace KK.Var.Services
{
    public interface IArtifactPathPolicy
    {
        string GetEffectiveRoot(UserSettings settings);

        string ValidateRoot(string? path);

        string ResolvePath(string root, string relativePath);

        void EnsureExistingPathHasNoReparsePoints(string path);

        void EnsureTreeHasNoReparsePoints(DirectoryInfo directory);

        void EnsureNotReparsePoint(string path);

        FileAttributes? GetExistingAttributes(string path);

        bool IsSameOrChild(string path, string parent);

        bool IsDefaultRoot(string path);
    }
}
