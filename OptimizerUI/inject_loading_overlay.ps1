$path = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$content = Get-Content -Path $path -Raw

$loadingOverlayXaml = @"
            <!-- LOADING OVERLAY -->
            <Grid x:Name="LoadingOverlay" Grid.RowSpan="2" Visibility="Collapsed" Background="#CC000000" Panel.ZIndex="150">
                <Border Background="#15151F" BorderBrush="#3300D2FF" BorderThickness="1" CornerRadius="15" 
                        Width="400" VerticalAlignment="Center" HorizontalAlignment="Center" Padding="30">
                    <Border.Effect>
                        <DropShadowEffect BlurRadius="30" ShadowDepth="0" Color="#00D2FF" Opacity="0.2"/>
                    </Border.Effect>
                    <StackPanel>
                        <TextBlock x:Name="TxtLoadingTitle" Text="Optimizing..." FontSize="20" FontWeight="Black" Foreground="#00D2FF" HorizontalAlignment="Center" Margin="0,0,0,15"/>
                        
                        <ProgressBar x:Name="ProgressBarLoading" Minimum="0" Maximum="100" Value="0" Height="8" BorderThickness="0" Background="#1AFFFFFF" Foreground="#00D2FF" Margin="0,0,0,15">
                            <ProgressBar.Template>
                                <ControlTemplate TargetType="ProgressBar">
                                    <Grid>
                                        <Border Background="{TemplateBinding Background}" CornerRadius="4"/>
                                        <Border x:Name="PART_Indicator" Background="{TemplateBinding Foreground}" CornerRadius="4" HorizontalAlignment="Left"/>
                                    </Grid>
                                </ControlTemplate>
                            </ProgressBar.Template>
                        </ProgressBar>
                        
                        <TextBlock x:Name="TxtLoadingMessage" Text="Please wait while the operation completes." FontSize="13" Foreground="#CCCCCC" TextWrapping="Wrap" HorizontalAlignment="Center" TextAlignment="Center"/>
                    </StackPanel>
                </Border>
            </Grid>

"@

# Remove duplicate RESTART OVERLAY comments if any, then insert
$content = $content.Replace("            <!-- RESTART OVERLAY -->`r`n            <!-- RESTART OVERLAY -->", "            <!-- RESTART OVERLAY -->")
$content = $content.Replace("            <!-- RESTART OVERLAY -->", $loadingOverlayXaml + "            <!-- RESTART OVERLAY -->")

Set-Content -Path $path -Value $content
Write-Host "Injected Loading Overlay"
