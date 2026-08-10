$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

$style = @"
        <!-- Neon ComboBox Style -->
        <ControlTemplate x:Key="NeonComboBoxToggleButton" TargetType="ToggleButton">
            <Border x:Name="templateRoot" Background="#0AFFFFFF" BorderBrush="#33FFFFFF" BorderThickness="1" CornerRadius="6" SnapsToDevicePixels="true">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" />
                        <ColumnDefinition Width="25" />
                    </Grid.ColumnDefinitions>
                    <Path x:Name="arrow" Grid.Column="1" Data="M0,0 L4,4 L8,0" Fill="Transparent" Stroke="#88FFFFFF" StrokeThickness="1.5" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,2,0,0"/>
                </Grid>
            </Border>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="true">
                    <Setter Property="BorderBrush" TargetName="templateRoot" Value="#00E5FF"/>
                    <Setter Property="Stroke" TargetName="arrow" Value="#00E5FF"/>
                </Trigger>
                <Trigger Property="IsChecked" Value="true">
                    <Setter Property="BorderBrush" TargetName="templateRoot" Value="#00E5FF"/>
                    <Setter Property="Background" TargetName="templateRoot" Value="#1A00E5FF"/>
                    <Setter Property="Stroke" TargetName="arrow" Value="#00E5FF"/>
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>

        <Style x:Key="NeonComboBox" TargetType="ComboBox">
            <Setter Property="Foreground" Value="White" />
            <Setter Property="Background" Value="Transparent" />
            <Setter Property="BorderBrush" Value="Transparent" />
            <Setter Property="Padding" Value="10,0,0,0" />
            <Setter Property="HorizontalContentAlignment" Value="Left" />
            <Setter Property="VerticalContentAlignment" Value="Center" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ComboBox">
                        <Grid x:Name="templateRoot" SnapsToDevicePixels="true">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="*" />
                                <ColumnDefinition MinWidth="{DynamicResource {x:Static SystemParameters.VerticalScrollBarWidthKey}}" Width="0" />
                            </Grid.ColumnDefinitions>
                            <ToggleButton x:Name="toggleButton" Grid.ColumnSpan="2" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}" Template="{StaticResource NeonComboBoxToggleButton}" />
                            <ContentPresenter x:Name="contentPresenter" IsHitTestVisible="false" Margin="{TemplateBinding Padding}" Content="{TemplateBinding SelectionBoxItem}" ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" />
                            <Popup x:Name="PART_Popup" AllowsTransparency="true" Grid.ColumnSpan="2" IsOpen="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}}" Placement="Bottom" PopupAnimation="Slide">
                                <Border x:Name="dropDownBorder" Background="#0F131C" BorderBrush="#00E5FF" BorderThickness="1" CornerRadius="6" Margin="0,5,0,0" MinWidth="{Binding ActualWidth, ElementName=templateRoot}" MaxHeight="{TemplateBinding MaxDropDownHeight}">
                                    <ScrollViewer x:Name="DropDownScrollViewer">
                                        <Grid x:Name="grid" RenderOptions.ClearTypeHint="Enabled">
                                            <Canvas x:Name="canvas" HorizontalAlignment="Left" Height="0" VerticalAlignment="Top" Width="0">
                                                <Rectangle x:Name="opaqueRect" Fill="#0F131C" Height="{Binding ActualHeight, ElementName=dropDownBorder}" Width="{Binding ActualWidth, ElementName=dropDownBorder}" />
                                            </Canvas>
                                            <ItemsPresenter x:Name="ItemsPresenter" KeyboardNavigation.DirectionalNavigation="Contained" />
                                        </Grid>
                                    </ScrollViewer>
                                </Border>
                            </Popup>
                        </Grid>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
            <Setter Property="ItemContainerStyle">
                <Setter.Value>
                    <Style TargetType="ComboBoxItem">
                        <Setter Property="Foreground" Value="#88FFFFFF"/>
                        <Setter Property="Padding" Value="10,5"/>
                        <Setter Property="Template">
                            <Setter.Value>
                                <ControlTemplate TargetType="ComboBoxItem">
                                    <Border x:Name="Bd" Background="Transparent" Padding="{TemplateBinding Padding}">
                                        <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center" />
                                    </Border>
                                    <ControlTemplate.Triggers>
                                        <Trigger Property="IsMouseOver" Value="True">
                                            <Setter TargetName="Bd" Property="Background" Value="#1A00E5FF" />
                                            <Setter Property="Foreground" Value="#00E5FF" />
                                        </Trigger>
                                        <Trigger Property="IsSelected" Value="True">
                                            <Setter TargetName="Bd" Property="Background" Value="#3300E5FF" />
                                            <Setter Property="Foreground" Value="#00E5FF" />
                                        </Trigger>
                                    </ControlTemplate.Triggers>
                                </ControlTemplate>
                            </Setter.Value>
                        </Setter>
                    </Style>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>
"@

$xaml = $xaml -replace '    </Window.Resources>', $style
$xaml = $xaml -replace '<ComboBox Grid.Column="1" x:Name="CmbLargeFileExtensions" Width="120" Height="25" Margin="0,0,15,8" VerticalAlignment="Center" SelectionChanged="CmbLargeFileExtensions_SelectionChanged">', '<ComboBox Grid.Column="1" x:Name="CmbLargeFileExtensions" Style="{StaticResource NeonComboBox}" Width="120" Height="30" Margin="0,0,15,8" VerticalAlignment="Center" SelectionChanged="CmbLargeFileExtensions_SelectionChanged">'

Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
