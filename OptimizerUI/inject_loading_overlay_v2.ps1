$path = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$content = Get-Content -Path $path -Raw

# Remove the old LoadingOverlay
$content = $content -replace '(?s)            <!-- LOADING OVERLAY -->.*?            <!-- RESTART OVERLAY -->', "            <!-- RESTART OVERLAY -->"

$loadingOverlayXaml = @"
            <!-- LOADING OVERLAY -->
            <Grid x:Name="LoadingOverlay" Grid.RowSpan="2" Visibility="Collapsed" Background="#D8000000" Panel.ZIndex="150">
                <Border Background="#101018" BorderBrush="#2200D2FF" BorderThickness="1" CornerRadius="12" 
                        Width="400" VerticalAlignment="Center" HorizontalAlignment="Center" Padding="30">
                    <StackPanel>
                        <TextBlock x:Name="TxtLoadingTitle" Text="Optimizing..." FontSize="22" FontWeight="Bold" Foreground="#00D2FF" HorizontalAlignment="Center" Margin="0,0,0,20"/>
                        
                        <ProgressBar x:Name="ProgressBarLoading" Minimum="0" Maximum="100" Value="0" Height="10" BorderThickness="0" Background="#22FFFFFF" Margin="0,0,0,20">
                            <ProgressBar.Foreground>
                                <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                                    <GradientStop Color="#007ACC" Offset="0"/>
                                    <GradientStop Color="#00D2FF" Offset="1"/>
                                </LinearGradientBrush>
                            </ProgressBar.Foreground>
                            <ProgressBar.Template>
                                <ControlTemplate TargetType="ProgressBar">
                                    <Grid x:Name="PART_Track">
                                        <Border Background="{TemplateBinding Background}" CornerRadius="5"/>
                                        <Border x:Name="PART_Indicator" Background="{TemplateBinding Foreground}" CornerRadius="5" HorizontalAlignment="Left">
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
                                    </Grid>
                                </ControlTemplate>
                            </ProgressBar.Template>
                        </ProgressBar>
                        
                        <TextBlock x:Name="TxtLoadingMessage" Text="Please wait while the operation completes." FontSize="14" Foreground="#AAAAAA" TextWrapping="Wrap" HorizontalAlignment="Center" TextAlignment="Center"/>
                    </StackPanel>
                </Border>
            </Grid>
"@

$content = $content.Replace("            <!-- RESTART OVERLAY -->", $loadingOverlayXaml + "`r`n            <!-- RESTART OVERLAY -->")

Set-Content -Path $path -Value $content
Write-Host "Injected Loading Overlay V2"
