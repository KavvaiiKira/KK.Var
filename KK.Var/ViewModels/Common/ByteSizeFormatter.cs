using KK.Var.Services;
using Texts = KK.Var.Resources.Strings;

namespace KK.Var.ViewModels
{
    internal static class ByteSizeFormatter
    {
        public static string Format(long size, ILocalizationService localizationService) => size switch
        {
            >= 1_073_741_824 => $"{size / 1_073_741_824d:F2} {Texts.GB}",
            >= 1_048_576 => $"{size / 1_048_576d:F2} {Texts.MB}",
            >= 1024 => $"{size / 1024d:F1} {Texts.KB}",
            _ => $"{size} {Texts.B}",
        };
    }
}
