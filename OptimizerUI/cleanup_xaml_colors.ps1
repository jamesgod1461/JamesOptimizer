$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

$replacements = @{
    'Background="#090A0F"' = 'Background="{DynamicResource AppBackgroundBrush}"'
    'BorderBrush="#1C2133"' = 'BorderBrush="{DynamicResource SidebarBorderBrush}"'
    
    'Background="#0D111A"' = 'Background="{DynamicResource GlassCardBackgroundBrush}"'
    'Background="#151A25"' = 'Background="{DynamicResource HoverBackgroundBrush}"'
    'Background="#2A0000"' = 'Background="{DynamicResource DangerLightBrush}"'
    
    'Background="#1A73E8"' = 'Background="{DynamicResource AccentBrush}"'
    'Foreground="#1A73E8"' = 'Foreground="{DynamicResource AccentBrush}"'
    'BorderBrush="#1A73E8"' = 'BorderBrush="{DynamicResource AccentBrush}"'
    'Fill="#1A73E8"' = 'Fill="{DynamicResource AccentBrush}"'
    
    'Background="#221A73E8"' = 'Background="{DynamicResource AccentLightBrush}"'
    
    'Background="#0AFFFFFF"' = 'Background="{DynamicResource HoverBackgroundBrush}"'
    'BorderBrush="#1AFFFFFF"' = 'BorderBrush="{DynamicResource ButtonBorderBrush}"'
    'BorderBrush="#33FFFFFF"' = 'BorderBrush="{DynamicResource ButtonBorderBrush}"'
    'Background="#33FFFFFF"' = 'Background="{DynamicResource HoverBackgroundBrush}"'
    
    'Background="#1100FF88"' = 'Background="{DynamicResource SuccessLightBrush}"'
    'BorderBrush="#3300FF88"' = 'BorderBrush="{DynamicResource SuccessBrush}"'
    
    'BorderBrush="#FF6B6B"' = 'BorderBrush="{DynamicResource DangerBrush}"'
}

foreach ($key in $replacements.Keys) {
    $xaml = $xaml.Replace($key, $replacements[$key])
}

# Update Slider style to be more minimal
$oldSliderHeight = 'Height="4" Background="{DynamicResource SidebarBorderBrush}" CornerRadius="2"'
$newSliderHeight = 'Height="2" Background="{DynamicResource SidebarBorderBrush}" CornerRadius="1"'
$xaml = $xaml.Replace($oldSliderHeight, $newSliderHeight)

$oldSliderThumb = '<Ellipse x:Name="ThumbEllipse" Width="14" Height="14" Fill="{DynamicResource PrimaryTextBrush}">'
$newSliderThumb = '<Ellipse x:Name="ThumbEllipse" Width="10" Height="10" Fill="{DynamicResource PrimaryTextBrush}">'
$xaml = $xaml.Replace($oldSliderThumb, $newSliderThumb)

$oldSliderHover = '<Setter TargetName="ThumbEllipse" Property="Width" Value="16"/>'
$newSliderHover = '<Setter TargetName="ThumbEllipse" Property="Width" Value="12"/>'
$xaml = $xaml.Replace($oldSliderHover, $newSliderHover)

$oldSliderHover2 = '<Setter TargetName="ThumbEllipse" Property="Height" Value="16"/>'
$newSliderHover2 = '<Setter TargetName="ThumbEllipse" Property="Height" Value="12"/>'
$xaml = $xaml.Replace($oldSliderHover2, $newSliderHover2)

Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
