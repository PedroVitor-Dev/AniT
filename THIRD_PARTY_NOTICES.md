# Avisos de terceiros

O AniT é distribuído sob a GNU General Public License v3.0. O instalador oficial 1.0.1 inclui os componentes de execução abaixo, que permanecem sujeitos às respectivas licenças e direitos autorais.

## Componentes de execução

| Componente | Versão homologada | Licença | Uso |
| --- | --- | --- | --- |
| Microsoft.EntityFrameworkCore.Sqlite e dependências Microsoft.Data.Sqlite | 10.0.12 | MIT | Persistência SQLite via Entity Framework Core |
| SQLitePCLRaw | 2.1.12 | Apache-2.0 | Integração nativa com SQLite |
| MPC-HC e componentes incluídos em sua distribuição portátil | 2.8.2 | GPL-3.0 e licenças indicadas pelo projeto upstream | Reprodução integrada |

O texto de licença fornecido com o MPC-HC deve permanecer no diretório `Player/MPC-HC/COPYING.txt` de todo pacote do AniT. Consulte também o [repositório oficial do MPC-HC](https://github.com/clsid2/mpc-hc).

## Dependências usadas somente em desenvolvimento e testes

| Componente | Licença |
| --- | --- |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |
| xUnit e xunit.runner.visualstudio | Apache-2.0 |

## Serviços online

AniList, MyAnimeList/Jikan, Kitsu, Wallhaven, Danbooru, Safebooru, Gelbooru e MyMemory não são incorporados ao executável. O AniT apenas realiza consultas diretas quando os recursos correspondentes estão habilitados. Dados e imagens retornados permanecem sujeitos aos termos e direitos dos respectivos provedores.

## Identidade visual

Os elementos de marca, interface, Baki-Pi e badges presentes em `assets/` foram fornecidos ao projeto pelo mantenedor. Antes de cada publicação, o mantenedor deve confirmar que conserva a autoria, licença compatível ou autorização necessária para cada recurso distribuído. Capas e fan arts obtidas em tempo de execução não são incluídas no pacote do AniT.
