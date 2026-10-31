# AniT na Microsoft Store (MSIX)

O pacote Windows x64 é autossuficiente, inclui MPC-HC/mpv e licenças, e usa versão `2.0.3.0`. O quarto componente é reservado a zero. Nenhum instalador Inno Setup é executado pelo MSIX.

## Identidade obrigatória

Copie literalmente do Partner Center → AniT → Gerenciamento do produto → Identidade do produto:

- `Package/Identity/Name`
- `Package/Identity/Publisher` (valor completo com `CN=`)
- `Package/Properties/PublisherDisplayName`

A identidade oficial fornecida pelo publicador está registrada em `installer/msix/StoreIdentity.json`. São identificadores públicos do pacote, não credenciais. O PublisherId calculado foi conferido com o Package Family Name exibido no Partner Center.

O nome reservado mostrado ao usuário deve ser AniT. Uma identidade de desenvolvimento não deve ser enviada ao cadastro público. O script recebe esses campos como parâmetros e não inventa a identidade da Store.

## Gerar

Prepare os players conforme `Player/README.md` e `scripts/Get-Mpv.ps1`. Use o MakeAppx x64 oficial do Windows SDK. Exemplo PowerShell, substituindo os valores do cadastro:

```powershell
.\scripts\Build-Msix.ps1 `
  -IdentityName 'VALOR_DO_PARTNER_CENTER' `
  -Publisher 'CN=VALOR_COMPLETO_DO_PARTNER_CENTER' `
  -PublisherDisplayName 'NOME_EXATO_DO_PUBLICADOR' `
  -MakeAppxPath 'C:\WindowsSDK\x64\makeappx.exe' `
  -DotNetPath 'dotnet'
```

A saída fica em `artifacts/msix-2.0.3/`: MSIX, SHA-256 e registro JSON de identidade/versão. O empacotador valida o manifesto e os recursos; não usa `/nv`. O executável deve ter exatamente a versão do pacote. Uma pasta publicada previamente pode ser fornecida com `-PublishDirectory`.

O parâmetro `-ValidationOnly` identifica explicitamente um pacote de engenharia, sem identidade do cadastro; esse arquivo não está pronto para envio à Store.

## Permissões e validação

`runFullTrust` permite executar o aplicativo WPF, SQLite e players Win32 empacotados, além de acessar os arquivos de vídeo escolhidos pelo usuário. Rede local/internet atende Movie Theater e torrents; microfone atende o chat de voz. Não há serviço, driver, elevação administrativa ou instalador secundário.

A assinatura de distribuição MSIX pela Store é feita pela Microsoft após a certificação; o SignPath do instalador EXE é um fluxo separado. Fora da Store, um MSIX sem assinatura não é um instalador para distribuição direta.

Antes do envio, validar em máquina de teste com identidade correta e Windows App Certification Kit: abertura/onboarding, pasta externa, atualização, dados do perfil, reprodução local, mpv, Movie Theater e microfone. A validação estrutural do MakeAppx não substitui esses testes nem garante aprovação da Store.

Fontes oficiais:
- https://learn.microsoft.com/windows/apps/publish/publish-your-app/msix/app-package-requirements
- https://learn.microsoft.com/windows/msix/desktop/desktop-to-uwp-manual-conversion
