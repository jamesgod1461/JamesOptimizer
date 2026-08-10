$path = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$content = Get-Content -Path $path -Raw

# The corrupted part is around line 1650:
#                                     </ControlTemplate>
#                         <DropShadowEffect BlurRadius="30" ShadowDepth="0" Color="#00D2FF" Opacity="0.3"/>

# Let's use string replacement to fix the entire block starting from the YesBtnBorder
$badBlock = @"
                                    </ControlTemplate>
                        <DropShadowEffect BlurRadius="30" ShadowDepth="0" Color="#00D2FF" Opacity="0.3"/>
"@

$goodBlock = @"
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
                        <DropShadowEffect BlurRadius="30" ShadowDepth="0" Color="#00D2FF" Opacity="0.3"/>
"@

$content = $content.Replace($badBlock, $goodBlock)

# Also fix RestartOverlay
$content = $content.Replace('<Grid x:Name="RestartOverlay" Visibility="Collapsed" Background="#CC000000" Panel.ZIndex="200">', '<Grid x:Name="RestartOverlay" Grid.RowSpan="2" Visibility="Collapsed" Background="#CC000000" Panel.ZIndex="200">')

Set-Content -Path $path -Value $content
Write-Host "Fixed overlays in MainWindow.xaml"
