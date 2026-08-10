$xamlPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml"
$xaml = Get-Content $xamlPath -Raw

# Replace Sidebar Navigation Buttons
$oldSidebar = @"
                                <RadioButton x:Name="NavNetwork" Tag="&#x1F310;" Content="NETWORK BOOST" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                <RadioButton x:Name="NavToolbox" Tag="&#x1F527;" Content="TOOLS" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                <RadioButton x:Name="NavDiagnostics" Tag="&#x1F5A5;" Content="HARDWARE INFO" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                <RadioButton x:Name="NavMouse" Tag="&#x2699;" Content="SETTINGS" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                
                                <!-- Hidden legacy buttons -->
                                <RadioButton x:Name="NavStorage" Tag="&#x1F4BE;" Content="STORAGE" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                <RadioButton x:Name="NavGraphics" Visibility="Collapsed"/>
"@

$newSidebar = @"
                                <RadioButton x:Name="NavNetwork" Tag="&#x1F310;" Content="NETWORK BOOST" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                <RadioButton x:Name="NavStorage" Tag="&#x1F4BE;" Content="STORAGE" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                <RadioButton x:Name="NavToolbox" Tag="&#x1F527;" Content="TOOLS" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                <RadioButton x:Name="NavDiagnostics" Tag="&#x1F5A5;" Content="HARDWARE INFO" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked"/>
                                <RadioButton x:Name="NavMouse" Tag="&#x2699;" Content="SETTINGS" Style="{StaticResource SidebarNavButton}" GroupName="SidebarNav" Checked="Nav_Checked" Visibility="Collapsed"/>
                                <RadioButton x:Name="NavGraphics" Visibility="Collapsed"/>
"@

$xaml = $xaml.Replace($oldSidebar, $newSidebar)

# Add Setting Button to Title Bar
$oldTitleBar = @"
                <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="20,0,0,0" VerticalAlignment="Center">
                    <Path Data="M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M12,6A6,6 0 0,1 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6Z" Fill="#1A73E8" Width="20" Height="20" Stretch="Fill" Margin="0,0,10,0"/>
                    <TextBlock Text="JAMES OPTIMIZER" FontSize="14" FontWeight="Bold" Foreground="#FFFFFF"/>
"@

$newTitleBar = @"
                <StackPanel Grid.Column="0" Orientation="Horizontal" Margin="20,0,0,0" VerticalAlignment="Center">
                    <Button x:Name="BtnTopSettings" Content="&#x2699;" Background="Transparent" Foreground="#8892B0" BorderThickness="0" Cursor="Hand" FontSize="16" Click="BtnTopSettings_Click" Margin="0,0,10,0" ToolTip="Settings"/>
                    <Path Data="M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M12,6A6,6 0 0,1 18,12A6,6 0 0,1 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6Z" Fill="#1A73E8" Width="20" Height="20" Stretch="Fill" Margin="0,0,10,0"/>
                    <TextBlock Text="JAMES OPTIMIZER" FontSize="14" FontWeight="Bold" Foreground="#FFFFFF"/>
"@

$xaml = $xaml.Replace($oldTitleBar, $newTitleBar)
Set-Content -Path $xamlPath -Value $xaml -Encoding UTF8

# Now modify C# code behind
$csPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$cs = Get-Content $csPath -Raw

$oldCs = @"
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
"@

$newCs = @"
        private void BtnTopSettings_Click(object sender, RoutedEventArgs e)
        {
            if (NavMouse != null)
            {
                NavMouse.IsChecked = true;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
"@

$cs = $cs.Replace($oldCs, $newCs)
Set-Content -Path $csPath -Value $cs -Encoding UTF8
