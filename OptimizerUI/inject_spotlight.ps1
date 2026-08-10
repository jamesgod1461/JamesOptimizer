$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$content = Get-Content $xamlPath -Raw

# 1. Update Window tag to hook MouseMove
$oldWindowTag = @"
        local:SmoothScrollBehavior.IsEnabled="True"
        Icon="pack://application:,,,/app_icon.ico">
"@
$newWindowTag = @"
        local:SmoothScrollBehavior.IsEnabled="True"
        MouseMove="Window_MouseMove"
        Icon="pack://application:,,,/app_icon.ico">
"@
$content = $content.Replace($oldWindowTag, $newWindowTag)

# 2. Add CursorSpotlightBrush to Window.Resources
$oldResources = @"
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
"@
$newResources = @"
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="BoolToVis"/>
        
        <!-- Neon Spotlight Brush -->
        <RadialGradientBrush x:Key="CursorSpotlightBrush" Center="0.5,0.5" GradientOrigin="0.5,0.5" RadiusX="0.5" RadiusY="0.5">
            <GradientStop Color="#1A00E5FF" Offset="0"/>
            <GradientStop Color="#00090A0F" Offset="1"/>
        </RadialGradientBrush>
"@
$content = $content.Replace($oldResources, $newResources)

# 3. Update the Main Container Border and add Spotlight Rectangle
$oldRootGrid = @"
    <!-- v2.0.0 Main Container -->
    <Border Margin="10" CornerRadius="10" ClipToBounds="True" Background="#080A10" BorderBrush="#202B3D" BorderThickness="1">
        <Grid x:Name="RootGrid">
"@
$newRootGrid = @"
    <!-- v2.0.0 Main Container -->
    <Border Margin="10" CornerRadius="10" ClipToBounds="True" Background="#090A0F" BorderBrush="#1C2133" BorderThickness="1">
        <Grid x:Name="RootGrid">
            <!-- Global Spotlight Rectangle -->
            <Rectangle x:Name="SpotlightOverlay" IsHitTestVisible="False" Fill="{StaticResource CursorSpotlightBrush}" Grid.RowSpan="2"/>
"@
$content = $content.Replace($oldRootGrid, $newRootGrid)

# 4. Update GlassCard to be Neon Dark
$oldGlassCard = @"
        <!-- Cyberpunk Glassmorphism Card Style -->
        <Style x:Key="GlassCard" TargetType="Border">
            <Setter Property="CornerRadius" Value="12"/>
            <Setter Property="BorderThickness" Value="1.5"/>
            <Setter Property="Background">
                <Setter.Value>
                    <SolidColorBrush Color="#2A0B2E"/>
                </Setter.Value>
            </Setter>
            <Setter Property="BorderBrush">
                <Setter.Value>
                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                        <GradientStop Color="#FF00F0FF" Offset="0"/>
                        <GradientStop Color="#FFFF0055" Offset="1"/>
                    </LinearGradientBrush>
                </Setter.Value>
            </Setter>
            <Setter Property="Effect">
                <Setter.Value>
                    <DropShadowEffect BlurRadius="15" ShadowDepth="4" Opacity="0.4" Color="#000000"/>
                </Setter.Value>
            </Setter>
        </Style>
"@
$newGlassCard = @"
        <!-- Cyberpunk Glassmorphism Card Style -->
        <Style x:Key="GlassCard" TargetType="Border">
            <Setter Property="CornerRadius" Value="12"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Background">
                <Setter.Value>
                    <SolidColorBrush Color="#12141D"/>
                </Setter.Value>
            </Setter>
            <Setter Property="BorderBrush">
                <Setter.Value>
                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                        <GradientStop Color="#6600E5FF" Offset="0"/>
                        <GradientStop Color="#66B500FF" Offset="1"/>
                    </LinearGradientBrush>
                </Setter.Value>
            </Setter>
            <Setter Property="Effect">
                <Setter.Value>
                    <DropShadowEffect BlurRadius="20" ShadowDepth="5" Opacity="0.6" Color="#05060A"/>
                </Setter.Value>
            </Setter>
        </Style>
"@
$content = $content.Replace($oldGlassCard, $newGlassCard)

Set-Content $xamlPath $content -Encoding UTF8
