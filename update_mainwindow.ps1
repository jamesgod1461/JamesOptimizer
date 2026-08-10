$file = "c:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$content = Get-Content $file -Raw

# 1. Update RefreshStartupManager to filter by TxtSearchStartup.Text
$searchReplacement = @"
            });

            string searchQuery = TxtSearchStartup.Text?.ToLower() ?? "";
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                items = items.Where(i => (i.Name != null && i.Name.ToLower().Contains(searchQuery)) || (i.Path != null && i.Path.ToLower().Contains(searchQuery))).ToList();
            }

            ListStartupItems.Children.Clear();
"@
$content = $content.Replace("            });`r`n`r`n            ListStartupItems.Children.Clear();", $searchReplacement)
if ($content -notmatch "TxtSearchStartup.Text") {
    $content = $content.Replace("            });`n`n            ListStartupItems.Children.Clear();", $searchReplacement)
}

# 2. Append new methods before the last two braces.
$newMethods = @"
        private void TxtSearchStartup_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshStartupManager();
        }

        private async void BtnRefreshServices_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            await RefreshServiceManager();
        }

        private void TxtSearchServices_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshServiceManager();
        }

        private void BtnWindowsDebloat_Click(object sender, RoutedEventArgs e)
        {
            PlayClickSound();
            System.Windows.MessageBox.Show("This feature is ready! Please provide the list of services you want to debloat in the future, and they will be disabled here.", "Windows Debloat", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private async Task RefreshServiceManager()
        {
            if (ListServiceItems == null) return;
            ListServiceItems.Children.Clear();
            ListServiceItems.Children.Add(new TextBlock { Text = "Loading Services...", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), Margin = new Thickness(10) });

            List<ServiceInfo> items = new List<ServiceInfo>();

            await Task.Run(() =>
            {
                items = ServiceManagerLogic.GetAllServices();
            });

            string searchQuery = TxtSearchServices.Text?.ToLower() ?? "";
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                items = items.Where(i => (i.DisplayName != null && i.DisplayName.ToLower().Contains(searchQuery)) || (i.ServiceName != null && i.ServiceName.ToLower().Contains(searchQuery))).ToList();
            }

            ListServiceItems.Children.Clear();
            
            if (items.Count == 0)
            {
                ListServiceItems.Children.Add(new TextBlock { Text = "No services found.", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), Margin = new Thickness(10) });
                return;
            }

            foreach (var item in items)
            {
                Border card = new Border
                {
                    Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#151A25")),
                    BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#202B3D")),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(15)
                };

                Grid mainGrid = new Grid();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                StackPanel infoPanel = new StackPanel { Grid.Column = 0 };
                infoPanel.Children.Add(new TextBlock { Text = item.DisplayName, Foreground = new SolidColorBrush(System.Windows.Media.Colors.White), FontSize = 16, FontWeight = FontWeights.SemiBold });
                infoPanel.Children.Add(new TextBlock { Text = $"Service Name: {item.ServiceName}  •  Status: {item.Status}  •  Startup: {item.StartType}", Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8892B0")), FontSize = 12, Margin = new Thickness(0, 5, 0, 0) });

                StackPanel actionPanel = new StackPanel { Grid.Column = 1, Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                
                Button btnToggle = new Button
                {
                    Content = item.IsRunning ? "Stop" : "Start",
                    Width = 80, Height = 30, Margin = new Thickness(10, 0, 0, 0),
                    Background = new SolidColorBrush(item.IsRunning ? System.Windows.Media.Colors.DarkRed : System.Windows.Media.Colors.DarkGreen),
                    Foreground = new SolidColorBrush(System.Windows.Media.Colors.White),
                    BorderThickness = new Thickness(0),
                    IsEnabled = item.CanStop || !item.IsRunning
                };
                
                btnToggle.Click += async (s, e) =>
                {
                    btnToggle.IsEnabled = false;
                    btnToggle.Content = "Wait...";
                    await Task.Run(() => ServiceManagerLogic.ToggleServiceState(item.ServiceName, !item.IsRunning));
                    await RefreshServiceManager();
                };

                actionPanel.Children.Add(btnToggle);

                mainGrid.Children.Add(infoPanel);
                mainGrid.Children.Add(actionPanel);
                card.Child = mainGrid;
                ListServiceItems.Children.Add(card);
            }
        }
"@

$content = $content.TrimEnd()
if ($content.EndsWith("}")) { $content = $content.Substring(0, $content.Length - 1).TrimEnd() }
if ($content.EndsWith("}")) { $content = $content.Substring(0, $content.Length - 1).TrimEnd() }

$content = $content + "`r`n" + $newMethods + "`r`n    }`r`n}`r`n"
Set-Content $file $content
