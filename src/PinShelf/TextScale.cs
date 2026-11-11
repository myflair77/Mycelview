using System.Windows;

namespace PinShelf;
internal static class TextScale
{
    internal static int Percent { get; private set; } = 100;
    internal static double Factor => 1.1 * Percent / 100d;
    internal static event Action? Changed;
    internal static void Apply(int percent)
    {
        Percent = Math.Clamp(percent, 70, 200);
        foreach (var size in new[] { 9, 10, 11, 12, 13, 14, 17, 18, 26 })
            Application.Current.Resources["FontSize" + size] = size * Factor;
        Application.Current.Resources["FileRowHeight"] = Math.Max(26, 11 * Factor + 12);
        Application.Current.Resources["CardHeaderHeight"] = new GridLength(Math.Max(50, 12 * Factor + 38));
        Application.Current.Resources["CardTitleHeight"] = new GridLength(Math.Max(20, 12 * Factor + 8));
        Changed?.Invoke();
    }
}
