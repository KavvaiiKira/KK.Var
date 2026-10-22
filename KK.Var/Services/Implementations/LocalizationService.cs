using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using KK.Var.Enums;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.Services.Implementations
{
    public sealed class LocalizationService : ILocalizationService
    {
        private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");

        public ApplicationLanguage CurrentLanguage { get; private set; } = ApplicationLanguage.Russian;

        public event EventHandler? LanguageChanged;

        public string Get(string key) =>
            Texts.ResourceManager.GetString(
                key,
                CurrentLanguage == ApplicationLanguage.English ?
                    CultureInfo.GetCultureInfo("en-US") :
                    RussianCulture) ?? key;

        public string Format(string key, params object?[] arguments) =>
            string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

        public void SetLanguage(ApplicationLanguage language)
        {
            if (!Enum.IsDefined(language))
            {
                language = ApplicationLanguage.Russian;
            }

            CurrentLanguage = language;
            var culture = CultureInfo.GetCultureInfo(language == ApplicationLanguage.English ? "en-US" : "ru-RU");
            Texts.Culture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            if (Application.Current?.Resources is ResourceDictionary resources &&
                Texts.ResourceManager.GetResourceSet(culture, true, true) is { } translations)
            {
                // One resource notification updates the UI after all translations are in place.
                resources.SetItems(translations.Cast<DictionaryEntry>()
                    .Select(entry => new KeyValuePair<object, object?>(entry.Key, entry.Value)));
            }

            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

    }
}
