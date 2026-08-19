namespace KK.Var.Configuration;

public sealed class UserSettings
{
    public bool IsFirstRunCompleted { get; set; }

    public string Theme { get; set; } = "System";

    public GitHubSettings GitHub { get; set; } = new();

    public RemoteMachineSettings RemoteMachine { get; set; } = new();
}
