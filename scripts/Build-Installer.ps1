[CmdletBinding()]
param(
    [string] $Version = '2.0.3',
    [string] $Configuration = 'Release',
    [string] $Runtime = 'win-x64',
    [string] $DotNetPath = 'dotnet',
    [string] $InnoCompilerPath,
    [string] $CertificateThumbprint,
    [switch] $AllowUnsigned,
    [string] $SignToolPath,
    [string] $TimestampUrl = 'https://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactsRoot "publish\$Runtime"
$installerDirectory = Join-Path $artifactsRoot 'installer'
$projectPath = Join-Path $repositoryRoot 'src\AniT.App\AniT.App.csproj'
$installerScript = Join-Path $repositoryRoot 'installer\AniT.iss'

function Reset-ArtifactDirectory([string] $path) {
    $resolved = [System.IO.Path]::GetFullPath($path)
    $expectedPrefix = [System.IO.Path]::GetFullPath($artifactsRoot).TrimEnd('\') + '\'
    if (-not $resolved.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Diretório de artefato fora da raiz permitida: $resolved"
    }
    if (Test-Path -LiteralPath $resolved) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolved -Force | Out-Null
}

function Invoke-Checked([string] $executable, [string[]] $arguments) {
    & $executable @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Falha ao executar $executable (código $LASTEXITCODE)."
    }
}

function Resolve-SignTool([string] $explicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($explicitPath)) {
        if (-not (Test-Path -LiteralPath $explicitPath)) {
            throw "signtool.exe não encontrado no caminho informado: $explicitPath"
        }
        return [System.IO.Path]::GetFullPath($explicitPath)
    }

    $roots = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:ProgramFiles 'Microsoft SDKs\ClickOnce\SignTool')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }

    $candidate = $roots |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -Filter 'signtool.exe' -File -Recurse -ErrorAction SilentlyContinue } |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' -or $_.DirectoryName -notmatch '\\(arm|arm64|x86)\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if (-not $candidate) {
        throw 'signtool.exe não encontrado. Instale o Windows SDK ou informe -SignToolPath.'
    }

    return $candidate.FullName
}

function Invoke-AuthenticodeSign([string] $path, [string] $thumbprint, [string] $signTool) {
    Invoke-Checked $signTool @(
        'sign', '/sha1', $thumbprint,
        '/fd', 'SHA256',
        '/tr', $TimestampUrl,
        '/td', 'SHA256',
        $path
    )
    Invoke-Checked $signTool @('verify', '/pa', '/v', $path)
}

# Fail before deleting previous artifacts or publishing when release signing is unavailable.
$signingEnabled = -not [string]::IsNullOrWhiteSpace($CertificateThumbprint)
if (-not $signingEnabled -and -not $AllowUnsigned) {
    throw 'Distribuições exigem um certificado de assinatura. Informe -CertificateThumbprint ou use -AllowUnsigned somente para testes locais.'
}
if ($signingEnabled) {
    $CertificateThumbprint = ($CertificateThumbprint -replace '\s', '').ToUpperInvariant()
    if ($CertificateThumbprint -notmatch '^[A-F0-9]{40}$') {
        throw 'Informe o thumbprint SHA-1 de 40 caracteres do certificado de assinatura.'
    }
    $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    if (-not $certificate -or -not $certificate.HasPrivateKey) {
        throw 'Certificado com chave privada não encontrado em Cert:\CurrentUser\My. Configure o certificado/token do emissor antes de gerar a release.'
    }
    $now = Get-Date
    if ($certificate.NotBefore -gt $now -or $certificate.NotAfter -le $now) {
        throw 'O certificado de assinatura está fora do período de validade.'
    }
    $codeSigningUsage = $certificate.Extensions |
        Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
        ForEach-Object { $_.EnhancedKeyUsages } |
        Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }
    if (-not $codeSigningUsage) {
        throw 'O certificado informado não possui a finalidade Code Signing.'
    }
    $timestampUri = $null
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
        $timestampUri.Scheme -ne 'https' -or $TimestampUrl -match '[\s"$]') {
        throw 'Informe uma URL HTTPS válida para o timestamp RFC 3161.'
    }
    $SignToolPath = Resolve-SignTool $SignToolPath
}

Reset-ArtifactDirectory $publishDirectory
Reset-ArtifactDirectory $installerDirectory

& (Join-Path $PSScriptRoot 'New-InstallerAssets.ps1')

Invoke-Checked $DotNetPath @(
    'publish', $projectPath,
    '--configuration', $Configuration,
    '--runtime', $Runtime,
    '--self-contained', 'true',
    '--output', $publishDirectory,
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    # A publicação por RID precisa de um grafo de dependências diferente do
    # restore comum do CI. Grave esse lock transitório em obj/ para não alterar
    # os packages.lock.json versionados da solução.
    '-p:NuGetLockFilePath=obj\packages.publish.lock.json',
    "-p:Version=$Version"
)

$requiredFiles = @(
    (Join-Path $publishDirectory 'AniT.exe'),
    (Join-Path $publishDirectory 'Player\MPV\mpv.exe'),
    (Join-Path $publishDirectory 'Licenses\Mpv\COPYING.txt'),
    (Join-Path $publishDirectory 'Player\MPC-HC\mpc-hc64.exe'),
    (Join-Path $publishDirectory 'Player\MPC-HC\COPYING.txt')
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $requiredFile)) {
        throw "Arquivo obrigatório ausente na publicação: $requiredFile"
    }
}

Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $publishDirectory 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $publishDirectory 'THIRD_PARTY_NOTICES.md')

if ($signingEnabled) {
    # Only sign our own binaries; preserve third-party signatures and ownership.
    $ownedBinaries = Get-ChildItem -LiteralPath $publishDirectory -File | Where-Object {
        $_.Name -eq 'AniT.exe' -or $_.Name -eq 'AniT.dll' -or $_.Name -like 'AniT.*.dll'
    }
    foreach ($binary in $ownedBinaries) {
        Invoke-AuthenticodeSign $binary.FullName $CertificateThumbprint $SignToolPath
    }
} else {
    Write-Warning 'BUILD DE TESTE SEM ASSINATURA: não distribuir como release oficial.'
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $InnoCompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or -not (Test-Path -LiteralPath $InnoCompilerPath)) {
    throw 'ISCC.exe não encontrado. Instale o Inno Setup 7 ou informe -InnoCompilerPath.'
}

$compilerArguments = @(
    "/DAppVersion=$Version",
    "/DSourceDir=$publishDirectory",
    "/DOutputDir=$installerDirectory"
)
if ($signingEnabled) {
    # Inno Setup signs setup AND the embedded uninstaller during compilation.
    # $q/$f are Inno placeholders; escape literal dollars in the executable path.
    $innoSignTool = $SignToolPath.Replace('$', '$$')
    $signCommand = '$q{0}$q sign /sha1 {1} /fd SHA256 /tr $q{2}$q /td SHA256 $f' -f $innoSignTool, $CertificateThumbprint, $TimestampUrl
    $compilerArguments += '/DSigningEnabled=1'
    $compilerArguments += "/SAniTReleaseSign=$signCommand"
}
$compilerArguments += $installerScript
Invoke-Checked $InnoCompilerPath $compilerArguments

$installerPath = Join-Path $installerDirectory "AniT-Setup-$Version-win-x64.exe"
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "O compilador não produziu o instalador esperado: $installerPath"
}

if ($signingEnabled) {
    Invoke-Checked $SignToolPath @('verify', '/pa', '/all', '/v', $installerPath)
}

$hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
$hashLine = "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), [System.IO.Path]::GetFileName($installerPath)
Set-Content -LiteralPath (Join-Path $installerDirectory 'SHA256SUMS.txt') -Value $hashLine -Encoding ascii

Write-Output ''
Write-Output 'Instalador gerado com sucesso:'
Write-Output "  $installerPath"
Write-Output "  SHA-256: $($hash.Hash)"
