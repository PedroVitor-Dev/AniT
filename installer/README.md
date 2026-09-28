# Instalador do AniT

O instalador oficial usa Inno Setup 7 e publica o AniT como aplicativo Windows x64 autossuficiente.

## Gerar

```powershell
winget install --id JRSoftware.InnoSetup.7 -e --source winget
.\scripts\Build-Installer.ps1
```

Os artefatos são criados em `artifacts/installer/`:

- `AniT-Setup-1.0.1-win-x64.exe`
- `SHA256SUMS.txt`

O pacote instala somente para o usuário atual em `%LOCALAPPDATA%\Programs\AniT`, não requer privilégios administrativos e não remove `%LOCALAPPDATA%\AniT` durante a desinstalação. O instalador e os atalhos usam `assets/anit.ico`; as artes do assistente são reconstruídas a partir dos assets oficiais pelo script `New-InstallerAssets.ps1`.

O primeiro release não é assinado digitalmente. O hash SHA-256 deve ser anexado à GitHub Release e essa limitação precisa constar nas notas públicas.
