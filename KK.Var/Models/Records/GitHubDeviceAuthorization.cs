using System;

namespace KK.Var.Models;

public sealed record GitHubDeviceAuthorization(
    string DeviceCode,
    string UserCode,
    Uri VerificationUri,
    DateTimeOffset ExpiresAtUtc,
    TimeSpan PollingInterval);
