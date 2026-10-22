using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KK.Var.Services.Implementations
{
    internal static class WindowsApplicationIdentity
    {
        private const string ApplicationId = "KK.Var.Desktop";

        public static void Initialize()
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var result = SetCurrentProcessExplicitAppUserModelID(ApplicationId);
            if (result < 0)
            {
                Trace.TraceWarning("Could not set the Windows application identity: 0x{0:X8}.", result);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string applicationId);
    }
}
