$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

$oldGrid = '<Grid x:Name="PageHome" Visibility="Visible">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="*"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>

                            <!-- HEADER -->'
                            
$newGrid = '<Grid x:Name="PageHome" Visibility="Visible">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="*"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            
                            <!-- STARRY BACKGROUND CANVAS -->
                            <Canvas x:Name="StarCanvas" Grid.RowSpan="3" IsHitTestVisible="False" ClipToBounds="True" Opacity="0.8"/>

                            <!-- HEADER -->'

$xaml = $xaml.Replace($oldGrid, $newGrid)

# Also fix the remaining hardcoded glows/strokes in the dashboard
$replacements = @{
    'Stroke="#221A73E8"' = 'Stroke="{DynamicResource AccentLightBrush}"'
    'Stroke="#1A73E8"' = 'Stroke="{DynamicResource AccentBrush}"'
    '<DropShadowEffect Color="#1A73E8" BlurRadius="20" ShadowDepth="0" Opacity="0.8"/>' = '<DropShadowEffect Color="#00D2FF" BlurRadius="20" ShadowDepth="0" Opacity="0.8"/>'
    '<DropShadowEffect Color="#1A73E8" BlurRadius="10" ShadowDepth="0"/>' = '<DropShadowEffect Color="#00D2FF" BlurRadius="10" ShadowDepth="0"/>'
    'Color="#1A73E8"' = 'Color="#00D2FF"'
}

foreach ($key in $replacements.Keys) {
    $xaml = $xaml.Replace($key, $replacements[$key])
}

Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
