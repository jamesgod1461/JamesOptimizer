$csPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$lines = Get-Content $csPath

# The file from line 189 looks like this:
# 189:             };
# 190: 
# 191:             }
# 192:         }
# 193: 
# 194:         private void BtnMinimize_Click(object sender, RoutedEventArgs e)
# ...
# 220:         }
# 221: 
# 222:             PlayClickSound();
# 223:             Close();
# 224:         }

$before190 = $lines[0..189]
$after224 = $lines[225..($lines.Length-1)]

$newLines = @"
            try { clickSound = new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Default.wav"); clickSound.LoadAsync(); } catch { }
            try { hoverSound = new System.Media.SoundPlayer(@"C:\Windows\Media\Windows Balloon.wav"); hoverSound.LoadAsync(); } catch { }

            InitializeResourceMonitors();
        }

        // --- SIDEBAR ANIMATION ---
        private void BtnToggleSidebar_Checked(object sender, RoutedEventArgs e)
        {
            var animWidth = new System.Windows.Media.Animation.DoubleAnimation { To = 70, Duration = TimeSpan.FromSeconds(0.4), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
            SidebarContainer.BeginAnimation(FrameworkElement.WidthProperty, animWidth);
            
            if (SidebarFooterText != null)
            {
                var animOpacity = new System.Windows.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(0.2) };
                SidebarFooterText.BeginAnimation(UIElement.OpacityProperty, animOpacity);
            }
            
            if (BtnToggleSidebar.RenderTransform is System.Windows.Media.RotateTransform rt)
            {
                var animRotate = new System.Windows.Media.Animation.DoubleAnimation { To = 180, Duration = TimeSpan.FromSeconds(0.4), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
                rt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, animRotate);
            }
        }

        private void BtnToggleSidebar_Unchecked(object sender, RoutedEventArgs e)
        {
            var animWidth = new System.Windows.Media.Animation.DoubleAnimation { To = 240, Duration = TimeSpan.FromSeconds(0.4), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
            SidebarContainer.BeginAnimation(FrameworkElement.WidthProperty, animWidth);
            
            if (SidebarFooterText != null)
            {
                var animOpacity = new System.Windows.Media.Animation.DoubleAnimation { To = 1, BeginTime = TimeSpan.FromSeconds(0.2), Duration = TimeSpan.FromSeconds(0.3) };
                SidebarFooterText.BeginAnimation(UIElement.OpacityProperty, animOpacity);
            }
            
            if (BtnToggleSidebar.RenderTransform is System.Windows.Media.RotateTransform rt)
            {
                var animRotate = new System.Windows.Media.Animation.DoubleAnimation { To = 0, Duration = TimeSpan.FromSeconds(0.4), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut } };
                rt.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, animRotate);
            }
        }

        // --- CUSTOM WINDOW CONTROLS ---
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            WindowState = WindowState.Minimized;
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (SpotlightOverlay?.Fill is RadialGradientBrush brush)
            {
                System.Windows.Point p = e.GetPosition(this);
                double x = p.X / this.ActualWidth;
                double y = p.Y / this.ActualHeight;
                brush.Center = new System.Windows.Point(x, y);
                brush.GradientOrigin = new System.Windows.Point(x, y);
                
                // Keep the spotlight circular despite window aspect ratio
                double ratio = this.ActualWidth / this.ActualHeight;
                if (ratio > 0)
                {
                    brush.RadiusX = 0.5 / ratio;
                    brush.RadiusY = 0.5;
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            Close();
        }
"@ -split "`r`n"

$finalLines = [System.Collections.Generic.List[string]]::new()
foreach ($l in $before190) { $finalLines.Add($l) }
foreach ($l in $newLines) { $finalLines.Add($l) }
foreach ($l in $after224) { $finalLines.Add($l) }

Set-Content $csPath $finalLines -Encoding UTF8
