using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KK.Var.Configuration;
using KK.Var.Data;

namespace KK.Var.Services.Implementations
{
    public sealed class ArtifactPathPolicy(ILocalizationService localizationService) : IArtifactPathPolicy
    {
        public string GetEffectiveRoot(UserSettings settings) =>
            string.IsNullOrWhiteSpace(settings.ArtifactsDirectoryPath) ?
                Path.GetFullPath(DatabasePaths.ArtifactsDirectory) :
                ValidateRoot(settings.ArtifactsDirectoryPath);

        public string ValidateRoot(string? path)
        {
            var candidate = string.IsNullOrWhiteSpace(path) ?
                DatabasePaths.ArtifactsDirectory :
                path.Trim();

            if (!Path.IsPathFullyQualified(candidate) || candidate.StartsWith("\\\\", StringComparison.Ordinal))
            {
                throw new ArgumentException(localizationService.Get(
                    "Каталог архивов должен быть абсолютным локальным путём."));
            }

            var fullPath = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathRoot = Path.GetPathRoot(fullPath)?.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(pathRoot) ||
                string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(localizationService.Get(
                    "Каталог архивов не может быть корнем диска."));
            }

            if (File.Exists(fullPath))
            {
                throw new ArgumentException(localizationService.Get(
                    "Каталог архивов не может указывать на файл."));
            }

            foreach (var restrictedPath in GetRestrictedPaths())
            {
                if (IsSameOrChild(fullPath, restrictedPath))
                {
                    throw new ArgumentException(localizationService.Get(
                        "Каталог архивов не может находиться в системной директории."));
                }
            }

            EnsureExistingPathHasNoReparsePoints(fullPath);
            return fullPath;
        }

        public string ResolvePath(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Путь к архиву версии должен быть относительным."));
            }

            var segments = relativePath.Replace('\\', '/').Split('/');
            if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or ".."))
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Путь к архиву версии содержит недопустимый сегмент."));
            }

            var normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootPrefix = normalizedRoot + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(
                normalizedRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Путь к архиву версии выходит за пределы каталога архивов."));
            }

            EnsureExistingPathHasNoReparsePoints(Path.GetDirectoryName(path) ?? normalizedRoot);
            return path;
        }

        public void EnsureExistingPathHasNoReparsePoints(string path)
        {
            var directory = new DirectoryInfo(path);

            while (directory is not null)
            {
                if (directory.Exists &&
                    (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(localizationService.Get(
                        "Путь к архивам не может проходить через reparse point."));
                }

                directory = directory.Parent;
            }
        }

        public void EnsureTreeHasNoReparsePoints(DirectoryInfo directory)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Каталог архивов не может содержать reparse point."));
            }

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(localizationService.Get(
                        "Каталог архивов не может содержать reparse point."));
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    EnsureTreeHasNoReparsePoints(childDirectory);
                }
            }
        }

        public void EnsureNotReparsePoint(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Файл архива не может быть reparse point."));
            }
        }

        public FileAttributes? GetExistingAttributes(string path)
        {
            try
            {
                return File.GetAttributes(path);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        private static IEnumerable<string> GetRestrictedPaths()
        {
            var paths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            };

            return paths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath);
        }

        public bool IsSameOrChild(string path, string parent)
        {
            var normalizedParent = parent.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

            return string.Equals(path, normalizedParent, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(
                    normalizedParent + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase);
        }

        public bool IsDefaultRoot(string path) =>
            string.Equals(
                path,
                Path.GetFullPath(DatabasePaths.ArtifactsDirectory),
                StringComparison.OrdinalIgnoreCase);
    }
}
