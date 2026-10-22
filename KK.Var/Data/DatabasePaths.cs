using System;
using System.IO;
using KK.Var.Constants;

namespace KK.Var.Data
{
    public static class DatabasePaths
    {
        private const string InvalidDatabaseFileNameMessage = "Имя файла базы данных не должно содержать путь к каталогу.";

        public static string UserDataDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ApplicationConstants.Name);

        public static string UserSettingsFilePath =>
            Path.Combine(UserDataDirectory, ApplicationConstants.SettingsFileName);

        public static string GitHubTokenFilePath =>
            Path.Combine(UserDataDirectory, ApplicationConstants.GitHubTokenFileName);

        public static string ArtifactsDirectory =>
            Path.Combine(UserDataDirectory, ApplicationConstants.ArtifactsDirectoryName);

        public static string LogsDirectory =>
            Path.Combine(UserDataDirectory, ApplicationConstants.LogsDirectoryName);

        public static string RecoveryDirectory =>
            Path.Combine(UserDataDirectory, ApplicationConstants.RecoveryDirectoryName);

        public static string GetDatabaseFilePath(string fileName) =>
            string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName ?
                throw new ArgumentException(
                    InvalidDatabaseFileNameMessage) :
                Path.Combine(UserDataDirectory, fileName);

        public static void EnsureUserDataDirectory() =>
            Directory.CreateDirectory(UserDataDirectory);
    }
}
