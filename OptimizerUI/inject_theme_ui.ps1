$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

$oldSection = @"
                                            <CheckBox x:Name="ChkSettingsAutoStart" Grid.Column="1" VerticalAlignment="Center" Margin="10,0,0,0" Checked="ChkAutoStart_Checked" Unchecked="ChkAutoStart_Unchecked">
                                                <CheckBox.LayoutTransform>
                                                    <ScaleTransform ScaleX="1.2" ScaleY="1.2"/>
                                                </CheckBox.LayoutTransform>
                                            </CheckBox>
                                        </Grid>
                                    </StackPanel>
"@

$newSection = @"
                                            <CheckBox x:Name="ChkSettingsAutoStart" Grid.Column="1" VerticalAlignment="Center" Margin="10,0,0,0" Checked="ChkAutoStart_Checked" Unchecked="ChkAutoStart_Unchecked">
                                                <CheckBox.LayoutTransform>
                                                    <ScaleTransform ScaleX="1.2" ScaleY="1.2"/>
                                                </CheckBox.LayoutTransform>
                                            </CheckBox>
                                        </Grid>
                                        
                                        <Border Height="1" Background="{DynamicResource GlassCardBorderBrush}" Margin="0,15,0,15"/>
                                        
                                        <TextBlock Text="App Theme" Foreground="{DynamicResource PrimaryTextBrush}" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,5"/>
                                        <StackPanel Orientation="Horizontal" Margin="0,0,0,15">
                                            <RadioButton x:Name="RdbThemeDark" Content="Dark Theme" Foreground="{DynamicResource PrimaryTextBrush}" Margin="0,0,15,0" IsChecked="True" Checked="RdbTheme_Checked"/>
                                            <RadioButton x:Name="RdbThemeLight" Content="Light Theme" Foreground="{DynamicResource PrimaryTextBrush}" Checked="RdbTheme_Checked"/>
                                        </StackPanel>

                                        <TextBlock Text="Glass Transparency" Foreground="{DynamicResource PrimaryTextBrush}" FontSize="14" FontWeight="SemiBold" Margin="0,0,0,5"/>
                                        <TextBlock Text="Adjust the opacity of the main window background." Foreground="{DynamicResource SecondaryTextBrush}" FontSize="11" Margin="0,0,0,10" TextWrapping="Wrap"/>
                                        <Slider x:Name="SldTransparency" Minimum="40" Maximum="100" Value="100" TickPlacement="BottomRight" TickFrequency="10" IsSnapToTickEnabled="True" Margin="0,0,0,5" ValueChanged="SldTransparency_ValueChanged"/>
                                    </StackPanel>
"@

$xaml = $xaml.Replace($oldSection, $newSection)
Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8
