$path = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$content = Get-Content -Path $path -Raw

# 1. Add FadeInPage method
$fadeInMethod = @"
        private void FadeInPage(System.Windows.UIElement page)
        {
            if (page == null) return;
            var da = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new System.Windows.Duration(TimeSpan.FromSeconds(0.25)),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            page.BeginAnimation(System.Windows.UIElement.OpacityProperty, da);
        }

        // --- SIDEBAR NAVIGATION ---
"@
$content = $content.Replace("        // --- SIDEBAR NAVIGATION ---", $fadeInMethod)

# 2. Add FadeInPage to Nav_Checked
# Match any PageXYZ.Visibility = Visibility.Visible; and append FadeInPage(PageXYZ);
$content = $content -replace '(Page\w+)\.Visibility = Visibility\.Visible;', '$1.Visibility = Visibility.Visible; FadeInPage($1);'

Set-Content -Path $path -Value $content
Write-Host "Injected UI Smooth Animations"
