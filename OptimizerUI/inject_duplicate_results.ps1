$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$content = Get-Content $xamlPath -Raw

$replacement = @"
                                        <TextBlock Grid.Column="1" x:Name="TxtDuplicateFilesStatus" Text="Found 45 groups of duplicates." Foreground="#88FFFFFF" FontSize="14" VerticalAlignment="Center" HorizontalAlignment="Right"/>
                                    </Grid>
                                    
                                    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled" PanningMode="VerticalOnly" Margin="0,0,0,20">
                                        <ItemsControl x:Name="ListDuplicateFiles" Background="Transparent" BorderThickness="0">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <Border Style="{StaticResource GlassCard}" Margin="0,0,10,15" Padding="15" Opacity="0">
                                                        <Border.RenderTransform>
                                                            <TranslateTransform Y="20"/>
                                                        </Border.RenderTransform>
                                                        <Border.Triggers>
                                                            <EventTrigger RoutedEvent="Loaded">
                                                                <BeginStoryboard>
                                                                    <Storyboard>
                                                                        <DoubleAnimation Storyboard.TargetProperty="Opacity" From="0" To="1" Duration="0:0:0.4" />
                                                                        <DoubleAnimation Storyboard.TargetProperty="(UIElement.RenderTransform).(TranslateTransform.Y)" From="20" To="0" Duration="0:0:0.4">
                                                                            <DoubleAnimation.EasingFunction>
                                                                                <QuadraticEase EasingMode="EaseOut"/>
                                                                            </DoubleAnimation.EasingFunction>
                                                                        </DoubleAnimation>
                                                                    </Storyboard>
                                                                </BeginStoryboard>
                                                            </EventTrigger>
                                                        </Border.Triggers>
                                                        <StackPanel>
                                                            <StackPanel Orientation="Horizontal" Margin="0,0,0,10">
                                                                <TextBlock Text="&#x1F4C1;" Foreground="#00D2FF" FontSize="16" Margin="0,0,8,0" VerticalAlignment="Center"/>
                                                                <TextBlock Text="{Binding GroupTitle}" Foreground="#00D2FF" FontWeight="Bold" FontSize="14" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" MaxWidth="450"/>
                                                            </StackPanel>
                                                            <Rectangle Height="1" Fill="#22FFFFFF" Margin="0,0,0,10"/>
                                                            <ItemsControl ItemsSource="{Binding Files}">
                                                                <ItemsControl.ItemTemplate>
                                                                    <DataTemplate>
                                                                        <Grid Margin="0,5">
                                                                            <Grid.ColumnDefinitions>
                                                                                <ColumnDefinition Width="Auto"/>
                                                                                <ColumnDefinition Width="*"/>
                                                                            </Grid.ColumnDefinitions>
                                                                            <CheckBox Grid.Column="0" IsChecked="{Binding IsSelected}" Margin="0,0,15,0" VerticalAlignment="Center">
                                                                                <CheckBox.LayoutTransform>
                                                                                    <ScaleTransform ScaleX="1.2" ScaleY="1.2"/>
                                                                                </CheckBox.LayoutTransform>
                                                                            </CheckBox>
                                                                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                                                                <TextBlock Text="{Binding File.FileName}" Foreground="White" FontWeight="SemiBold" TextTrimming="CharacterEllipsis"/>
                                                                                <TextBlock Text="{Binding File.FilePath}" Foreground="#88FFFFFF" FontSize="11" TextTrimming="CharacterEllipsis" Margin="0,2,0,0"/>
                                                                            </StackPanel>
                                                                        </Grid>
                                                                    </DataTemplate>
                                                                </ItemsControl.ItemTemplate>
                                                            </ItemsControl>
                                                        </StackPanel>
                                                    </Border>
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </ScrollViewer>
                                </Grid>
                            </Grid>
                            
                            <!-- TIPS SIDEBAR -->
                            <StackPanel Grid.Column="1" Margin="10,45,0,0">
                                <!-- Status Card -->
                                <Border Style="{StaticResource GlassCard}" Padding="20" Margin="0,0,0,20" Background="#1A00D2FF" BorderBrush="#3300D2FF" BorderThickness="1">
                                    <StackPanel>
                                        <TextBlock Text="&#x1F46F;" FontSize="24" HorizontalAlignment="Center" Margin="0,0,0,10"/>
                                        <TextBlock Text="Duplicate Finder" Foreground="White" FontSize="16" FontWeight="Bold" HorizontalAlignment="Center" Margin="0,0,0,5"/>
                                        <TextBlock Text="We use secure file hashing to guarantee these files are identical byte-for-byte." Foreground="#88FFFFFF" FontSize="12" TextWrapping="Wrap" TextAlignment="Center" LineHeight="18"/>
                                    </StackPanel>
                                </Border>
                                
                                <Border Style="{StaticResource GlassCard}" Padding="15" Margin="0,0,0,12" BorderBrush="#22FFFFFF" BorderThickness="1">
                                    <StackPanel>
                                        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                            <TextBlock Text="&#x1F4A1;" FontSize="14" Margin="0,0,8,0" VerticalAlignment="Center"/>
                                            <TextBlock Text="Keep One Copy" Foreground="#00D2FF" FontSize="14" FontWeight="Bold" VerticalAlignment="Center"/>
                                        </StackPanel>
                                        <TextBlock Text="Always make sure to leave at least one checkbox unselected per group so you don't lose the file entirely!" Foreground="#AAAAAA" FontSize="12" TextWrapping="Wrap" LineHeight="18"/>
                                    </StackPanel>
                                </Border>
                                
                                <Border Style="{StaticResource GlassCard}" Padding="15" Margin="0,0,0,12" BorderBrush="#22FFFFFF" BorderThickness="1">
                                    <StackPanel>
                                        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                                            <TextBlock Text="&#x1F553;" FontSize="14" Margin="0,0,8,0" VerticalAlignment="Center"/>
                                            <TextBlock Text="Check Paths" Foreground="#00D2FF" FontSize="14" FontWeight="Bold" VerticalAlignment="Center"/>
                                        </StackPanel>
                                        <TextBlock Text="Review the file paths carefully. Sometimes a file is intentionally duplicated in different project folders." Foreground="#AAAAAA" FontSize="12" TextWrapping="Wrap" LineHeight="18"/>
                                    </StackPanel>
                                </Border>
                            </StackPanel>
                        </Grid>
"@

$target = @"
                                        </Button>
                                    </StackPanel>
                                </Grid>
                            </StackPanel>
                        </Grid>
"@

$content = $content.Replace($target, $replacement)
Set-Content $xamlPath $content -Encoding UTF8
