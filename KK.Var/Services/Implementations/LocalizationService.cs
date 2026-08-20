using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using KK.Var.Enums;

namespace KK.Var.Services.Implementations;

public sealed class LocalizationService : ILocalizationService
{
    private static readonly IReadOnlyDictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["• адрес и SSH-порт Linux-машины"] = "• Linux machine address and SSH port",
            ["• пользователя SSH"] = "• SSH user",
            ["• приватный ключ или пароль"] = "• private key or password",
            ["＋  Новый проект"] = "＋  New project",
            ["＋ Добавить"] = "＋ Add",
            ["Автоопределение можно уточнить вручную"] = "Automatic detection can be overridden manually",
            ["Автосохранение не завершилось или в списке есть незаполненная переменная."] = "Autosave has not completed or a variable is incomplete.",
            ["Адрес или IP"] = "Address or IP",
            ["Аккаунт"] = "Account",
            ["Архитектура удалённой машины"] = "Remote machine architecture",
            ["Введённые данные проекта будут потеряны."] = "The entered project data will be lost.",
            ["Версии"] = "Versions",
            ["Версии, переменные окружения и история deploy будут удалены из базы данных."] = "Versions, environment variables, and deploy history will be removed from the database.",
            ["Версия"] = "Version",
            ["Версия создаётся автоматически после успешной сборки. Архив и контрольная сумма неизменяемы."] = "A version is created automatically after a successful build. Its archive and checksum are immutable.",
            ["Во время deploy они будут записаны в выбранном формате по пути, указанному в проекте."] = "During deploy, they will be written in the selected format to the path configured in the project.",
            ["Выберите папку на этом компьютере"] = "Select a folder on this computer",
            ["Выберите репозиторий"] = "Select a repository",
            ["Далее"] = "Next",
            ["Дата"] = "Date",
            ["Действие"] = "Action",
            ["Директория на удалённой машине"] = "Remote machine directory",
            ["Для создания и развёртывания проектов KK.Var должен знать, к какой Linux-машине подключаться."] = "To create and deploy projects, KK.Var needs to know which Linux machine to connect to.",
            ["Добавить проект"] = "Add project",
            ["Доступ к списку ваших репозиториев через GitHub Device Flow"] = "Access your repository list through GitHub Device Flow",
            ["Завершите обязательную настройку"] = "Complete the required setup",
            ["Закрыть"] = "Close",
            ["Заполните отмеченные поля подключения к удалённой машине и сохраните настройки."] = "Complete the required remote connection fields and save the settings.",
            ["Значение"] = "Value",
            ["ИМЯ_ПЕРЕМЕННОЙ"] = "VARIABLE_NAME",
            ["Исполняемый файл или точка входа"] = "Executable file or entry point",
            ["История"] = "History",
            ["История пока пуста"] = "History is empty",
            ["История проекта"] = "Project history",
            ["Источник"] = "Source",
            ["Источник, сборка и параметры развёртывания"] = "Source, build, and deployment settings",
            ["Исходный код"] = "Source code",
            ["Как проходит deploy"] = "How deploy works",
            ["Копировать код"] = "Copy code",
            ["Назад"] = "Back",
            ["Назад к проектам"] = "Back to projects",
            ["Название проекта"] = "Project name",
            ["Название systemd-сервиса"] = "systemd service name",
            ["Например, Billing API"] = "For example, Billing API",
            ["Например, release-1"] = "For example, release-1",
            ["Настройки"] = "Settings",
            ["Не подключён"] = "Not connected",
            ["Не сохранять"] = "Discard",
            ["Не сохранять изменения?"] = "Discard changes?",
            ["Необязательно — иначе используется название"] = "Optional — the project name is used by default",
            ["Обзор"] = "Overview",
            ["Обзор..."] = "Browse...",
            ["Обновить"] = "Refresh",
            ["Одна машина для доставки проектов и управления systemd-сервисами"] = "One machine for delivering projects and managing systemd services",
            ["Описание"] = "Description",
            ["Осталось настроить доступ"] = "Configure access to continue",
            ["Остаться"] = "Stay",
            ["Отключить"] = "Disconnect",
            ["Открыть"] = "Open",
            ["Открыть настройки"] = "Open settings",
            ["Открыть GitHub"] = "Open GitHub",
            ["Отмена"] = "Cancel",
            ["Папка проекта"] = "Project folder",
            ["Пароль"] = "Password",
            ["Первый запуск"] = "First launch",
            ["Переменные"] = "Variables",
            ["Переменные ещё не сохранены"] = "Variables have not been saved",
            ["Переменные окружения"] = "Environment variables",
            ["Подключён"] = "Connected",
            ["Подключить GitHub"] = "Connect GitHub",
            ["Подтвердите вход в GitHub"] = "Confirm sign-in to GitHub",
            ["Показать ещё"] = "Show more",
            ["Пользователь"] = "User",
            ["Помощь"] = "Help",
            ["После этого шага откроется страница настроек. Для начала работы обязательно укажите:"] = "The settings page will open after this step. To get started, provide:",
            ["Последний deploy"] = "Last deploy",
            ["Проверить подключение"] = "Check connection",
            ["Продолжить редактирование"] = "Continue editing",
            ["Проект"] = "Project",
            ["Проект, версия или ДД/ММ/ГГГГ"] = "Project, version, or DD/MM/YYYY",
            ["Проекты"] = "Projects",
            ["Простой локальный CI/CD"] = "Simple local CI/CD",
            ["Путь задаётся относительно корня проекта. Сам файл KK.Var сформирует при сборке."] = "The path is relative to the project root. KK.Var creates the file during the build.",
            ["Путь к приватному SSH-ключу"] = "Private SSH key path",
            ["Развернуть или восстановить"] = "Maximize or restore",
            ["Редактировать"] = "Edit",
            ["Репозиторий GitHub"] = "GitHub repository",
            ["Сборка"] = "Build",
            ["Сборка и запуск"] = "Build and run",
            ["Сборка, доставка и управление локальными релизами"] = "Build, delivery, and local release management",
            ["Скрыть окно"] = "Minimize window",
            ["Следующая версия"] = "Next version",
            ["Сначала настройте SSH-доступ"] = "Configure SSH access first",
            ["Собрать и выполнить deploy"] = "Build and deploy",
            ["Сохранить"] = "Save",
            ["Сохранить и перейти"] = "Save and continue",
            ["Сохранить сейчас"] = "Save now",
            ["Способ входа"] = "Authentication method",
            ["Способ получения исходников"] = "Source type",
            ["Статус"] = "Status",
            ["Тип проекта"] = "Project type",
            ["Тип сборки"] = "Build type",
            ["Три понятных этапа без удалённого хранилища релизов"] = "Three clear stages without remote release storage",
            ["У проекта пока нет собранных версий"] = "This project has no built versions yet",
            ["Удалённая Linux-машина"] = "Remote Linux machine",
            ["Удалить"] = "Delete",
            ["Удалить проект?"] = "Delete project?",
            ["Уйти без сохранения"] = "Discard and leave",
            ["Файл переменных окружения"] = "Environment variables file",
            ["Что изменилось в этой версии"] = "What changed in this version",
            ["Deploy и rollback всех проектов в порядке выполнения"] = "Deploy and rollback operations for all projects in chronological order",
            ["Deploy и rollback ещё не выполнялись"] = "No deploy or rollback operations yet",
            ["Deploy проекта"] = "Project Deploy",
            ["GitHub и подключение к удалённой Linux-машине"] = "GitHub and remote Linux machine connection",
            ["GitHub или папка"] = "GitHub or folder",
            ["GitHub можно подключить сейчас или позже."] = "You can connect GitHub now or later.",
            ["KK.Var будет собирать проект из выбранного источника"] = "KK.Var will build the project from the selected source",
            ["KK.Var соберёт исходный код, создаст локальную версию, передаст файлы на Linux-машину и перезапустит systemd-сервис."] = "KK.Var will build the source code, create a local version, transfer the files to the Linux machine, and restart the systemd service.",
            ["KK.Var собирает проекты с GitHub или из локальной папки, хранит ваши версии локально и доставляет их на Linux-машину."] = "KK.Var builds projects from GitHub or a local folder, stores versions locally, and delivers them to a Linux machine.",
            ["SSH и systemd"] = "SSH and systemd",
            ["SSH-порт"] = "SSH port",
            ["SSH-ключ"] = "SSH key",
            ["Локальная папка"] = "Local folder",
            ["Определить автоматически"] = "Detect automatically",
            ["Свой сценарий"] = "Custom script",
            ["Редактирование проекта"] = "Edit project",
            ["Новый проект"] = "New project",
            ["Сохранить изменения"] = "Save changes",
            ["Сохранить проект"] = "Save project",
            ["Все проекты"] = "All projects",
            ["Ожидает"] = "Pending",
            ["Выполняется"] = "Running",
            ["Успешно"] = "Succeeded",
            ["Ошибка"] = "Failed",
            ["Отменено"] = "Cancelled",
            ["Без описания"] = "No description",
            ["Не выполнялся"] = "Never",
            ["Нет версий"] = "No versions",
            ["Проверка SSH-подключения"] = "Checking SSH connection",
            ["Подключение GitHub"] = "Connecting GitHub",
            ["Загрузка репозиториев GitHub"] = "Loading GitHub repositories",
            ["Удаление проекта"] = "Deleting project",
            ["Загрузка проекта"] = "Loading project",
            ["Сохранение переменных окружения"] = "Saving environment variables",
            ["Выполнение deploy"] = "Running Deploy",
            ["Сохранение проекта"] = "Saving project",
            ["Нет активных операций"] = "No active operations",
            ["Все изменения сохранены"] = "All changes saved",
            ["Укажите тег новой версии."] = "Enter a tag for the new version.",
            ["Версия «{0}» успешно развёрнута"] = "Version “{0}” was deployed successfully",
            ["ОШИБКА: {0}"] = "ERROR: {0}",
            ["Выполнен rollback на версию «{0}»"] = "Rolled back to version “{0}”",
            ["Заполните имя каждой переменной"] = "Enter a name for every variable",
            ["Формат и переменные окружения сохранены"] = "Environment format and variables saved",
            ["Есть несохранённые изменения"] = "There are unsaved changes",
            ["Сохранение..."] = "Saving...",
            ["Проект «{0}» добавлен"] = "Project “{0}” added",
            ["Проект «{0}» изменён"] = "Project “{0}” updated",
            ["Проект «{0}» удалён"] = "Project “{0}” deleted",
            ["Получаем код авторизации..."] = "Requesting an authorization code...",
            ["Введите этот код на открывшейся странице GitHub"] = "Enter this code on the GitHub page",
            ["GitHub успешно подключён"] = "GitHub connected successfully",
            ["Подключение GitHub отменено"] = "GitHub connection cancelled",
            ["GitHub отключён"] = "GitHub disconnected",
            ["Настройки сохранены"] = "Settings saved",
            ["Подключение и определение архитектуры..."] = "Connecting and detecting architecture...",
            ["Подключение успешно"] = "Connection successful",
            ["Проект не найден после обновления."] = "Project was not found after refresh.",
            ["Укажите адрес удалённой машины."] = "Enter the remote machine address.",
            ["SSH-порт должен находиться в диапазоне от 1 до 65535."] = "SSH port must be between 1 and 65535.",
            ["Укажите пользователя SSH."] = "Enter the SSH user.",
            ["Укажите путь к приватному SSH-ключу."] = "Enter the private SSH key path.",
            ["Укажите пароль SSH."] = "Enter the SSH password.",
            ["Будет определена автоматически при проверке подключения"] = "Detected automatically during the connection check",
            ["Определена автоматически: {0}"] = "Detected automatically: {0}",
            ["Сначала подключите GitHub на странице настроек."] = "Connect GitHub on the settings page first.",
            ["Укажите название проекта."] = "Enter a project name.",
            ["Выберите локальную папку проекта."] = "Select the local project folder.",
            ["Выберите репозиторий GitHub."] = "Select a GitHub repository.",
            ["Укажите название systemd-сервиса."] = "Enter the systemd service name.",
            ["Укажите исполняемый файл или точку входа."] = "Enter the executable file or entry point.",
            ["Укажите директорию развёртывания на удалённой машине."] = "Enter the deployment directory on the remote machine.",
            ["Укажите путь к файлу переменных окружения внутри проекта."] = "Enter the environment file path inside the project.",
            ["Дождитесь завершения Deploy. Закрытие приложения во время переключения версии заблокировано."] = "Wait for Deploy to finish. Closing the application during version switching is blocked.",
            ["Проект «{0}» будет удалён без возможности отмены."] = "Project “{0}” will be deleted permanently.",
            ["Выберите папку проекта"] = "Select project folder",
            ["SSH подключён, но определить архитектуру машины не удалось."] = "SSH connected, but the machine architecture could not be detected.",
            ["SSH-сервер отклонил указанные данные для входа."] = "The SSH server rejected the supplied credentials.",
            ["Не удалось установить SSH-соединение: {0}"] = "Could not establish an SSH connection: {0}",
            ["Удалённая машина недоступна: {0}"] = "The remote machine is unavailable: {0}",
            ["Файл приватного SSH-ключа не найден."] = "The private SSH key file was not found.",
            ["Нет доступа к файлу приватного SSH-ключа."] = "The private SSH key file cannot be accessed.",
            ["Проверка подключения завершилась ошибкой: {0}"] = "Connection check failed: {0}",
            ["Подготовка исходного кода"] = "Preparing source code",
            ["Сборка: {0}"] = "Build: {0}",
            ["Создание локального архива"] = "Creating local archive",
            ["Проверка удалённой машины"] = "Checking remote machine",
            ["Удалённая машина готова к Deploy"] = "Remote machine is ready for Deploy",
            ["Загрузка архива на удалённую машину"] = "Uploading archive to remote machine",
            ["Распаковка новой версии"] = "Extracting new version",
            ["Systemd unit будет создан"] = "systemd unit will be created",
            ["Systemd unit будет обновлён"] = "systemd unit will be updated",
            ["Systemd unit не изменился"] = "systemd unit is unchanged",
            ["Переключение версии"] = "Switching version",
            ["Systemd перечитал изменённый unit"] = "systemd reloaded the updated unit",
            ["Запуск systemd-сервиса"] = "Starting systemd service",
            ["Systemd-сервис успешно запущен"] = "systemd service started successfully",
            ["Deploy завершён"] = "Deploy completed",
            ["Ошибка Deploy, восстановление предыдущей версии"] = "Deploy failed, restoring previous version",
            ["Предыдущая версия восстановлена"] = "Previous version restored",
            ["Авторизация GitHub была отменена пользователем."] = "GitHub authorization was cancelled by the user.",
            ["Срок действия кода GitHub истёк. Запустите подключение ещё раз."] = "The GitHub code has expired. Start the connection again.",
            ["Для GitHub OAuth App не включён Device Flow."] = "Device Flow is not enabled for the GitHub OAuth App.",
            ["GitHub Client ID указан неверно."] = "The GitHub Client ID is invalid.",
            ["GitHub не выдал токен доступа."] = "GitHub did not issue an access token.",
            ["Некорректное имя репозитория GitHub."] = "Invalid GitHub repository name.",
            ["GitHub вернул ошибку HTTP {0}."] = "GitHub returned HTTP error {0}.",
            ["GitHub Client ID не настроен в appsettings.json."] = "GitHub Client ID is not configured in appsettings.json.",
            ["Тег версии может содержать только латинские буквы, цифры, точку, дефис и подчёркивание."] = "A version tag may contain only Latin letters, digits, periods, hyphens, and underscores.",
            ["После сборки не найден исполняемый файл «{0}»."] = "The executable file “{0}” was not found after the build.",
            ["Артефакт версии «{0}» уже существует."] = "An artifact for version “{0}” already exists.",
            ["Не указана локальная папка проекта."] = "The local project folder is not configured.",
            ["Локальная папка проекта не найдена: {0}"] = "The local project folder was not found: {0}",
            ["Не указан репозиторий GitHub."] = "The GitHub repository is not configured.",
            ["Подключите GitHub в настройках перед сборкой проекта."] = "Connect GitHub in settings before building the project.",
            ["Не удалось автоматически определить способ сборки проекта."] = "The project build type could not be detected automatically.",
            ["Найдено несколько способов сборки. Выберите нужный в настройках проекта."] = "Multiple build types were detected. Select the correct one in project settings.",
            ["Для C++ требуется настроенный Linux cross-toolchain. Он будет добавлен как отдельная конфигурация сборки."] = "C++ requires a configured Linux cross-toolchain. It will be added as a separate build configuration.",
            ["Свой сценарий сборки ещё не настроен для этого проекта."] = "A custom build script has not been configured for this project.",
            ["Архитектура удалённой машины «{0}» пока не поддерживается для .NET."] = "Remote architecture “{0}” is not currently supported for .NET.",
            ["Архитектура удалённой машины «{0}» пока не поддерживается для Go."] = "Remote architecture “{0}” is not currently supported for Go.",
            ["Не удалось запустить {0}."] = "Could not start {0}.",
            ["Сборка завершилась с кодом {0}: {1}"] = "Build exited with code {0}: {1}",
            ["Не удалось однозначно выбрать исполняемый .csproj. Имя выходной сборки должно совпадать с исполняемым файлом проекта."] = "The executable .csproj could not be selected unambiguously. Its assembly name must match the configured executable file.",
            ["Найдено несколько исполняемых .csproj с одинаковым именем сборки."] = "Multiple executable .csproj files have the same assembly name.",
            ["неизвестная ошибка"] = "unknown error",
            ["Архив GitHub пуст."] = "The GitHub archive is empty.",
            ["Архив GitHub содержит небезопасный путь."] = "The GitHub archive contains an unsafe path.",
            ["Архитектура удалённой машины изменилась: ожидалась {0}, получена {1}. Выполните проверку подключения ещё раз."] = "The remote machine architecture changed: expected {0}, received {1}. Run the connection check again.",
            ["Удалённая команда завершилась с кодом {0}."] = "Remote command exited with code {0}.",
            ["Локальный архив версии не найден."] = "The local version archive was not found.",
            ["Удалённая машина не настроена."] = "The remote machine is not configured.",
            ["Параметр содержит недопустимые символы."] = "A parameter contains invalid characters.",
            ["ГБ"] = "GB",
            ["МБ"] = "MB",
            ["КБ"] = "KB",
            ["Б"] = "B",
            ["Проект не найден."] = "Project not found.",
            ["Сначала проверьте SSH-подключение, чтобы определить архитектуру удалённой машины."] = "Check the SSH connection first to detect the remote machine architecture.",
            ["Версия не найдена."] = "Version not found.",
            ["Версия принадлежит другому проекту."] = "The version belongs to another project.",
            ["Путь к архиву версии недопустим."] = "The version archive path is invalid.",
            ["Локальный архив выбранной версии не найден."] = "The local archive for the selected version was not found.",
            ["Контрольная сумма архива версии не совпадает."] = "The version archive checksum does not match.",
            ["Переключить на английский"] = "Switch to English",
            ["Переключить на русский"] = "Switch to Russian",
        };

    public ApplicationLanguage CurrentLanguage { get; private set; } =
        ApplicationLanguage.Russian;

    public event EventHandler? LanguageChanged;

    public string Get(string key) =>
        CurrentLanguage == ApplicationLanguage.English &&
        English.TryGetValue(key, out var translation)
            ? translation
            : key;

    public string GetKey(string localizedText)
    {
        foreach (var item in English)
        {
            if (string.Equals(item.Value, localizedText, StringComparison.Ordinal))
            {
                return item.Key;
            }
        }

        return localizedText;
    }

    public string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    public void SetLanguage(ApplicationLanguage language)
    {
        if (!Enum.IsDefined(language))
        {
            language = ApplicationLanguage.Russian;
        }

        CurrentLanguage = language;
        var culture = new CultureInfo(
            language == ApplicationLanguage.English ? "en-US" : "ru-RU");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (Application.Current?.Resources is { } resources)
        {
            foreach (var key in English.Keys)
            {
                resources[key] = Get(key);
            }
        }

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
