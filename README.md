# AniT
Sua Crunchyroll particular para animes armazenados no PC.

O AniT é uma biblioteca local e offline para acompanhar animes, episódios, progresso, avaliações e histórico. O player recomendado será uma versão testada e embarcada do MPC-HC; outros players externos poderão ser escolhidos nas configurações.

## Estrutura

- `AniT.Core`: regras e entidades de domínio.
- `AniT.Infrastructure`: SQLite e persistência local.
- `AniT.Player`: fronteira de integração com players; MPC-HC será o padrão.
- `AniT.App`: interface WPF.
- `AniT.Tests`: testes automatizados.

## Executar

O projeto requer o SDK .NET 10.

```powershell
dotnet run --project src/AniT.App
```

Os dados locais ficam em `%LOCALAPPDATA%\\AniT\\Data\\anit.db`.
