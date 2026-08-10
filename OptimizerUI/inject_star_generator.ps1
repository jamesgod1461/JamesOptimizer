$csPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$cs = Get-Content $csPath -Raw

$initComponent = "InitializeComponent();"
$newInitComponent = "InitializeComponent();`n            Loaded += (s, e) => GenerateStars();"
$cs = $cs.Replace($initComponent, $newInitComponent)

$method = @"
        private void GenerateStars()
        {
            if (StarCanvas == null) return;
            StarCanvas.Children.Clear();
            
            Random rand = new Random();
            int starCount = 80;
            
            for (int i = 0; i < starCount; i++)
            {
                double size = rand.NextDouble() * 3 + 1; // 1 to 4 px
                
                Ellipse star = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(rand.Next(100, 200)), 255, 255, 255)),
                };
                
                Canvas.SetLeft(star, rand.NextDouble() * 1500);
                Canvas.SetTop(star, rand.NextDouble() * 1000);
                
                if (rand.Next(3) == 0) // 1 in 3 stars twinkle
                {
                    DoubleAnimation twinkle = new DoubleAnimation
                    {
                        From = star.Opacity,
                        To = rand.NextDouble() * 0.3,
                        Duration = TimeSpan.FromSeconds(rand.NextDouble() * 2 + 1),
                        AutoReverse = true,
                        RepeatBehavior = RepeatBehavior.Forever
                    };
                    star.BeginAnimation(UIElement.OpacityProperty, twinkle);
                }
                
                StarCanvas.Children.Add(star);
            }
        }
"@

$insertIndex = $cs.LastIndexOf("    }")
if ($insertIndex -gt 0) {
    $cs = $cs.Insert($insertIndex, $method + "`n")
}

Set-Content -Path $csPath -Value $cs -Encoding UTF8
