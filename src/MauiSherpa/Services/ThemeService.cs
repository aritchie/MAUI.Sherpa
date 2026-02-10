using MauiSherpa.Core.Interfaces;
#if MACCATALYST
using UIKit;
#endif

namespace MauiSherpa.Services;

public class ThemeService : IThemeService
{
    private string _currentTheme = "System";
    private double _fontScale = 1.0;
    
    public string CurrentTheme => _currentTheme;
    public double FontScale => _fontScale;
    
    public bool IsDarkMode
    {
        get
        {
            if (_currentTheme == "Dark") return true;
            if (_currentTheme == "Light") return false;
            
            // System theme detection
#if MACCATALYST
            var style = UIScreen.MainScreen.TraitCollection.UserInterfaceStyle;
            return style == UIUserInterfaceStyle.Dark;
#elif WINDOWS
            // Windows theme detection
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                var color = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
                return color.R < 128; // Dark if background is dark
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }
    }
    
    public event Action? ThemeChanged;
    
    public void SetTheme(string theme)
    {
        if (_currentTheme == theme) return;
        
        _currentTheme = theme;
        ThemeChanged?.Invoke();
    }

    public void SetFontScale(double scale)
    {
        scale = Math.Clamp(scale, 0.8, 1.5);
        if (Math.Abs(_fontScale - scale) < 0.001) return;
        
        _fontScale = scale;
        ThemeChanged?.Invoke();
    }
}
