using System;
using System.IO;

namespace KK.Var.Data;

public static class DatabasePaths
{
    private const string ApplicationDirectoryName = "KK.Var";

    public static string UserDataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        ApplicationDirectoryName);

    public static string UserSettingsFilePath =>
        Path.Combine(UserDataDirectory, "settings.json");

    public static string GetDatabaseFilePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            throw new ArgumentException(
                "The database file name must not contain a directory path.",
                nameof(fileName));
        }

        return Path.Combine(UserDataDirectory, fileName);
    }

    public static void EnsureUserDataDirectory()
    {
        Directory.CreateDirectory(UserDataDirectory);
    }
}
