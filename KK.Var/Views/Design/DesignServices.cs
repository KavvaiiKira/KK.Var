using System;
using KK.Var.Services;
using KK.Var.Services.Implementations;

namespace KK.Var.Views.Design
{
    internal static class DesignServices
    {
        private const string RuntimeDependenciesRequiredMessage = "Create the window with its application dependencies.";

        public static ILocalizationService Localization => Avalonia.Controls.Design.IsDesignMode ?
            new LocalizationService() :
            throw new InvalidOperationException(RuntimeDependenciesRequiredMessage);
    }
}
