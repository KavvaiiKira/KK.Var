using System;
using KK.Var.Enums;

namespace KK.Var.Models
{
    public sealed class VersionTagException(
        VersionTagError error,
        string message,
        Exception? innerException = null) : InvalidOperationException(message, innerException)
    {
        public VersionTagError Error { get; } = error;
    }
}
