using System;
using System.Globalization;
using System.Text.Json;
using KK.Var.Enums;
using KK.Var.Models;
using KK.Var.Services;

namespace KK.Var.ViewModels;

public sealed class DeploymentDetailsViewModel(
    KKProjectDeployment deployment,
    string? logText,
    ILocalizationService? localizationService = null) : ViewModelBase
{
    public KKProjectDeployment Deployment { get; } = deployment;

    public string ProjectName => Deployment.Project?.Name ?? string.Empty;

    public string VersionTag => Deployment.Version?.Tag ?? string.Empty;

    public string OperationDisplay => Deployment.OperationType switch
    {
        DeploymentOperationType.Deploy => "Deploy",
        DeploymentOperationType.Rollback => "Rollback",
        _ => Deployment.OperationType.ToString(),
    };

    public string StatusDisplay => Deployment.Status switch
    {
        DeploymentStatus.Pending => Localize("Ожидает"),
        DeploymentStatus.Running => Localize("Выполняется"),
        DeploymentStatus.Succeeded => Localize("Успешно"),
        DeploymentStatus.Failed => Localize("Ошибка"),
        DeploymentStatus.Cancelled => Localize("Отменено"),
        DeploymentStatus.Interrupted => Localize("Прервано"),
        _ => Deployment.Status.ToString(),
    };

    public string StartedAtDisplay => Deployment.StartedAtUtc.ToLocalTime().ToString("G");

    public string CompletedAtDisplay => Deployment.CompletedAtUtc?.ToLocalTime().ToString("G") ?? "—";

    public string DurationDisplay
    {
        get
        {
            if (Deployment.CompletedAtUtc is not { } completedAtUtc)
            {
                return "—";
            }

            var duration = completedAtUtc - Deployment.StartedAtUtc;
            return duration.TotalDays >= 1 ?
                string.Format(
                    CultureInfo.CurrentCulture,
                    "{0}.{1:00}:{2:00}:{3:00}",
                    (int)duration.TotalDays,
                    duration.Hours,
                    duration.Minutes,
                    duration.Seconds) :
                duration.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture);
        }
    }

    public string CommitSha => Deployment.Version?.SourceCommitSha ?? string.Empty;

    public bool HasCommitSha => !string.IsNullOrWhiteSpace(CommitSha);

    public string StageDisplay => Deployment.Stage switch
    {
        DeploymentStage.Preparing => Localize("Подготовка"),
        DeploymentStage.ReadyToSwitch => Localize("Готово к переключению"),
        DeploymentStage.SwitchingVersion => Localize("Переключение версии"),
        DeploymentStage.VersionSwitched => Localize("Версия переключена"),
        DeploymentStage.UpdatingUnit => Localize("Обновление unit-файла"),
        DeploymentStage.UnitUpdated => Localize("Unit-файл обновлён"),
        DeploymentStage.StartingService => Localize("Запуск сервиса"),
        DeploymentStage.ServiceStarted => Localize("Сервис запущен"),
        DeploymentStage.HealthChecking => Localize("Проверка работоспособности"),
        DeploymentStage.Committed => Localize("Операция завершена"),
        _ => Deployment.Stage.ToString(),
    };

    public string UnitChangeDisplay => Deployment.UnitChange switch
    {
        DeploymentUnitChange.Unchanged => Localize("Не изменялся"),
        DeploymentUnitChange.Created => Localize("Создан"),
        DeploymentUnitChange.Changed => Localize("Изменён"),
        _ => Deployment.UnitChange.ToString(),
    };

    public string DaemonReloadDisplay =>
        Deployment.UnitChange == DeploymentUnitChange.Unchanged ?
            Localize("Нет") :
            Localize("Да");

    public bool HasError => !string.IsNullOrWhiteSpace(Deployment.ErrorMessage);

    public string ErrorMessage => Deployment.ErrorMessage ?? string.Empty;

    public string VariablesSnapshotDisplay => FormatJson(Deployment.VariablesSnapshotJson);

    public string LogDisplay => logText ?? Localize("Файл лога операции отсутствует или недоступен.");

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(StageDisplay));
        OnPropertyChanged(nameof(UnitChangeDisplay));
        OnPropertyChanged(nameof(DaemonReloadDisplay));
        OnPropertyChanged(nameof(LogDisplay));
    }

    private static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                });
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private string Localize(string key) => localizationService?.Get(key) ?? key;
}
