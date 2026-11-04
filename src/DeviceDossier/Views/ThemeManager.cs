using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using DeviceDossier.Core;

namespace DeviceDossier.Views
{
    public static class ThemeManager
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeLegacy = 19;
        private const string LightSource = "/DeviceDossier;component/Themes/Light.xaml";
        private const string DarkSource = "/DeviceDossier;component/Themes/Dark.xaml";

        private static ResourceDictionary _palette;
        private static string _kind;
        private static bool _listening;

        public static AppTheme Mode { get; private set; }
        public static bool IsDark { get; private set; }

        public static event EventHandler Changed;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static void Initialize(AppTheme mode)
        {
            if (!_listening)
            {
                _listening = true;
                try
                {
                    SystemEvents.UserPreferenceChanged += (s, e) =>
                    {
                        if (Application.Current == null) return;
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => Apply(Mode)));
                    };
                }
                catch
                {
                }
            }
            Apply(mode);
        }

        public static void Apply(AppTheme mode)
        {
            Mode = mode;
            bool dark = mode == AppTheme.Dark || (mode == AppTheme.System && WindowsPrefersDark());
            var app = Application.Current;
            if (app == null) return;

            bool highContrast = SystemParameters.HighContrast;
            string kind = highContrast ? "contrast" : dark ? "dark" : "light";
            bool changed = _palette == null || kind != _kind;
            IsDark = highContrast ? !IsLight(SystemColors.WindowColor) : dark;
            _kind = kind;
            if (changed)
            {
                var next = highContrast ? HighContrastPalette() : new ResourceDictionary { Source = new Uri(dark ? DarkSource : LightSource, UriKind.Relative) };
                var dictionaries = app.Resources.MergedDictionaries;
                int index = -1;
                for (int i = 0; i < dictionaries.Count; i++)
                {
                    var source = dictionaries[i].Source == null ? "" : dictionaries[i].Source.OriginalString;
                    if (dictionaries[i] == _palette || source.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase) || source.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase))
                    {
                        index = i;
                        break;
                    }
                }
                if (index >= 0) dictionaries[index] = next;
                else dictionaries.Insert(0, next);
                _palette = next;
            }

            foreach (Window w in app.Windows) ApplyTitleBar(w);
            var handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static ResourceDictionary HighContrastPalette()
        {
            var d = new ResourceDictionary();
            Action<Color, string[]> set = (color, keys) =>
            {
                foreach (var key in keys)
                {
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    d[key] = brush;
                }
            };
            set(SystemColors.WindowColor, new[] { "AppBackground", "Surface", "SurfaceAlt", "SurfaceHover", "SurfacePressed", "InputBackground", "HeaderBackground", "Neutral",
                "AccentSoft", "SuccessSoft", "WarningSoft", "DangerSoft", "Sidebar", "SidebarHover", "ProgressTrack" });
            set(SystemColors.WindowTextColor, new[] { "BorderBrush", "BorderStrong", "GridLine", "SidebarBorder", "WarningBorder", "ScrollThumb", "ScrollThumbHover",
                "TextPrimary", "TextMuted", "Info", "SidebarText" });
            set(SystemColors.GrayTextColor, new[] { "TextSubtle", "SidebarMuted" });
            set(SystemColors.HighlightColor, new[] { "Accent", "AccentHover", "AccentPressed", "SidebarActive", "Success", "Warning", "Danger" });
            set(SystemColors.HotTrackColor, new[] { "AccentText" });
            set(SystemColors.HighlightTextColor, new[] { "OnAccent", "OnWarning" });
            return d;
        }

        private static bool IsLight(Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 128;
        }

        public static AppTheme Next(AppTheme mode)
        {
            switch (mode)
            {
                case AppTheme.System: return AppTheme.Light;
                case AppTheme.Light: return AppTheme.Dark;
                default: return AppTheme.System;
            }
        }

        public static string Describe(AppTheme mode)
        {
            switch (mode)
            {
                case AppTheme.Light: return "Light";
                case AppTheme.Dark: return "Dark";
                default: return "Match Windows";
            }
        }

        public static void ApplyTitleBar(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int value = IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeLegacy, ref value, sizeof(int));
            }
            catch
            {
            }
        }

        public static bool WindowsPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
