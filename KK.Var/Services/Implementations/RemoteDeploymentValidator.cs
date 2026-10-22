using System;
using System.IO;
using KK.Var.Configuration;
using KK.Var.Models;

namespace KK.Var.Services.Implementations
{
    public sealed class RemoteDeploymentValidator(ILocalizationService localizationService) : IRemoteDeploymentValidator
    {
        public void Validate(
            RemoteMachineSettings settings,
            KKProject project,
            string artifactPath)
        {
            if (!File.Exists(artifactPath))
            {
                throw new FileNotFoundException(
                    localizationService.Get("Локальный архив версии не найден."),
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
                throw new InvalidOperationException(localizationService.Get(
                    "Удалённая машина не настроена."));
            }

            if (string.IsNullOrWhiteSpace(settings.HostKeyFingerprint) ||
                !string.Equals(
                    settings.HostKeyHost,
                    settings.Host.Trim(),
                    StringComparison.OrdinalIgnoreCase) ||
                settings.HostKeyPort != settings.Port)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Проверьте подключение и подтвердите отпечаток SSH-сервера."));
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
                    localizationService.Get("Недопустимый идентификатор операции развёртывания."),
                    nameof(operationId));
            }
        }

        public void RejectControlCharacters(string value)
        {
            if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
            {
                throw new InvalidOperationException(localizationService.Get(
                    "Параметр содержит недопустимые символы."));
            }
        }
    }
}
