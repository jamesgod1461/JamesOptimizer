$csPath = "C:\Users\adity\source\repos\optimizer\OptimizerUI\MainWindow.xaml.cs"
$cs = Get-Content $csPath -Raw
$cs = $cs.Replace("â€”", "-")
Set-Content -Path $csPath -Value $cs -Encoding UTF8
