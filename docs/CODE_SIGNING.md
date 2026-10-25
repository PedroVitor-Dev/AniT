# Assinatura digital das distribuições

O AniT está preparado para Authenticode. A assinatura real depende de um certificado de assinatura de código emitido para o responsável pelo projeto, com acesso à chave privada. O repositório não fornece esse certificado e não cria certificados autoassinados para distribuição pública.

## Obter e preparar

1. Contrate um certificado de **Code Signing / Authenticode** ou um serviço de assinatura compatível, confirmando elegibilidade para pessoa física/empresa e disponibilidade no seu país diretamente com o fornecedor. A emissão exige validação de identidade.
2. Siga as instruções do emissor para proteger a chave em token/HSM ou serviço. Não copie chave privada, PIN, senha ou `.pfx` para o Git.
3. Para o fluxo atual com SignTool, configure o certificado e o provedor da chave em `Cert:\CurrentUser\My`. Serviços com API própria exigem integração específica; não são ativados apenas com um thumbprint.
4. Instale o **Windows SDK** (SignTool) e o **Inno Setup 7**.
5. Localize o certificado disponível, sem exportar a chave:

```powershell
Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Select-Object Subject, Thumbprint, NotAfter, HasPrivateKey
```

## Gerar uma distribuição assinada

```powershell
.\scripts\Build-Installer.ps1 -Version 1.0.2 -CertificateThumbprint SEU_THUMBPRINT
```

O script verifica o formato do thumbprint SHA-1, a presença da chave privada, a validade e a finalidade Code Signing antes de publicar ou limpar artefatos anteriores. Cada binário próprio é assinado com SHA-256, recebe timestamp RFC 3161 e passa por `signtool verify /pa`. O Inno Setup assina o instalador e seu desinstalador embutido. O SHA-256 do instalador é calculado depois da assinatura. Uma falha de assinatura/verificação encerra o processo; não publique os artefatos parciais.

`-SignToolPath` e `-InnoCompilerPath` permitem apontar as ferramentas explicitamente. `-TimestampUrl` aceita um endpoint HTTPS RFC 3161 indicado pelo emissor. O fluxo atual usa o repositório de certificados do usuário; certificados em LocalMachine ou serviços remotos exigem adaptação.

## Testes locais sem certificado

```powershell
.\scripts\Build-Installer.ps1 -Version 1.0.2 -AllowUnsigned
```

Essa opção é exclusiva para desenvolvimento e mostra um aviso. Não transforma o build em uma distribuição assinada. Passar um certificado inválido continua causando erro, mesmo com `-AllowUnsigned`.

## Conferir o resultado

```powershell
Get-AuthenticodeSignature .\artifacts\publish\win-x64\AniT.exe |
    Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
Get-AuthenticodeSignature .\artifacts\installer\AniT-Setup-1.0.2-win-x64.exe |
    Format-List Status, StatusMessage, SignerCertificate, TimeStamperCertificate
```

Exija `Status: Valid` e confira o nome do titular e o timestamp. Em uma instalação de teste, confira também `unins000.exe` na pasta instalada. Faça o smoke test em uma conta/máquina limpa. A assinatura identifica o editor e protege a integridade; não garante ausência de alertas do SmartScreen, cuja reputação é avaliada separadamente.

As notas da versão 1.0.2 continuam descrevendo o artefato histórico sem assinatura. Só anuncie uma nova distribuição como assinada depois de gerar e verificar o arquivo final com o certificado real.

Referências: [SignTool (Microsoft)](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool), [timestamp Authenticode (Microsoft)](https://learn.microsoft.com/en-us/windows/win32/seccrypto/time-stamping-authenticode-signatures), [SignTool no Inno Setup](https://jrsoftware.org/ishelp/topic_setup_signtool.htm) e [SignedUninstaller](https://jrsoftware.org/ishelp/topic_setup_signeduninstaller.htm).

## Testar as verificações prévias

Execute `.\scripts\Test-InstallerSigning.ps1`. Os testes usam certificados efêmeros em memória para cobrir ausência de certificado/chave, formato, expiração, finalidade, timestamp e descoberta do SignTool. Não instalam certificados nem geram artefatos. Isso não substitui a verificação final com um certificado confiável real.
