using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using KK.Var.ViewModels;

namespace KK.Var
{
    [RequiresUnreferencedCode(
        "Default implementation of ViewLocator involves reflection which may be trimmed away.",
        Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
    public class ViewLocator : IDataTemplate
    {
        private const string MissingViewMessage = "Представление «{0}» не найдено.";

        private const string ViewModelSuffix = "ViewModel";

        private const string ViewSuffix = "View";

        public Control? Build(object? param)
        {
            if (param is null)
            {
                return null;
            }

            var name = param.GetType().FullName!.Replace(ViewModelSuffix, ViewSuffix, StringComparison.Ordinal);
            var type = Type.GetType(name);

            if (type is not null)
            {
                return (Control)Activator.CreateInstance(type)!;
            }

            var message = Application.Current?.TryGetResource(MissingViewMessage, null, out var resource) == true &&
                resource is string translation ? translation : MissingViewMessage;
            return new TextBlock { Text = string.Format(message, name) };
        }

        public bool Match(object? data) =>
            data is ViewModelBase;
    }
}
