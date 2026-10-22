using KK.Var.Services;

namespace KK.Var.ViewModels
{
    internal static class ByteSizeFormatter
    {
        public static string Format(long size, ILocalizationService localizationService) => size switch
        {
            >= 1_073_741_824 => $"{size / 1_073_741_824d:F2} {localizationService.Get("ГБ")}",
            >= 1_048_576 => $"{size / 1_048_576d:F2} {localizationService.Get("МБ")}",
            >= 1024 => $"{size / 1024d:F1} {localizationService.Get("КБ")}",
            _ => $"{size} {localizationService.Get("Б")}",
        };
    }
}
