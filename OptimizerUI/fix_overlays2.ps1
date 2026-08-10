$path = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$content = Get-Content -Path $path -Raw

$pattern = '(?s)YesBtnBorder.*?Color="#3300FF88"/>\s*</Border\.Background>\s*<ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>\s*</Border>\s*</ControlTemplate>\s*<DropShadowEffect BlurRadius="30" ShadowDepth="0" Color="#00D2FF"'

$replacement = @"
YesBtnBorder" CornerRadius="8" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                                            <Border.Background>
                                                <SolidColorBrush Color="#3300FF88"/>
                                            </Border.Background>
                                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                                        </Border>
                                    </ControlTemplate>
                                </Button.Template>
                            </Button>
                        </Grid>
                    </StackPanel>
                </Border>
            </Grid>

            <!-- FEEDBACK OVERLAY -->
            <Grid x:Name="FeedbackOverlay" Grid.RowSpan="2" Visibility="Collapsed" Background="#B2000000" Panel.ZIndex="100">
                <Border x:Name="FeedbackCard" Background="#15151F" BorderBrush="#3300D2FF" BorderThickness="1" CornerRadius="15" 
                        Width="350" VerticalAlignment="Center" HorizontalAlignment="Center" Padding="30">
                    <Border.RenderTransform>
                        <ScaleTransform x:Name="FeedbackCardScale" ScaleX="0.8" ScaleY="0.8" CenterX="175" CenterY="100"/>
                    </Border.RenderTransform>
                    <Border.Effect>
                        <DropShadowEffect BlurRadius="30" ShadowDepth="0" Color="#00D2FF"
"@

$newContent = [regex]::Replace($content, $pattern, $replacement)

Set-Content -Path $path -Value $newContent
Write-Host "Fixed overlays in MainWindow.xaml"
