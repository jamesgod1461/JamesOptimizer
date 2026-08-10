$path = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$content = Get-Content -Path $path -Raw

# Replace the ProgressBar Loading with the foolproof Border implementation
$oldProgressBar = '(?s)<ProgressBar x:Name="ProgressBarLoading".*?</ProgressBar>'
$newProgressBar = @"
                        <Border x:Name="ProgressBarTrack" Height="10" Background="#22FFFFFF" CornerRadius="5" Margin="0,0,0,20" HorizontalAlignment="Stretch">
                            <Border x:Name="ProgressBarIndicator" CornerRadius="5" HorizontalAlignment="Left" Width="0">
                                <Border.Background>
                                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                        <GradientStop Color="#007ACC" Offset="0"/>
                                        <GradientStop Color="#00D2FF" Offset="1"/>
                                    </LinearGradientBrush>
                                </Border.Background>
                                <Border.Triggers>
                                    <EventTrigger RoutedEvent="FrameworkElement.Loaded">
                                        <BeginStoryboard>
                                            <Storyboard>
                                                <DoubleAnimation Storyboard.TargetProperty="Opacity" From="0.6" To="1.0" Duration="0:0:0.8" AutoReverse="True" RepeatBehavior="Forever" />
                                            </Storyboard>
                                        </BeginStoryboard>
                                    </EventTrigger>
                                </Border.Triggers>
                            </Border>
                        </Border>
"@
$content = $content -replace $oldProgressBar, $newProgressBar

Set-Content -Path $path -Value $content

$csPath = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$csContent = Get-Content -Path $csPath -Raw

# Update OnProgress
$oldOnProgress = '(?s)OptimizationEngine\.OnProgress \+= \(percent, message\) =>.*?ProgressBarLoading\.Value = percent;.*?TxtLoadingMessage\.Text = message;.*?}\);\s*};'
$newOnProgress = @"
            OptimizationEngine.OnProgress += (percent, message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    double totalWidth = ProgressBarTrack.ActualWidth;
                    if (totalWidth <= 0) totalWidth = 340; // Fallback width for the padded container

                    double targetWidth = (totalWidth * Math.Min(100, Math.Max(0, percent))) / 100.0;
                    
                    var da = new System.Windows.Media.Animation.DoubleAnimation
                    {
                        To = targetWidth,
                        Duration = TimeSpan.FromMilliseconds(400),
                        EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                    };
                    ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, da);

                    TxtLoadingMessage.Text = message;
                });
            };
"@
$csContent = $csContent -replace $oldOnProgress, $newOnProgress

# Update ExecuteTool
$csContent = $csContent -replace 'ProgressBarLoading\.Value = 0;', 'ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null); ProgressBarIndicator.Width = 0;'

# Update BtnRunDiagnostics_Click
$csContent = $csContent -replace 'ProgressBarLoading\.Value = 0;', 'ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null); ProgressBarIndicator.Width = 0;'

# Update BtnDeepCleanupProceed_Click
$csContent = $csContent -replace 'ProgressBarLoading\.Value = 0;', 'ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null); ProgressBarIndicator.Width = 0;'

# Update BtnToolBackup_Click
$csContent = $csContent -replace 'ProgressBarLoading\.Value = 0;', 'ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null); ProgressBarIndicator.Width = 0;'

# Update BtnToolDriverCheck_Click
$csContent = $csContent -replace 'ProgressBarLoading\.Value = 0;', 'ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null); ProgressBarIndicator.Width = 0;'

# Update BtnDeepCleanupScan_Click
$csContent = $csContent -replace 'ProgressBarLoading\.Value = 0;', 'ProgressBarIndicator.BeginAnimation(System.Windows.FrameworkElement.WidthProperty, null); ProgressBarIndicator.Width = 0;'

Set-Content -Path $csPath -Value $csContent
Write-Host "Injected Foolproof Progress Bar"
