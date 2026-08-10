$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

$oldXaml = @"
                                <Grid Grid.Row="1" Margin="0,0,0,20">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <Button Grid.Column="0" x:Name="BtnDeleteLargeFiles" Content="Delete Selected" Style="{StaticResource NeonToolButton}" Height="35" Background="#FF3333" Foreground="White" BorderBrush="#FF3333" FontWeight="Bold" Click="BtnDeleteLargeFiles_Click" Margin="0,0,15,0" Padding="15,0" Visibility="Collapsed"/>
                                    <TextBlock Grid.Column="1" x:Name="TxtLargeFilesStatus" Text="Scanning..." Foreground="#88FFFFFF" FontSize="14" VerticalAlignment="Center" HorizontalAlignment="Right"/>
                                </Grid>
"@
$newXaml = @"
                                <Grid Grid.Row="1" Margin="0,0,0,20">
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="*"/>
                                    </Grid.ColumnDefinitions>
                                    <Button Grid.Column="0" x:Name="BtnDeleteLargeFiles" Content="Delete Selected" Style="{StaticResource NeonToolButton}" Height="35" Background="#FF3333" Foreground="White" BorderBrush="#FF3333" FontWeight="Bold" Click="BtnDeleteLargeFiles_Click" Margin="0,0,15,0" Padding="15,0" Visibility="Collapsed"/>
                                    <ComboBox Grid.Column="1" x:Name="CmbLargeFileExtensions" Width="120" Height="25" Margin="0,0,15,8" VerticalAlignment="Center" SelectionChanged="CmbLargeFileExtensions_SelectionChanged">
                                        <ComboBoxItem Content="All Files" IsSelected="True"/>
                                    </ComboBox>
                                    <TextBlock Grid.Column="2" x:Name="TxtLargeFilesStatus" Text="Scanning..." Foreground="#88FFFFFF" FontSize="14" VerticalAlignment="Center" HorizontalAlignment="Right"/>
                                </Grid>
"@
$xaml = $xaml.Replace($oldXaml, $newXaml)
Set-Content $xamlPath $xaml -Encoding UTF8


$csPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$cs = Get-Content $csPath -Raw

$oldCs = @"
        // --- LARGE FILES HANDLERS ---
        private async void BtnLargeFiles_Click(object sender, MouseButtonEventArgs e)
        {
            PlayClickSound();
            PageStorage.Visibility = Visibility.Collapsed;
            PageLargeFiles.Visibility = Visibility.Visible; FadeInPage(PageLargeFiles);
            BtnDeleteLargeFiles.Visibility = Visibility.Collapsed;
            TxtLargeFilesStatus.Text = `"Scanning your profile for files > 50MB...`";
            
            var files = await OptimizationEngine.ScanLargeFilesAsync();
            var items = files.Select(f => new SelectableFileItem { File = f, IsSelected = false }).ToList();
            
            ListLargeFiles.ItemsSource = items;
            TxtLargeFilesStatus.Text = `$"Found {items.Count} large files.`";
            if (items.Count > 0) BtnDeleteLargeFiles.Visibility = Visibility.Visible;
        }

        private async void BtnDeleteLargeFiles_Click(object sender, RoutedEventArgs e)
        {
            if (ListLargeFiles.ItemsSource is IEnumerable<SelectableFileItem> items)
            {
                var selected = items.Where(x => x.IsSelected).Select(x => x.File.FilePath).ToList();
"@
$newCs = @"
        // --- LARGE FILES HANDLERS ---
        private List<SelectableFileItem> _allLargeFiles = new List<SelectableFileItem>();

        private async void BtnLargeFiles_Click(object sender, MouseButtonEventArgs e)
        {
            PlayClickSound();
            PageStorage.Visibility = Visibility.Collapsed;
            PageLargeFiles.Visibility = Visibility.Visible; FadeInPage(PageLargeFiles);
            BtnDeleteLargeFiles.Visibility = Visibility.Collapsed;
            TxtLargeFilesStatus.Text = `"Scanning your profile for files > 50MB...`";
            
            var files = await OptimizationEngine.ScanLargeFilesAsync();
            _allLargeFiles = files.Select(f => new SelectableFileItem { File = f, IsSelected = false }).ToList();
            
            var exts = _allLargeFiles.Select(f => System.IO.Path.GetExtension(f.File.FilePath).ToLower())
                                     .Distinct().Where(ext => !string.IsNullOrEmpty(ext))
                                     .OrderBy(ext => ext).ToList();
            
            CmbLargeFileExtensions.SelectionChanged -= CmbLargeFileExtensions_SelectionChanged;
            CmbLargeFileExtensions.Items.Clear();
            CmbLargeFileExtensions.Items.Add(new ComboBoxItem { Content = `"All Files`" });
            foreach(var ext in exts)
            {
                CmbLargeFileExtensions.Items.Add(new ComboBoxItem { Content = ext });
            }
            CmbLargeFileExtensions.SelectedIndex = 0;
            CmbLargeFileExtensions.SelectionChanged += CmbLargeFileExtensions_SelectionChanged;

            ListLargeFiles.ItemsSource = _allLargeFiles;
            TxtLargeFilesStatus.Text = `$"Found {_allLargeFiles.Count} large files.`";
            if (_allLargeFiles.Count > 0) BtnDeleteLargeFiles.Visibility = Visibility.Visible;
        }

        private void CmbLargeFileExtensions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbLargeFileExtensions.SelectedItem is ComboBoxItem item)
            {
                string filter = item.Content.ToString();
                if (filter == `"All Files`")
                {
                    ListLargeFiles.ItemsSource = _allLargeFiles;
                }
                else
                {
                    ListLargeFiles.ItemsSource = _allLargeFiles.Where(f => System.IO.Path.GetExtension(f.File.FilePath).ToLower() == filter).ToList();
                }
                
                var currentItems = ListLargeFiles.ItemsSource as IList<SelectableFileItem>;
                int count = currentItems?.Count ?? 0;
                TxtLargeFilesStatus.Text = `$"Showing {count} files.`";
                BtnDeleteLargeFiles.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void BtnDeleteLargeFiles_Click(object sender, RoutedEventArgs e)
        {
            if (ListLargeFiles.ItemsSource is IEnumerable<SelectableFileItem> items)
            {
                var selected = _allLargeFiles.Where(x => x.IsSelected).Select(x => x.File.FilePath).ToList();
"@
$cs = $cs.Replace($oldCs, $newCs)
Set-Content $csPath $cs -Encoding UTF8
