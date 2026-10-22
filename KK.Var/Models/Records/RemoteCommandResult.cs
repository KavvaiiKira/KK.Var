namespace KK.Var.Models
{
    public sealed record RemoteCommandResult(int? ExitStatus, string Result, string Error);
}
