[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string] $IdentityName,
    [Parameter(Mandatory=$true)][string] $Publisher,
    [Parameter(Mandatory=$true)][string] $PublisherDisplayName,
    [Parameter(Mandatory=$true)][string] $MakeAppxPath,
    [string] $Version = '2.0.2',
    [string] $PublishDirectory,
    [string] $OutputDirectory,
    [string] $DotNetPath = 'dotnet',
    [switch] $ValidationOnly
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Use o Package/Identity/Name exato do Partner Center (3-50 caracteres).' }
if ($Publisher -notmatch '^CN=') { throw 'Use o Package/Identity/Publisher completo, incluindo CN=.' }
if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) { throw 'PublisherDisplayName obrigatório.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use uma versão de três componentes; o quarto componente MSIX será zero.' }
$packageVersion = "$Version.0"
if (!(Test-Path -LiteralPath $MakeAppxPath)) { throw 'MakeAppx.exe não encontrado. Use o Windows SDK oficial.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repositoryRoot "artifacts\msix-$Version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
if (!$PublishDirectory) {
    $PublishDirectory = Join-Path $OutputDirectory ('publish-' + [Guid]::NewGuid().ToString('N'))
    & $DotNetPath publish (Join-Path $repositoryRoot 'src\AniT.App') -c Release -r win-x64 --self-contained true --output $PublishDirectory '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' '-p:DebugType=None' '-p:DebugSymbols=false' '-p:NuGetLockFilePath=obj\packages.publish.lock.json' "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação .NET.' }
}
$PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
foreach ($relative in @('AniT.exe', 'AniT.dll', 'Player\MPV\mpv.exe', 'Player\MPC-HC\mpc-hc64.exe', 'Licenses\Mpv\COPYING.txt', 'Licenses\Mpv\NOTICE.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $PublishDirectory $relative))) { throw "Arquivo obrigatório ausente: $relative" }
}
$appVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $PublishDirectory 'AniT.exe')).FileVersion
if ($appVersion -ne $packageVersion) { throw "Versão do aplicativo ($appVersion) difere do pacote ($packageVersion)." }
$staging = Join-Path $OutputDirectory ('stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
Get-ChildItem -LiteralPath $PublishDirectory -Force | Copy-Item -Destination $staging -Recurse
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $staging 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $staging
$assets = Join-Path $staging 'StoreAssets'
New-Item -ItemType Directory -Path $assets | Out-Null
Add-Type -AssemblyName System.Drawing
$icon = [Drawing.Icon]::new((Join-Path $repositoryRoot 'assets\anit.ico'), 256, 256)
$source = $icon.ToBitmap()
try {
    foreach ($asset in @(@('StoreLogo',50,50), @('Square44x44Logo',44,44), @('Square150x150Logo',150,150), @('Wide310x150Logo',310,150))) {
        $canvas = [Drawing.Bitmap]::new([int]$asset[1], [int]$asset[2], [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [Drawing.Graphics]::FromImage($canvas)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $size = [int]([Math]::Min([int]$asset[1], [int]$asset[2]) * 0.88)
            $graphics.DrawImage($source, [int](([int]$asset[1]-$size)/2), [int](([int]$asset[2]-$size)/2), $size, $size)
            $canvas.Save((Join-Path $assets ($asset[0]+'.png')), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $canvas.Dispose() }
    }
} finally { $source.Dispose(); $icon.Dispose() }
$manifest = Get-Content -LiteralPath (Join-Path $repositoryRoot 'installer\msix\AppxManifest.xml.template') -Raw -Encoding utf8
foreach ($entry in @{IdentityName=$IdentityName; Publisher=$Publisher; PublisherDisplayName=$PublisherDisplayName; Version=$packageVersion}.GetEnumerator()) {
    $manifest = $manifest.Replace('{{'+$entry.Key+'}}', [Security.SecurityElement]::Escape($entry.Value))
}
if ($manifest.Contains('{{')) { throw 'Manifesto contém placeholders não resolvidos.' }
[IO.File]::WriteAllText((Join-Path $staging 'AppxManifest.xml'), $manifest, [Text.UTF8Encoding]::new($false))
$suffix = if ($ValidationOnly) { '-validation-only' } else { '' }
$package = Join-Path $OutputDirectory "AniT-$packageVersion-x64$suffix.msix"
if (Test-Path -LiteralPath $package) { throw "O pacote já existe; escolha outra pasta de saída: $package" }
& $MakeAppxPath pack /d $staging /p $package /h SHA256
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx rejeitou o pacote. Nenhuma validação foi desativada.' }
$checksum = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory ([IO.Path]::GetFileName($package)+'.sha256')), "$checksum  $([IO.Path]::GetFileName($package))`n")
@{version=$packageVersion; identity=$IdentityName; publisher=$Publisher; publisherDisplayName=$PublisherDisplayName; architecture='x64'; sha256=$checksum; validationOnly=[bool]$ValidationOnly; signed=$false; file=[IO.Path]::GetFileName($package)} | ConvertTo-Json | Set-Content -LiteralPath ($package+'.json') -Encoding utf8
Write-Output "MSIX: $package"
Write-Output "SHA256: $checksum"
