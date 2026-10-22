using System;
using KK.Var.Constants;
using KK.Var.Enums;
using KK.Var.Models;

namespace KK.Var.Services
{
    public static class ProjectRuntime
    {
        public static bool IsPythonProject(KKProject project) =>
            project.BuildProvider == ProjectBuildProvider.Python ||
            project.RemoteExecutableFileName.EndsWith(BuildConstants.PythonScriptExtension, StringComparison.OrdinalIgnoreCase);

        public static string GetExecutableRelativePath(KKProject project) =>
            project.RemoteExecutableFileName.EndsWith(BuildConstants.DotNetAssemblyExtension, StringComparison.OrdinalIgnoreCase) ?
                project.RemoteExecutableFileName[..^4] :
                project.RemoteExecutableFileName;
    }
}
