using System.Globalization;
using KK.Var.Enums;

namespace KK.Var.Configuration
{
    public sealed class UserSettings
    {
        private const string RussianCultureCode = "ru";

        public bool IsFirstRunCompleted { get; set; }

        public bool DeploymentNotificationsEnabled { get; set; } = true;

        public string? ArtifactsDirectoryPath { get; set; }

        public ApplicationLanguage Language { get; set; } =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == RussianCultureCode ?
                ApplicationLanguage.Russian :
                ApplicationLanguage.English;

        public GitHubSettings GitHub { get; set; } = new GitHubSettings();
    }
}
