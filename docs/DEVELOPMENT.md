# Ambiente de desenvolvimento

## Pré-requisitos

- Windows 10/11 x64;
- Git;
- .NET 10 SDK;
- Visual Studio com workload de desktop .NET, VS Code ou editor equivalente;
- MPC-HC portátil para validar reprodução.

Confirme o SDK:

```powershell
dotnet --info
```

## Setup

```powershell
git clone https://github.com/PedroVitor-Dev/AniT.git
cd AniT
dotnet restore AniT.slnx
dotnet build AniT.slnx -c Debug
dotnet test AniT.slnx -c Debug --no-build
dotnet run --project src/AniT.App/AniT.App.csproj
```

Para o player, siga [Player/README.md](../Player/README.md). Binários do MPC-HC não entram no Git.

## Comandos úteis

```powershell
# Build equivalente ao CI
dotnet build AniT.slnx -c Release --no-restore

# Todos os testes
dotnet test AniT.slnx -c Release --no-build

# Teste por nome
dotnet test tests/AniT.Tests/AniT.Tests.csproj --filter "FullyQualifiedName~LibraryScanner"

# Cobertura local
dotnet test tests/AniT.Tests/AniT.Tests.csproj --collect:"XPlat Code Coverage"
```

## Dados locais de desenvolvimento

O app usa:

```text
%LOCALAPPDATA%\AniT\Data\anit.db
%LOCALAPPDATA%\AniT\Covers
```

Antes de testar migrações ou cenários destrutivos, faça uma cópia dessas pastas. Prefira uma pasta de mídia temporária, nunca sua coleção principal, ao desenvolver scanner e organizador.

## Padrões de código

- C# com nullable reference types e implicit usings habilitados.
- Prefira tipos imutáveis (`record`) para resultados e mensagens.
- I/O deve aceitar `CancellationToken` quando a operação puder demorar.
- Não bloqueie o dispatcher com `.Result`, `.Wait()` ou leitura pesada.
- Mantenha regras de negócio fora do code-behind quando forem reutilizáveis ou testáveis.
- Use contextos EF Core de vida curta em operações assíncronas.
- Nunca sobrescreva mídia do usuário; valide conflitos e mantenha rollback.
- Erros de rede em metadados devem degradar para cache/fallback, não impedir a biblioteca local.

## Padrões de interface

- Valide em 1280×720, 1920×1080 e 3440×1440, com escalas de 100%, 125% e 150%.
- Reserve espaço real para bordas arredondadas; conteúdo não deve pintar sobre o contorno.
- Texto principal precisa de contraste nos estados normal, hover, foco e desabilitado.
- Controles interativos precisam de foco de teclado visível e nome acessível.
- Não dependa apenas de cor para comunicar status.
- Evite medidas mágicas quando `Grid`, `WrapPanel` ou limites mínimos/máximos expressarem melhor a intenção.

## Estratégia de testes

Mudanças devem cobrir, conforme aplicável:

- nomes de arquivo comuns e casos-limite;
- decisões e explicações do matcher;
- migrações sem perda de vínculo;
- roots offline, arquivos movidos, duplicatas e versões;
- cache e fallback de metadados;
- prévia, conflito e rollback do organizador;
- cálculo de progresso e conclusão.

Testes que acessam disco devem criar diretórios temporários isolados e removê-los ao final.

## Commits

O projeto adota mensagens no estilo Conventional Commits:

```text
feat: add calendar view
fix: preserve progress after file move
docs: explain review workflow
test: cover multi-episode filename
refactor: isolate metadata cache
```

Use um assunto imperativo e curto. Explique motivação e trade-offs no corpo quando a alteração não for óbvia.

## Pull requests

Antes de abrir:

```powershell
dotnet restore AniT.slnx
dotnet build AniT.slnx -c Release --no-restore
dotnet test AniT.slnx -c Release --no-build
```

Inclua screenshots ou gravações para mudanças visuais e descreva as resoluções testadas. Não misture refatorações sem relação com a correção principal.
