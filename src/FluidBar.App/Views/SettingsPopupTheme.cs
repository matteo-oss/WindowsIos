using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using FluidBar.App.Settings;
using FluidBar.App.Views.Settings;

namespace FluidBar.App.Views;

internal static class SettingsPopupTheme
{
    public static SettingsPalette Palette(SettingsController settings)
    {
        var configured = settings.Current.Theme;
        var theme = configured == AppTheme.System
            ? (Application.Current?.ActualThemeVariant == ThemeVariant.Light ? AppTheme.Light : AppTheme.Dark)
            : configured;
        var glass = settings.Current.Material is IslandMaterial.Acrylic or IslandMaterial.LiquidGlass;
        return SettingsPalette.For(theme, glass);
    }

    public static void Apply(Window window, Border root, Border editor, SettingsPalette palette)
    {
        window.Background = palette.WindowBrush;
        window.Foreground = palette.ForegroundBrush;
        root.Background = palette.WindowBrush;
        editor.Background = palette.RaisedBrush;
        editor.BorderBrush = palette.LineBrush;
        editor.BorderThickness = new Thickness(1);
        editor.CornerRadius = new CornerRadius(SettingsPalette.CardCornerRadiusDip);
    }

    public static void ApplyRow(Border row, SettingsPalette palette)
    {
        row.Background = palette.RaisedBrush;
        row.BorderBrush = palette.LineBrush;
        row.BorderThickness = new Thickness(1);
        row.CornerRadius = new CornerRadius(SettingsPalette.CardCornerRadiusDip);
    }
}
