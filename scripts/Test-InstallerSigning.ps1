[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$buildScript = Join-Path $PSScriptRoot 'Build-Installer.ps1'
$script:mockCertificate = $null
$checks = 0

# Replace only certificate lookup within this test process. No OS certificate is installed.
function Get-Item {
    param([string] $LiteralPath, [string] $ErrorAction)
    if ($LiteralPath -like 'Cert:\CurrentUser\My\*') { return $mockCertificate }
    Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath
}

function Assert-PreflightFailure([hashtable] $parameters, [string] $expected) {
    $failed = $false
    try {
        & $buildScript @parameters -DotNetPath 'must-not-run.exe'
    } catch {
        $failed = $true
        if ($_.Exception.Message -notlike $expected) { throw }
    }
    if (-not $failed) { throw "Expected preflight failure: $expected" }
    $script:checks++
    Write-Output "PASS: $expected"
}

function New-InMemoryCertificate([string] $usage, [DateTimeOffset] $expires) {
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=AniT preflight test', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $usages = [Security.Cryptography.OidCollection]::new()
        [void]$usages.Add([Security.Cryptography.Oid]::new($usage))
        $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages, $false))
        return $request.CreateSelfSigned([DateTimeOffset]::Now.AddDays(-2), $expires)
    } finally { $rsa.Dispose() }
}

Assert-PreflightFailure @{} '*Distribuições exigem*'
Assert-PreflightFailure @{ CertificateThumbprint = 'INVALID'; AllowUnsigned = $true } '*thumbprint SHA-1*'
$parameters = @{ CertificateThumbprint = ('A' * 40); SignToolPath = (Join-Path $PSScriptRoot 'not-a-real-signtool.exe') }
Assert-PreflightFailure $parameters '*Certificado com chave privada não encontrado*'
try {
    $script:mockCertificate = New-InMemoryCertificate '1.3.6.1.5.5.7.3.3' ([DateTimeOffset]::Now.AddDays(-1))
    Assert-PreflightFailure $parameters '*fora do período de validade*'
    $script:mockCertificate.Dispose()
    $script:mockCertificate = New-InMemoryCertificate '1.3.6.1.5.5.7.3.1' ([DateTimeOffset]::Now.AddDays(1))
    Assert-PreflightFailure $parameters '*não possui a finalidade Code Signing*'
    $script:mockCertificate.Dispose()
    $script:mockCertificate = New-InMemoryCertificate '1.3.6.1.5.5.7.3.3' ([DateTimeOffset]::Now.AddDays(1))
    $invalidTimestamp = $parameters.Clone()
    $invalidTimestamp.TimestampUrl = 'http://example.invalid'
    Assert-PreflightFailure $invalidTimestamp '*URL HTTPS válida*'
    # A valid in-memory certificate must reach tool discovery without publishing anything.
    Assert-PreflightFailure $parameters '*signtool.exe não encontrado no caminho informado*'
} finally {
    if ($script:mockCertificate) { $script:mockCertificate.Dispose() }
}

Write-Output "$checks signing preflight checks passed. No build artifacts or certificate stores were modified."
