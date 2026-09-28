# Ambiente de desenvolvimento

## Pré-requisitos

- Windows 10/11 x64;
- Git;
- .NET 10 SDK;
- Visual Studio com workload desktop .NET, VS Code ou equivalente;
- Inno Setup 7 somente para gerar o instalador;
- MPC-HC 2.8.2 portátil para validar reprodução integrada.

```powershell
dotnet --info
git --version
```

## Setup

```powershell
git clone https://github.com/PedroVitor-Dev/AniT.git
cd AniT
dotnet restore AniT.slnx --locked-mode
dotnet build AniT.slnx -c Debug --no-restore
dotnet test AniT.slnx -c Debug --no-build
dotnet run --project src/AniT.App/AniT.App.csproj
```

Siga [Player/README.md](../Player/README.md) para obter o binário homologado. Ele não entra no Git.

## Validação equivalente ao CI

```powershell
dotnet restore AniT.slnx --locked-mode
dotnet list AniT.slnx package --vulnerable --include-transitive
dotnet build AniT.slnx -c Release --no-restore
dotnet test AniT.slnx -c Release --no-build --collect:"XPlat Code Coverage"
```

O CI exige cobertura de linhas mínima de 70% e só envia o relatório quando ele foi produzido. A versão 1.0.1 foi publicada com 171 testes aprovados e 73% de cobertura de linhas.

Comandos úteis:

```powershell
dotnet test tests/AniT.Tests/AniT.Tests.csproj --filter "FullyQualifiedName~LibraryScanner"
dotnet test tests/AniT.Tests/AniT.Tests.csproj --filter "FullyQualifiedName~Backup"
dotnet test tests/AniT.Tests/AniT.Tests.csproj --filter "FullyQualifiedName~Profile"
```

## Dados locais de desenvolvimento

```text
%LOCALAPPDATA%\AniT\Data\anit.db
%LOCALAPPDATA%\AniT\Data\Profiles\<id>\anit.db
%LOCALAPPDATA%\AniT\Data\profiles.json
%LOCALAPPDATA%\AniT\Data\system-settings.json
%LOCALAPPDATA%\AniT\Covers
%LOCALAPPDATA%\AniT\Cache
```

Use perfis e pastas descartáveis. Nunca teste scanner, organizador, importação ou reparo sobre sua coleção sem backup. Não versione bancos, caches, logs, backups ou mídia.

## Padrões de código

- C# com nullable reference types e implicit usings.
- Tipos imutáveis (`record`) para resultados e mensagens quando apropriado.
- I/O demorado deve aceitar `CancellationToken`.
- Não bloqueie o dispatcher com `.Result`, `.Wait()` ou leitura pesada.
- Regras testáveis ficam fora do code-behind.
- Contextos EF Core devem ter vida curta.
- Nunca sobrescreva mídia; valide conflitos e mantenha rollback.
- Falhas online degradam para cache/fallback e não bloqueiam a biblioteca local.
- Persistência crítica usa escrita atômica, validação e cópia de recuperação.

## Padrões de interface

- Valide 1280×720, 1920×1080 e 3440×1440 em 100%, 125% e 150%.
- Teste tema escuro, claro e automático depois de reiniciar.
- Texto deve ter contraste normal, hover, foco, seleção e desabilitado.
- Controles precisam de foco visível, nome acessível e navegação por teclado.
- Não comunique estado apenas por cor.
- Imagens e mascotes devem usar enquadramento que não corte conteúdo essencial.
- Respeite redução de movimento e escalas configuradas.

## Testes esperados

Cubra conforme o risco:

- parser e matching, incluindo nomes ruidosos;
- roots offline, movimentos, duplicatas e versões;
- perfis, troca de banco, PIN e remoção de PIN;
- backup, manifesto, hash, traversal, limites e recuperação;
- cache, rede, modo offline e fontes personalizadas;
- prévia, conflito e rollback do organizador;
- progresso, conclusão, notificações e conquistas;
- serialização e migrações compatíveis.

Testes de disco devem criar diretórios temporários isolados e limpá-los.

## Instalador local

```powershell
winget install --id JRSoftware.InnoSetup.7 -e --source winget
.\scripts\Build-Installer.ps1 -Version 1.0.1
```

Os artefatos ficam em `artifacts/installer/`. Consulte [Release](RELEASING.md) antes de distribuir.

## Commits e pull requests

Use Conventional Commits, por exemplo `feat:`, `fix:`, `docs:`, `test:` e `chore:`. Antes do PR, execute validação em Release, atualize documentação e inclua evidência visual para WPF. Não misture refatorações sem relação.
