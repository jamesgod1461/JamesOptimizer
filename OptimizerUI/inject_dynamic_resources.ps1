$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

# Replace hardcoded colors with DynamicResources
$replacements = @{
    'Background="#12141D"' = 'Background="{DynamicResource AppBackgroundBrush}"'
    'Background="#0F131C"' = 'Background="{DynamicResource GlassCardBackgroundBrush}"'
    'Background="#0A0D14"' = 'Background="{DynamicResource SidebarBackgroundBrush}"'
    'Background="#161B26"' = 'Background="{DynamicResource GlassCardBackgroundBrush}"'
    
    'BorderBrush="#1E2532"' = 'BorderBrush="{DynamicResource GlassCardBorderBrush}"'
    'BorderBrush="#2D3A50"' = 'BorderBrush="{DynamicResource SidebarBorderBrush}"'
    'BorderBrush="#202B3D"' = 'BorderBrush="{DynamicResource GlassCardBorderBrush}"'
    'BorderBrush="#3300D2FF"' = 'BorderBrush="{DynamicResource AccentLightBrush}"'
    'BorderBrush="#00E5FF"' = 'BorderBrush="{DynamicResource AccentBrush}"'
    
    'Foreground="White"' = 'Foreground="{DynamicResource PrimaryTextBrush}"'
    'Foreground="#FFFFFF"' = 'Foreground="{DynamicResource PrimaryTextBrush}"'
    'Foreground="#8892B0"' = 'Foreground="{DynamicResource SecondaryTextBrush}"'
    'Foreground="#88FFFFFF"' = 'Foreground="{DynamicResource SecondaryTextBrush}"'
    'Foreground="#CCCCCC"' = 'Foreground="{DynamicResource PrimaryTextBrush}"'
    'Foreground="#00D2FF"' = 'Foreground="{DynamicResource AccentBrush}"'
    
    'Background="#1AFFFFFF"' = 'Background="{DynamicResource HoverBackgroundBrush}"'
    'Background="#1A00D2FF"' = 'Background="{DynamicResource AccentLightBrush}"'
    
    'Background="#3300FF88"' = 'Background="{DynamicResource SuccessLightBrush}"'
    'Background="#33FF6B6B"' = 'Background="{DynamicResource DangerLightBrush}"'
    'Background="#3300D2FF"' = 'Background="{DynamicResource AccentLightBrush}"'
    'Background="#33FF9900"' = 'Background="{DynamicResource WarningLightBrush}"'
    'Background="#33FF3333"' = 'Background="{DynamicResource DangerLightBrush}"'
    
    'Foreground="#00FF88"' = 'Foreground="{DynamicResource SuccessBrush}"'
    'Foreground="#FF6B6B"' = 'Foreground="{DynamicResource DangerBrush}"'
    'Foreground="#FF9900"' = 'Foreground="{DynamicResource WarningBrush}"'
    'Foreground="#FF3333"' = 'Foreground="{DynamicResource DangerBrush}"'
    
    'Color="#12141D"' = 'Color="{DynamicResource AppBackgroundColor}"'
    'Color="#0A0D14"' = 'Color="{DynamicResource SidebarBackgroundColor}"'
    'Color="#0F131C"' = 'Color="{DynamicResource GlassCardBackgroundColor}"'
}

foreach ($key in $replacements.Keys) {
    $xaml = $xaml.Replace($key, $replacements[$key])
}

# The Style for Grid inside AppBackground doesn't accept DynamicResource for Color directly inside a Storyboard.
# Actually, ColorAnimations cannot target DynamicResource colors. We will need to leave them hardcoded for now, or just let them be. The user cares about the main theme toggle.
# But we can replace the SolidColorBrush color property.
$xaml = $xaml.Replace('<SolidColorBrush Color="#12141D"/>', '<SolidColorBrush Color="{DynamicResource AppBackgroundColor}"/>')
$xaml = $xaml.Replace('<SolidColorBrush Color="#2D3A50"/>', '<SolidColorBrush Color="{DynamicResource SidebarBorderColor}"/>')

Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
