namespace KK.Var.Models;

public sealed record RemoteConnectionCheckResult(
    bool IsSuccessful,
    string Architecture,
    string ErrorMessage,
    bool RequiresHostKeyConfirmation,
    string HostKeyFingerprint)
{
    public static RemoteConnectionCheckResult Success(
        string architecture,
        string hostKeyFingerprint) =>
        new RemoteConnectionCheckResult(true, architecture, string.Empty, false, hostKeyFingerprint);

    public static RemoteConnectionCheckResult ConfirmationRequired(
        string hostKeyFingerprint) =>
        new RemoteConnectionCheckResult(false, string.Empty, string.Empty, true, hostKeyFingerprint);

    public static RemoteConnectionCheckResult Failure(
        string errorMessage) =>
        new RemoteConnectionCheckResult(false, string.Empty, errorMessage, false, string.Empty);
}
