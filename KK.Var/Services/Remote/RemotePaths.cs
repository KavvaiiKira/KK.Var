using System;
using System.Linq;

namespace KK.Var.Services
{
    public static class RemotePaths
    {
        public const string HomeDirectoryName = "home";

        private static readonly string[] ProtectedPrefixes =
        [
            "/bin/", "/boot/", "/dev/", "/etc/", "/lib/", "/lib64/",
            "/proc/", "/run/", "/sbin/", "/sys/", "/usr/bin/", "/usr/lib/",
            "/usr/lib64/", "/usr/sbin/", "/usr/share/",
        ];

        public static bool IsProtectedDirectory(string path) => ProtectedPrefixes.Any(prefix =>
            (path.TrimEnd('/') + "/").StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
