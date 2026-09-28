# Instalação, atualização e remoção

## Requisitos

- Windows 10 build 17763 ou posterior, ou Windows 11;
- processador e sistema operacional x64;
- espaço livre para aproximadamente 300 MB do aplicativo, além do cache de artes configurado;
- permissão de escrita no perfil do usuário.

O instalador oficial é autossuficiente: não exige instalação separada do .NET nem do MPC-HC.

## Download oficial

Use somente a página [Releases do repositório](https://github.com/PedroVitor-Dev/AniT/releases). Para a versão 1.0.2, baixe:

- `AniT-Setup-1.0.2-win-x64.exe`;
- `SHA256SUMS.txt`.

Valide o arquivo antes de executar:

```powershell
Get-FileHash .\AniT-Setup-1.0.2-win-x64.exe -Algorithm SHA256
```

Compare o valor exibido com a linha correspondente em `SHA256SUMS.txt`, publicado junto do instalador. Não execute o arquivo se os valores forem diferentes.

## Instalar

1. Execute o instalador.
2. Escolha Português (Brasil) ou English.
3. Confira o diretório e escolha se deseja um atalho na Área de Trabalho.
4. Conclua e inicie o AniT.
5. Escolha ou crie um perfil e configure as pastas da sua biblioteca.

A instalação padrão fica em `%LOCALAPPDATA%\Programs\AniT` e não requer privilégios administrativos.

### Aviso do SmartScreen

A versão 1.0.2 ainda não possui assinatura digital. Por isso, o Windows pode exibir “O Windows protegeu o computador”. Confirme que o arquivo veio da página oficial e que o SHA-256 coincide; então use **Mais informações → Executar assim mesmo**. Não ignore o aviso se o hash for diferente.

## Atualizar

1. Faça um `.anitbackup` em **Configurações → Dados e backup**.
2. Feche o AniT.
3. Baixe e execute o instalador da nova versão.
4. Instale sobre o mesmo diretório.
5. Abra o aplicativo e valide perfil, biblioteca e reprodução.

O instalador preserva `%LOCALAPPDATA%\AniT`, onde ficam perfis, banco, configurações e caches.

## Desinstalar

Remova o AniT em **Configurações do Windows → Aplicativos**. A desinstalação remove o programa, mas preserva `%LOCALAPPDATA%\AniT` para evitar perda acidental.

Para apagar tudo, primeiro exporte o que quiser manter, feche o aplicativo e remova manualmente `%LOCALAPPDATA%\AniT`. Os vídeos originais ficam fora dessa pasta e não são apagados.

## Instalação silenciosa

Para ambientes controlados:

```powershell
.\AniT-Setup-1.0.2-win-x64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
```

Os parâmetros são os padrões do Inno Setup. A instalação continua sendo feita por usuário.

## Executar pelo código-fonte

Desenvolvedores devem seguir [Ambiente de desenvolvimento](DEVELOPMENT.md). O SDK .NET e o player portátil só são necessários nessa modalidade.
