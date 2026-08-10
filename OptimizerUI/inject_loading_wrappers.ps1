$path = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$content = Get-Content -Path $path -Raw

# Remove OptimizationEngine.OnProgress = null; globally
$content = $content.Replace("            OptimizationEngine.OnProgress = null;", "")

# Add LoadingOverlay to BtnRunDiagnostics
$diagOld = @"
        private async void BtnRunDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnRunDiagnostics.IsEnabled = false;
            TxtDiagnosticsOutput.Text = `"Gathering system health data. Please wait...\n\n`";

            string report = await Task.Run(() => GatherDiagnosticsData());
            
            TxtDiagnosticsOutput.Text = report;
            BtnRunDiagnostics.IsEnabled = true;
        }
"@
$diagNew = @"
        private async void BtnRunDiagnostics_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnRunDiagnostics.IsEnabled = false;
            TxtDiagnosticsOutput.Text = `"Gathering system health data. Please wait...\n\n`";
            
            LoadingOverlay.Visibility = Visibility.Visible;
            TxtLoadingTitle.Text = `"Running Diagnostics`";
            ProgressBarLoading.Value = 0;
            TxtLoadingMessage.Text = `"Gathering system health data. Please wait...`";

            string report = "";
            try {
                report = await Task.Run(() => GatherDiagnosticsData());
            } finally {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            
            TxtDiagnosticsOutput.Text = report;
            BtnRunDiagnostics.IsEnabled = true;
        }
"@
$content = $content.Replace($diagOld, $diagNew)

# Add LoadingOverlay to BtnDeepCleanupProceed_Click
$cleanupOld = @"
        private async void BtnDeepCleanupProceed_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnDeepCleanupProceed.IsEnabled = false;
            BtnDeepCleanupProceed.Content = `"Cleaning...`";
            
            var options = new OptimizationEngine.DeepCleanupOptions
            {
                CleanAppTemp = ChkAppItems?.IsChecked == true,
                CleanSysTemp = ChkSysItems?.IsChecked == true,
                CleanShaderCache = ChkShaderCache?.IsChecked == true,
                CleanVendorCache = ChkVendorCache?.IsChecked == true
            };
            
            await OptimizationEngine.ExecuteDeepCleanupAsync(options);
            
            BtnDeepCleanupProceed.Content = `"Done!`";
            await Task.Delay(2000);
            BtnDeepCleanupProceed.Content = `"Proceed`";
            BtnDeepCleanupProceed.IsEnabled = true;
            
            // Rescan
            BtnDeepCleanupScan_Click(null!, null!);
        }
"@
$cleanupNew = @"
        private async void BtnDeepCleanupProceed_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnDeepCleanupProceed.IsEnabled = false;
            BtnDeepCleanupProceed.Content = `"Cleaning...`";
            
            LoadingOverlay.Visibility = Visibility.Visible;
            TxtLoadingTitle.Text = `"Deep Cleanup`";
            ProgressBarLoading.Value = 0;
            TxtLoadingMessage.Text = `"Cleaning storage...`";

            var options = new OptimizationEngine.DeepCleanupOptions
            {
                CleanAppTemp = ChkAppItems?.IsChecked == true,
                CleanSysTemp = ChkSysItems?.IsChecked == true,
                CleanShaderCache = ChkShaderCache?.IsChecked == true,
                CleanVendorCache = ChkVendorCache?.IsChecked == true
            };
            
            try {
                await OptimizationEngine.ExecuteDeepCleanupAsync(options);
            } finally {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
            
            BtnDeepCleanupProceed.Content = `"Done!`";
            await Task.Delay(2000);
            BtnDeepCleanupProceed.Content = `"Proceed`";
            BtnDeepCleanupProceed.IsEnabled = true;
            
            // Rescan
            BtnDeepCleanupScan_Click(null!, null!);
        }
"@
$content = $content.Replace($cleanupOld, $cleanupNew)

# Add LoadingOverlay to BtnToolBackup_Click
$backupOld = @"
        private async void BtnToolBackup_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnToolBackup.IsEnabled = false;
            BtnToolBackup.Content = `"...`";

            string result = await OptimizationEngine.CreateRestorePointAsync();

            BtnToolBackup.Content = `"CREATE`";
            BtnToolBackup.IsEnabled = true;
            ShowFeedback(result, 5);
        }
"@
$backupNew = @"
        private async void BtnToolBackup_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnToolBackup.IsEnabled = false;
            BtnToolBackup.Content = `"...`";

            LoadingOverlay.Visibility = Visibility.Visible;
            TxtLoadingTitle.Text = `"Creating Restore Point`";
            ProgressBarLoading.Value = 0;
            TxtLoadingMessage.Text = `"Backing up registry and system state...`";

            string result = "";
            try {
                result = await OptimizationEngine.CreateRestorePointAsync();
            } finally {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }

            BtnToolBackup.Content = `"CREATE`";
            BtnToolBackup.IsEnabled = true;
            ShowFeedback(result, 5);
        }
"@
$content = $content.Replace($backupOld, $backupNew)

# Add LoadingOverlay to BtnToolDriverCheck_Click
$driverOld = @"
        private async void BtnToolDriverCheck_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnToolDriverCheck.IsEnabled = false;
            BtnToolDriverCheck.Content = `"...`";
            TxtDriverResult.Visibility = Visibility.Collapsed;

            string result = await OptimizationEngine.CheckDriversAsync();

            TxtDriverResult.Text = result;
            TxtDriverResult.Visibility = Visibility.Visible;
            BtnToolDriverCheck.Content = `"CHECK`";
            BtnToolDriverCheck.IsEnabled = true;
        }
"@
$driverNew = @"
        private async void BtnToolDriverCheck_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            BtnToolDriverCheck.IsEnabled = false;
            BtnToolDriverCheck.Content = `"...`";
            TxtDriverResult.Visibility = Visibility.Collapsed;

            LoadingOverlay.Visibility = Visibility.Visible;
            TxtLoadingTitle.Text = `"Checking Drivers`";
            ProgressBarLoading.Value = 0;
            TxtLoadingMessage.Text = `"Querying system devices...`";

            string result = "";
            try {
                result = await OptimizationEngine.CheckDriversAsync();
            } finally {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }

            TxtDriverResult.Text = result;
            TxtDriverResult.Visibility = Visibility.Visible;
            BtnToolDriverCheck.Content = `"CHECK`";
            BtnToolDriverCheck.IsEnabled = true;
        }
"@
$content = $content.Replace($driverOld, $driverNew)

Set-Content -Path $path -Value $content
Write-Host "Injected Loading Wrappers"
