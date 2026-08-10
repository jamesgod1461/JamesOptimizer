$file = "C:\Users\adity\source\repos\optimizer\OptimizerUI\SmoothScrollBehavior.cs"
$content = Get-Content $file -Raw

$content = $content.Replace("private static void CompositionTarget_Rendering(object sender, EventArgs e)", "private static void CompositionTarget_Rendering(object? sender, EventArgs e)")
$content = $content.Replace("private static ScrollViewer GetScrollViewerUnderMouse()", "private static ScrollViewer? GetScrollViewerUnderMouse()")

Set-Content -Path $file -Value $content -Encoding UTF8
