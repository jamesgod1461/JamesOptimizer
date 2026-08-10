$csPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$content = Get-Content $csPath -Raw

$oldCode = @"
        private void BtnClose_Click(object sender, RoutedEventArgs e)
"@
$newCode = @"
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (SpotlightOverlay?.Fill is RadialGradientBrush brush)
            {
                Point p = e.GetPosition(this);
                double x = p.X / this.ActualWidth;
                double y = p.Y / this.ActualHeight;
                brush.Center = new Point(x, y);
                brush.GradientOrigin = new Point(x, y);
                
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
"@
$content = $content.Replace($oldCode, $newCode)

Set-Content $csPath $content -Encoding UTF8
