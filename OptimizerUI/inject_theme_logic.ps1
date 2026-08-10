$csPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$cs = Get-Content $csPath -Raw

$method = @"
        private void RdbTheme_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool isDark = RdbThemeDark.IsChecked == true;
            var app = Application.Current;
            var uri = new Uri($"Themes/{(isDark ? "Dark" : "Light")}Theme.xaml", UriKind.Relative);
            var dict = new ResourceDictionary() { Source = uri };
            
            if (app.Resources.MergedDictionaries.Count > 0)
            {
                app.Resources.MergedDictionaries[0] = dict;
            }
            else
            {
                app.Resources.MergedDictionaries.Add(dict);
            }
            ApplyTransparency();
        }

        private void SldTransparency_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            ApplyTransparency();
        }

        private void ApplyTransparency()
        {
            if (!IsLoaded || SldTransparency == null) return;
            
            byte alpha = (byte)((SldTransparency.Value / 100.0) * 255.0);
            var app = Application.Current;
            
            if (app.Resources["AppBackgroundColor"] is Color appBgColor)
            {
                appBgColor.A = alpha;
                app.Resources["AppBackgroundBrush"] = new SolidColorBrush(appBgColor);
            }
            
            if (app.Resources["GlassCardBackgroundColor"] is Color cardBgColor)
            {
                cardBgColor.A = (byte)(Math.Min(255, alpha + 15));
                app.Resources["GlassCardBackgroundBrush"] = new SolidColorBrush(cardBgColor);
            }
            
            if (app.Resources["SidebarBackgroundColor"] is Color sidebarBgColor)
            {
                sidebarBgColor.A = (byte)(Math.Min(255, alpha + 10));
                app.Resources["SidebarBackgroundBrush"] = new SolidColorBrush(sidebarBgColor);
            }
        }
"@

$insertIndex = $cs.LastIndexOf("    }")
if ($insertIndex -gt 0) {
    $cs = $cs.Insert($insertIndex, $method + "`n")
}

Set-Content -Path $csPath -Value $cs -Encoding UTF8
