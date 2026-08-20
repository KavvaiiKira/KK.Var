using System;
using KK.Var.Enums;

namespace KK.Var.Services;

public interface ILocalizationService
{
    ApplicationLanguage CurrentLanguage { get; }

    event EventHandler? LanguageChanged;

    string Get(string key);

    string GetKey(string localizedText);

    string Format(string key, params object?[] arguments);

    void SetLanguage(ApplicationLanguage language);
}
