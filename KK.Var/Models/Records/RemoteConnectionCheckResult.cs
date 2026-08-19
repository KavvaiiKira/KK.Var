namespace KK.Var.Models;

public sealed record RemoteConnectionCheckResult(
    bool IsSuccessful,
    string Architecture,
    string ErrorMessage)
{
    public static RemoteConnectionCheckResult Success(string architecture) =>
        new(true, architecture, string.Empty);

    public static RemoteConnectionCheckResult Failure(string errorMessage) =>
        new(false, string.Empty, errorMessage);
}
