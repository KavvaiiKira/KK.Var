using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using KK.Var.Constants;

namespace KK.Var.Services
{
    public static class SourceFiles
    {
        private static readonly HashSet<string> IgnoredDirectories = new HashSet<string>(
            [".git", ".vs", ".idea", ".vscode", "bin", "obj", "node_modules", BuildConstants.PythonVirtualEnvironmentDirectory],
            StringComparer.OrdinalIgnoreCase);

        public static bool IsIgnoredDirectory(string name) => IgnoredDirectories.Contains(name);

        public static void CopyDirectory(
            string source,
            string destination,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destination);

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsIgnoredDirectory(Path.GetFileName(directory)))
                {
                    continue;
                }

                CopyDirectory(
                    directory,
                    Path.Combine(destination, Path.GetFileName(directory)),
                    cancellationToken);
            }

            foreach (var file in Directory.EnumerateFiles(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }
        }
    }
}
