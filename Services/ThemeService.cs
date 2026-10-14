using System.Windows.Media;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Barline.Services;

internal static class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static TaskbarPalette LightTaskbar { get; } = new(
        Primary: FrozenBrush(0xFF, 0x17, 0x17, 0x17),
        Secondary: FrozenBrush(0xB8, 0x17, 0x17, 0x17),
        Separator: FrozenBrush(0x66, 0x17, 0x17, 0x17),
        Warning: FrozenBrush(0xFF, 0x9A, 0x58, 0x00),
        Danger: FrozenBrush(0xFF, 0xC4, 0x2B, 0x1C));

    public static TaskbarPalette DarkTaskbar { get; } = new(
        Primary: FrozenBrush(0xFF, 0xF5, 0xF5, 0xF5),
        Secondary: FrozenBrush(0xB8, 0xF5, 0xF5, 0xF5),
        Separator: FrozenBrush(0x66, 0xF5, 0xF5, 0xF5),
        Warning: FrozenBrush(0xFF, 0xFF, 0xB8, 0x4D),
        Danger: FrozenBrush(0xFF, 0xFF, 0x6B, 0x63));

    public static TaskbarPalette GetTaskbarPalette() =>
        IsLightTaskbar() ? LightTaskbar : DarkTaskbar;

    public static bool IsLightTaskbar()
    {
        var isLight = false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            isLight = key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch
        {
            // The dark taskbar palette is the safest fallback.
        }

        return isLight;
    }

    private static Brush FrozenBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }
}

internal sealed record TaskbarPalette(
    Brush Primary,
    Brush Secondary,
    Brush Separator,
    Brush Warning,
    Brush Danger);
