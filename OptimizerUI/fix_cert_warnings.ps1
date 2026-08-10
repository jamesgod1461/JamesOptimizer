$files = @(
    "C:\Users\adity\source\repos\optimizer\OptimizerUI\ServiceManagerLogic.cs",
    "C:\Users\adity\source\repos\optimizer\OptimizerUI\StartupManagerService.cs"
)

foreach ($file in $files) {
    $content = Get-Content $file -Raw
    
    # Fix CS8600: string cn = null, o = null; -> string? cn = null, o = null;
    $content = $content -replace 'string cn = null, o = null;', 'string? cn = null, o = null;'
    
    # Fix SYSLIB0057: suppress warning for CreateFromSignedFile
    $badCertCode = "var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(exePath);"
    $goodCertCode = @"
#pragma warning disable SYSLIB0057
                        var cert = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(exePath);
#pragma warning restore SYSLIB0057
"@
    $content = $content.Replace($badCertCode, $goodCertCode)
    
    Set-Content -Path $file -Value $content -Encoding UTF8
}
