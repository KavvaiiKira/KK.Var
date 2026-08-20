using KK.Var.Enums;
using System.Globalization;

namespace KK.Var.Configuration;

public sealed class UserSettings
{
    public bool IsFirstRunCompleted { get; set; }

    public string Theme { get; set; } = "System";

    public ApplicationLanguage Language { get; set; } =
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru"
            ? ApplicationLanguage.Russian
            : ApplicationLanguage.English;

    public GitHubSettings GitHub { get; set; } = new();

    public RemoteMachineSettings RemoteMachine { get; set; } = new();
}
