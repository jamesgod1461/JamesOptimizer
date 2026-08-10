$csPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$cs = Get-Content $csPath -Raw

# 1. Replace NavMouse in Nav_Checked
$oldNavMouse = "else if (sender == NavMouse && PageMouse != null) { PageMouse.Visibility = Visibility.Visible; FadeInPage(PageMouse); }"
$newNavMouse = @"
else if (sender == NavMouse && PageMouse != null) { 
                PageMouse.Visibility = Visibility.Visible; FadeInPage(PageMouse); 
                _ = LoadAboutSpecsAsync();
            }
"@
$cs = $cs.Replace($oldNavMouse, $newNavMouse)

# 2. Remove old mouse methods
$start1 = $cs.IndexOf('private void SldMouseSensitivity_ValueChanged')
$end1 = $cs.IndexOf('private void BtnDisableAcceleration_Click')
if ($start1 -gt -1 -and $end1 -gt $start1) {
    $cs = $cs.Remove($start1, $end1 - $start1)
}

$start2 = $cs.IndexOf('private void BtnDisableAcceleration_Click')
$end2 = $cs.IndexOf('private async void BtnDebloat_Click')
if ($start2 -gt -1 -and $end2 -gt $start2) {
    $cs = $cs.Remove($start2, $end2 - $start2)
}

$start3 = $cs.IndexOf('private async void BtnMouseRawInput_Click')
$end3 = $cs.IndexOf('private async void BtnGpu_Click')
if ($start3 -gt -1 -and $end3 -gt $start3) {
    $cs = $cs.Remove($start3, $end3 - $start3)
}

# 3. Add LoadAboutSpecsAsync
$method = @"
        private bool _specsLoaded = false;
        private async Task LoadAboutSpecsAsync()
        {
            if (_specsLoaded) return;
            _specsLoaded = true;

            await Task.Run(() =>
            {
                // CPU
                string cpu = "Unknown CPU";
                try {
                    using (var searcher = new System.Management.ManagementObjectSearcher("select Name from Win32_Processor"))
                    {
                        foreach (var item in searcher.Get()) { cpu = item["Name"]?.ToString(); break; }
                    }
                } catch { }

                // GPU
                string gpu = "Unknown GPU";
                try {
                    using (var searcher = new System.Management.ManagementObjectSearcher("select Name from Win32_VideoController"))
                    {
                        foreach (var item in searcher.Get()) { gpu = item["Name"]?.ToString(); break; }
                    }
                } catch { }

                // RAM
                string ram = "Unknown RAM";
                try {
                    using (var searcher = new System.Management.ManagementObjectSearcher("select Capacity from Win32_PhysicalMemory"))
                    {
                        long totalBytes = 0;
                        foreach (var item in searcher.Get()) {
                            if (long.TryParse(item["Capacity"]?.ToString(), out long cap)) totalBytes += cap;
                        }
                        ram = Math.Round(totalBytes / (1024.0 * 1024 * 1024), 1) + " GB";
                    }
                } catch { }

                // Storage
                string storage = "";
                try {
                    using (var searcher = new System.Management.ManagementObjectSearcher("select Model, Size, MediaType from Win32_DiskDrive"))
                    {
                        foreach (var item in searcher.Get()) {
                            string model = item["Model"]?.ToString() ?? "Unknown";
                            string media = item["MediaType"]?.ToString() ?? "Disk";
                            double sizeGb = 0;
                            if (long.TryParse(item["Size"]?.ToString(), out long sizeBytes)) {
                                sizeGb = Math.Round(sizeBytes / (1024.0 * 1024 * 1024), 0);
                            }
                            storage += $"{model} ({media}, {sizeGb}GB)\n";
                        }
                    }
                } catch { }
                if (string.IsNullOrWhiteSpace(storage)) storage = "Unknown Storage";

                // Network
                string network = "Unknown Network Adapter";
                try {
                    using (var searcher = new System.Management.ManagementObjectSearcher("select Name, NetConnectionStatus from Win32_NetworkAdapter where NetConnectionStatus = 2"))
                    {
                        foreach (var item in searcher.Get()) { network = item["Name"]?.ToString(); break; }
                    }
                } catch { }

                // Ping
                string pingStr = "Checking...";
                try {
                    using (var pinger = new System.Net.NetworkInformation.Ping())
                    {
                        var reply = pinger.Send("8.8.8.8", 2000);
                        if (reply.Status == System.Net.NetworkInformation.IPStatus.Success) {
                            pingStr = $"{reply.RoundtripTime} ms (to 8.8.8.8)";
                        } else {
                            pingStr = $"Timeout ({reply.Status})";
                        }
                    }
                } catch { pingStr = "Failed to ping"; }

                // Update UI
                Dispatcher.Invoke(() => {
                    TxtInfoCpu.Text = cpu;
                    TxtInfoGpu.Text = gpu;
                    TxtInfoRam.Text = ram;
                    TxtInfoStorage.Text = storage.Trim();
                    TxtInfoNetwork.Text = network;
                    TxtInfoPing.Text = pingStr;
                });
            });
        }
"@

$insertIndex = $cs.LastIndexOf('}')
if ($insertIndex -gt 0) {
    # It's inside the namespace. The actual last '}' is namespace, second to last is class.
    # Let's just insert before the last '    }' in the file which is the class end.
    $classEndIndex = $cs.LastIndexOf("    }")
    if ($classEndIndex -gt 0) {
        $cs = $cs.Insert($classEndIndex, $method + "`n")
    }
}

Set-Content -Path $csPath -Value $cs -Encoding UTF8
