[CmdletBinding()]
param([Parameter(Mandatory=$true)][string] $Destination)
$ErrorActionPreference='Stop'
$destinationPath=[IO.Path]::GetFullPath($Destination)
if ((Test-Path -LiteralPath $destinationPath) -and (Get-ChildItem -LiteralPath $destinationPath -Force | Select-Object -First 1)) { throw 'Escolha um destino vazio para o mpv.' }
New-Item -ItemType Directory -Path $destinationPath -Force | Out-Null
$archive=Join-Path ([IO.Path]::GetTempPath()) ("AniT-mpv-"+[Guid]::NewGuid().ToString('N')+'.7z')
$url='https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/mpv-x86_64-20261021-git-e471496c56.7z'
Invoke-WebRequest -Uri $url -OutFile $archive
$expected='4A355F3E89C83FABC730D33FB4E759DFEC747806ED2287B61A90D1681B83E8D3'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'O SHA-256 do pacote mpv não corresponde ao fixado.' }
& tar -xf $archive -C $destinationPath
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $destinationPath 'mpv.exe'))) { throw 'Falha ao extrair o mpv.' }
Remove-Item -LiteralPath $archive
Write-Output "mpv validado em $destinationPath"
