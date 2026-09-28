# Instalador do AniT

O instalador oficial usa Inno Setup 7 e publica o AniT para Windows x64 como aplicativo autossuficiente e por usuário.

## Gerar

```powershell
winget install --id JRSoftware.InnoSetup.7 -e --source winget
.\scripts\Build-Installer.ps1 -Version 1.0.2
```

Saída:

```text
artifacts\installer\AniT-Setup-1.0.2-win-x64.exe
artifacts\installer\SHA256SUMS.txt
```

O script publica o app, inclui o MPC-HC homologado, gera artes do assistente a partir dos assets oficiais, aplica `assets/anit.ico` e compila o `.iss`.

Para remover **Fornecedor desconhecido**, use um certificado Authenticode de assinatura de código instalado no repositório de certificados do usuário e informe seu thumbprint:

```powershell
.\scripts\Build-Installer.ps1 -Version 1.0.2 -CertificateThumbprint SEU_THUMBPRINT
```

O script assina e valida tanto `AniT.exe` quanto o instalador, usando carimbo de tempo SHA-256. O nome mostrado pelo Windows vem da identidade validada no certificado, não apenas de `AppPublisher` no Inno Setup. Sem um certificado confiável, o aviso do SmartScreen pode continuar mesmo com os metadados de fornecedor preenchidos.

## Comportamento

- instalação por usuário em `%LOCALAPPDATA%\Programs\AniT`;
- sem privilégios administrativos;
- Português (Brasil) e English;
- atalho opcional na Área de Trabalho;
- dados em `%LOCALAPPDATA%\AniT` preservados na atualização/desinstalação;
- nenhuma mídia pessoal incluída ou removida.

## Validação

Antes da publicação, confira hash, instalação limpa, atualização, atalho/ícone, perfil inicial, player e desinstalação. O artefato publicado deve ser baixado novamente e comparado com `SHA256SUMS.txt`.

Enquanto a versão 1.0.2 for publicada sem certificado, declare a limitação nas notas e nunca distribua sem o hash. Veja [docs/RELEASING.md](../docs/RELEASING.md).
