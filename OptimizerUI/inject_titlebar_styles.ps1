$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

# 1. Inject Style
$style = @"
        <Style x:Key="TitleBarButton" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource SecondaryTextBrush}"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Bg" Background="{TemplateBinding Background}" CornerRadius="5">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" Margin="5"/>
                            <Border.RenderTransform>
                                <RotateTransform x:Name="IconRotation" Angle="0" CenterX="15" CenterY="15"/>
                            </Border.RenderTransform>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource TitleBarHoverBrush}"/>
                                <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
                            </Trigger>
                            <EventTrigger RoutedEvent="MouseEnter">
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="IconRotation" Storyboard.TargetProperty="Angle" To="90" Duration="0:0:0.3">
                                            <DoubleAnimation.EasingFunction>
                                                <CubicEase EasingMode="EaseOut"/>
                                            </DoubleAnimation.EasingFunction>
                                        </DoubleAnimation>
                                    </Storyboard>
                                </BeginStoryboard>
                            </EventTrigger>
                            <EventTrigger RoutedEvent="MouseLeave">
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="IconRotation" Storyboard.TargetProperty="Angle" To="0" Duration="0:0:0.3">
                                            <DoubleAnimation.EasingFunction>
                                                <CubicEase EasingMode="EaseOut"/>
                                            </DoubleAnimation.EasingFunction>
                                        </DoubleAnimation>
                                    </Storyboard>
                                </BeginStoryboard>
                            </EventTrigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="TitleBarWindowButton" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource SecondaryTextBrush}"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="Bg" Background="{TemplateBinding Background}" CornerRadius="0">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource TitleBarHoverBrush}"/>
                                <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
"@

$insertIndex = $xaml.IndexOf("    </Window.Resources>")
if ($insertIndex -gt 0) {
    $xaml = $xaml.Insert($insertIndex, $style + "`n")
}

# 2. Update Settings Button
$oldBtnSettings = '<Button x:Name="BtnTopSettings" Content="&#x2699;" Background="Transparent" Foreground="{DynamicResource SecondaryTextBrush}" BorderThickness="0" Cursor="Hand" FontSize="16" Click="BtnTopSettings_Click" Margin="0,0,10,0" ToolTip="Settings"/>'
$newBtnSettings = '<Button x:Name="BtnTopSettings" Style="{StaticResource TitleBarButton}" Content="&#x2699;" Width="30" Height="30" FontSize="16" Click="BtnTopSettings_Click" Margin="0,0,10,0" ToolTip="Settings"/>'
$xaml = $xaml.Replace($oldBtnSettings, $newBtnSettings)

# 3. Update Minimize/Maximize/Close Buttons
$oldMin = '<Button Content="&#x2014;" Width="45" Height="35" Background="Transparent" Foreground="{DynamicResource SecondaryTextBrush}" BorderThickness="0" Click="BtnMinimize_Click" Cursor="Hand" FontSize="14"/>'
$newMin = '<Button Style="{StaticResource TitleBarWindowButton}" Content="&#x2014;" Width="45" Height="35" Click="BtnMinimize_Click" FontSize="14"/>'
$xaml = $xaml.Replace($oldMin, $newMin)

$oldMax = '<Button Content="&#x25A1;" Width="45" Height="35" Background="Transparent" Foreground="{DynamicResource SecondaryTextBrush}" BorderThickness="0" Cursor="Hand" FontSize="14"/>'
$newMax = '<Button Style="{StaticResource TitleBarWindowButton}" Content="&#x25A1;" Width="45" Height="35" FontSize="14"/>'
$xaml = $xaml.Replace($oldMax, $newMax)

$oldClose = '<Button Content="&#x2715;" Width="45" Height="35" Background="Transparent" Foreground="{DynamicResource SecondaryTextBrush}" BorderThickness="0" Click="BtnClose_Click" Cursor="Hand" FontSize="14"/>'
$newClose = '<Button Style="{StaticResource TitleBarWindowButton}" Content="&#x2715;" Width="45" Height="35" Click="BtnClose_Click" FontSize="14"/>'
$xaml = $xaml.Replace($oldClose, $newClose)

Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
