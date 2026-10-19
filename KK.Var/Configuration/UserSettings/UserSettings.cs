using KK.Var.Enums;
using System.Globalization;
using System.Text.Json.Serialization;

namespace KK.Var.Configuration;

public sealed class UserSettings
{
    public bool IsFirstRunCompleted { get; set; }

    public string Theme { get; set; } = "System";

    public bool DeploymentNotificationsEnabled { get; set; } = true;

    public string? ArtifactsDirectoryPath { get; set; }

    public ApplicationLanguage Language { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ?
            ApplicationLanguage.Russian :
            ApplicationLanguage.English;

    public GitHubSettings GitHub { get; set; } = new GitHubSettings();

    [JsonIgnore]
    public RemoteMachineSettings RemoteMachine { get; set; } = new RemoteMachineSettings();
}
