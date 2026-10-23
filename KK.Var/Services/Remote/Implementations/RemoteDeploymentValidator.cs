using System;
using System.IO;
using KK.Var.Configuration;
using KK.Var.Models;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class RemoteDeploymentValidator : IRemoteDeploymentValidator
    {
        public void Validate(
            RemoteMachineSettings settings,
            KKProject project,
            string artifactPath)
        {
            if (!File.Exists(artifactPath))
            {
                throw new FileNotFoundException(
                    Texts.LocalVersionArchiveNotFound,
                    artifactPath);
            }

            ValidateConnection(settings, project);
        }

        public void ValidateConnection(
            RemoteMachineSettings settings,
            KKProject project)
        {
            if (string.IsNullOrWhiteSpace(settings.Host) ||
                string.IsNullOrWhiteSpace(settings.UserName))
            {
                throw new InvalidOperationException(Texts.RemoteMachineNotConfigured);
            }

            if (string.IsNullOrWhiteSpace(settings.HostKeyFingerprint) ||
                !string.Equals(
                    settings.HostKeyHost,
                    settings.Host.Trim(),
                    StringComparison.OrdinalIgnoreCase) ||
                settings.HostKeyPort != settings.Port)
            {
                throw new InvalidOperationException(Texts.CheckConnectionConfirmSSHServerFingerprint);
            }

            RejectControlCharacters(project.RemoteServiceName);
            RejectControlCharacters(project.RemoteDeploymentDirectory);
            RejectControlCharacters(project.RemoteExecutableFileName);
        }

        public void ValidateOperationId(string operationId)
        {
            if (operationId.Length != 32 ||
                !Guid.TryParseExact(operationId, "N", out _))
            {
                throw new ArgumentException(
                    Texts.InvalidDeploymentOperationIdentifier,
                    nameof(operationId));
            }
        }

        public void RejectControlCharacters(string value)
        {
            if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            {
                throw new InvalidOperationException(Texts.ParameterContainsInvalidCharacters);
            }
        }
    }
}
