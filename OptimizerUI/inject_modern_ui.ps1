$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

$styles = @"
        <!-- MODERN TOGGLE SWITCH -->
        <Style x:Key="ModernToggleSwitch" TargetType="CheckBox">
            <Setter Property="Background" Value="{DynamicResource SidebarBorderBrush}"/>
            <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="CheckBox">
                        <Grid Width="40" Height="22">
                            <Border x:Name="BackgroundBorder" Background="{TemplateBinding Background}" CornerRadius="11" BorderThickness="0">
                                <Border.Effect>
                                    <DropShadowEffect Color="Black" BlurRadius="5" ShadowDepth="1" Opacity="0.2"/>
                                </Border.Effect>
                            </Border>
                            <Ellipse x:Name="Thumb" Fill="White" Width="18" Height="18" HorizontalAlignment="Left" Margin="2,0,0,0">
                                <Ellipse.RenderTransform>
                                    <TranslateTransform x:Name="ThumbTransform" X="0"/>
                                </Ellipse.RenderTransform>
                                <Ellipse.Effect>
                                    <DropShadowEffect Color="Black" BlurRadius="3" ShadowDepth="1" Opacity="0.3"/>
                                </Ellipse.Effect>
                            </Ellipse>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="BackgroundBorder" Property="Background" Value="{DynamicResource AccentBrush}"/>
                            </Trigger>
                            <EventTrigger RoutedEvent="Checked">
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="ThumbTransform" Storyboard.TargetProperty="X" To="18" Duration="0:0:0.2">
                                            <DoubleAnimation.EasingFunction>
                                                <CubicEase EasingMode="EaseOut"/>
                                            </DoubleAnimation.EasingFunction>
                                        </DoubleAnimation>
                                    </Storyboard>
                                </BeginStoryboard>
                            </EventTrigger>
                            <EventTrigger RoutedEvent="Unchecked">
                                <BeginStoryboard>
                                    <Storyboard>
                                        <DoubleAnimation Storyboard.TargetName="ThumbTransform" Storyboard.TargetProperty="X" To="0" Duration="0:0:0.2">
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

        <!-- MODERN SLIDER -->
        <Style x:Key="ModernSlider" TargetType="Slider">
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Slider">
                        <Grid Height="20">
                            <Border Height="4" Background="{DynamicResource SidebarBorderBrush}" CornerRadius="2" VerticalAlignment="Center"/>
                            <Track x:Name="PART_Track">
                                <Track.DecreaseRepeatButton>
                                    <RepeatButton Command="Slider.DecreaseLarge">
                                        <RepeatButton.Template>
                                            <ControlTemplate TargetType="RepeatButton">
                                                <Border Height="4" Background="{DynamicResource AccentBrush}" CornerRadius="2,0,0,2"/>
                                            </ControlTemplate>
                                        </RepeatButton.Template>
                                    </RepeatButton>
                                </Track.DecreaseRepeatButton>
                                <Track.Thumb>
                                    <Thumb>
                                        <Thumb.Template>
                                            <ControlTemplate TargetType="Thumb">
                                                <Grid>
                                                    <Ellipse x:Name="ThumbEllipse" Width="14" Height="14" Fill="{DynamicResource PrimaryTextBrush}">
                                                        <Ellipse.Effect>
                                                            <DropShadowEffect Color="Black" BlurRadius="5" ShadowDepth="1" Opacity="0.5"/>
                                                        </Ellipse.Effect>
                                                    </Ellipse>
                                                </Grid>
                                                <ControlTemplate.Triggers>
                                                    <Trigger Property="IsMouseOver" Value="True">
                                                        <Setter TargetName="ThumbEllipse" Property="Width" Value="16"/>
                                                        <Setter TargetName="ThumbEllipse" Property="Height" Value="16"/>
                                                    </Trigger>
                                                    <Trigger Property="IsDragging" Value="True">
                                                        <Setter TargetName="ThumbEllipse" Property="Fill" Value="{DynamicResource AccentBrush}"/>
                                                    </Trigger>
                                                </ControlTemplate.Triggers>
                                            </ControlTemplate>
                                        </Thumb.Template>
                                    </Thumb>
                                </Track.Thumb>
                                <Track.IncreaseRepeatButton>
                                    <RepeatButton Command="Slider.IncreaseLarge">
                                        <RepeatButton.Template>
                                            <ControlTemplate TargetType="RepeatButton">
                                                <Border Background="Transparent"/>
                                            </ControlTemplate>
                                        </RepeatButton.Template>
                                    </RepeatButton>
                                </Track.IncreaseRepeatButton>
                            </Track>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <!-- SEGMENTED RADIO BUTTON -->
        <Style x:Key="SegmentedRadioButton" TargetType="RadioButton">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="{DynamicResource SecondaryTextBrush}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource GlassCardBorderBrush}"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="RadioButton">
                        <Border x:Name="Bg" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="15" Padding="15,6">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource AccentLightBrush}"/>
                                <Setter TargetName="Bg" Property="BorderBrush" Value="{DynamicResource AccentBrush}"/>
                                <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
                            </Trigger>
                            <MultiTrigger>
                                <MultiTrigger.Conditions>
                                    <Condition Property="IsChecked" Value="False"/>
                                    <Condition Property="IsMouseOver" Value="True"/>
                                </MultiTrigger.Conditions>
                                <Setter TargetName="Bg" Property="Background" Value="{DynamicResource TitleBarHoverBrush}"/>
                                <Setter Property="Foreground" Value="{DynamicResource PrimaryTextBrush}"/>
                            </MultiTrigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
"@

$insertIndex = $xaml.IndexOf("    </Window.Resources>")
if ($insertIndex -gt 0) {
    $xaml = $xaml.Insert($insertIndex, $styles + "`n")
}

# Update Settings UI elements
$xaml = $xaml.Replace('<CheckBox x:Name="ChkSettingsAutoStart"', '<CheckBox x:Name="ChkSettingsAutoStart" Style="{StaticResource ModernToggleSwitch}"')
# Remove the old LayoutTransform since we don't want to scale the custom switch
$xaml = $xaml.Replace('<ScaleTransform ScaleX="1.2" ScaleY="1.2"/>', '')
$xaml = $xaml.Replace('<CheckBox.LayoutTransform>', '<!-- Removed -->')
$xaml = $xaml.Replace('</CheckBox.LayoutTransform>', '<!-- Removed -->')

$xaml = $xaml.Replace('<RadioButton x:Name="RdbThemeDark"', '<RadioButton x:Name="RdbThemeDark" Style="{StaticResource SegmentedRadioButton}"')
$xaml = $xaml.Replace('<RadioButton x:Name="RdbThemeLight"', '<RadioButton x:Name="RdbThemeLight" Style="{StaticResource SegmentedRadioButton}"')

$xaml = $xaml.Replace('<Slider x:Name="SldTransparency"', '<Slider x:Name="SldTransparency" Style="{StaticResource ModernSlider}"')

Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
