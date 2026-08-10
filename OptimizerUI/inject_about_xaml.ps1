$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

$startIndex = $xaml.IndexOf('<Grid x:Name="PageMouse"')
$endIndex = $xaml.IndexOf('<!-- PAGE 3: CLEANUPS -->')

if ($startIndex -gt -1 -and $endIndex -gt $startIndex) {
    $oldPage = $xaml.Substring($startIndex, $endIndex - $startIndex)
    
    $newPage = @"
                        <Grid x:Name="PageMouse" Visibility="Collapsed" VerticalAlignment="Top" Margin="0,0,20,20">
                            <Grid.RowDefinitions>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                                <RowDefinition Height="Auto"/>
                            </Grid.RowDefinitions>
                            
                            <TextBlock Grid.Row="0" Text="&#x2139;&#xFE0F; ABOUT &amp; SYSTEM INFO" FontSize="20" FontWeight="Black" Foreground="#00D2FF" Margin="0,0,0,20"/>
                            
                            <Grid Grid.Row="1" Margin="0,0,0,20">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>
                                
                                <Border Grid.Column="0" Style="{StaticResource GlassCard}" Padding="20" Margin="0,0,10,0" VerticalAlignment="Top">
                                    <StackPanel>
                                        <TextBlock Text="PC Specifications" Foreground="White" FontSize="16" FontWeight="Bold" Margin="0,0,0,15"/>
                                        
                                        <Grid Margin="0,0,0,10">
                                            <Grid.ColumnDefinitions><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                            <TextBlock Text="Processor:" Foreground="#8892B0" FontSize="12"/>
                                            <TextBlock x:Name="TxtInfoCpu" Grid.Column="1" Text="Loading..." Foreground="White" FontSize="12" TextWrapping="Wrap"/>
                                        </Grid>
                                        <Grid Margin="0,0,0,10">
                                            <Grid.ColumnDefinitions><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                            <TextBlock Text="GPU:" Foreground="#8892B0" FontSize="12"/>
                                            <TextBlock x:Name="TxtInfoGpu" Grid.Column="1" Text="Loading..." Foreground="White" FontSize="12" TextWrapping="Wrap"/>
                                        </Grid>
                                        <Grid Margin="0,0,0,10">
                                            <Grid.ColumnDefinitions><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                            <TextBlock Text="RAM:" Foreground="#8892B0" FontSize="12"/>
                                            <TextBlock x:Name="TxtInfoRam" Grid.Column="1" Text="Loading..." Foreground="White" FontSize="12" TextWrapping="Wrap"/>
                                        </Grid>
                                        <Grid Margin="0,0,0,10">
                                            <Grid.ColumnDefinitions><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                            <TextBlock Text="Storage Types:" Foreground="#8892B0" FontSize="12"/>
                                            <TextBlock x:Name="TxtInfoStorage" Grid.Column="1" Text="Loading..." Foreground="White" FontSize="12" TextWrapping="Wrap"/>
                                        </Grid>
                                    </StackPanel>
                                </Border>
                                
                                <Border Grid.Column="1" Style="{StaticResource GlassCard}" Padding="20" Margin="10,0,0,0" VerticalAlignment="Top">
                                    <StackPanel>
                                        <TextBlock Text="Network Info" Foreground="White" FontSize="16" FontWeight="Bold" Margin="0,0,0,15"/>
                                        <Grid Margin="0,0,0,10">
                                            <Grid.ColumnDefinitions><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                            <TextBlock Text="Adapter:" Foreground="#8892B0" FontSize="12"/>
                                            <TextBlock x:Name="TxtInfoNetwork" Grid.Column="1" Text="Loading..." Foreground="White" FontSize="12" TextWrapping="Wrap"/>
                                        </Grid>
                                        <Grid Margin="0,0,0,10">
                                            <Grid.ColumnDefinitions><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                                            <TextBlock Text="Latency (Ping):" Foreground="#8892B0" FontSize="12"/>
                                            <TextBlock x:Name="TxtInfoPing" Grid.Column="1" Text="Loading..." Foreground="White" FontSize="12" TextWrapping="Wrap"/>
                                        </Grid>
                                    </StackPanel>
                                </Border>
                            </Grid>

                            <Grid Grid.Row="2">
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="*"/>
                                    <ColumnDefinition Width="*"/>
                                </Grid.ColumnDefinitions>

                                <Border Grid.Column="0" Style="{StaticResource GlassCard}" Padding="20" Margin="0,0,10,0" VerticalAlignment="Top">
                                    <StackPanel>
                                        <TextBlock Text="Developer Info" Foreground="White" FontSize="16" FontWeight="Bold" Margin="0,0,0,15"/>
                                        <TextBlock Text="James Optimizer v2.0.0" Foreground="#00D2FF" FontSize="14" FontWeight="Bold" Margin="0,0,0,5"/>
                                        <TextBlock Text="Developed by James God" Foreground="White" FontSize="12" Margin="0,0,0,10"/>
                                        <TextBlock Text="Dedicated to building the absolute fastest and most reliable Windows tweaking utility for gamers and power users." Foreground="#8892B0" FontSize="11" TextWrapping="Wrap" LineHeight="18"/>
                                    </StackPanel>
                                </Border>

                                <Border Grid.Column="1" Style="{StaticResource GlassCard}" Padding="20" Margin="10,0,0,0" VerticalAlignment="Top">
                                    <StackPanel>
                                        <TextBlock Text="System Settings" Foreground="White" FontSize="16" FontWeight="Bold" Margin="0,0,0,15"/>
                                        <Grid>
                                            <Grid.ColumnDefinitions>
                                                <ColumnDefinition Width="*"/>
                                                <ColumnDefinition Width="Auto"/>
                                            </Grid.ColumnDefinitions>
                                            <StackPanel Grid.Column="0" VerticalAlignment="Center">
                                                <TextBlock Text="Automatic Start-up" Foreground="White" FontSize="14" FontWeight="SemiBold"/>
                                                <TextBlock Text="Run James Optimizer silently in the background on boot." Foreground="#8892B0" FontSize="11" Margin="0,2,0,0" TextWrapping="Wrap"/>
                                            </StackPanel>
                                            <CheckBox x:Name="ChkSettingsAutoStart" Grid.Column="1" VerticalAlignment="Center" Margin="10,0,0,0" Checked="ChkAutoStart_Checked" Unchecked="ChkAutoStart_Unchecked">
                                                <CheckBox.LayoutTransform>
                                                    <ScaleTransform ScaleX="1.2" ScaleY="1.2"/>
                                                </CheckBox.LayoutTransform>
                                            </CheckBox>
                                        </Grid>
                                    </StackPanel>
                                </Border>
                            </Grid>
                        </Grid>
                        
"@
    $xaml = $xaml.Replace($oldPage, $newPage)
    Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
}
