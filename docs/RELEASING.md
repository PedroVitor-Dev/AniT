# Processo de release

Este é o checklist proposto para releases oficiais. Enquanto não houver artefatos publicados, ele funciona como contrato de qualidade para preparar o primeiro pacote.

## Versionamento

Use [Versionamento Semântico](https://semver.org/lang/pt-BR/):

- `MAJOR`: incompatibilidade intencional;
- `MINOR`: funcionalidade compatível;
- `PATCH`: correção compatível.

Antes de `1.0.0`, mudanças significativas podem ocorrer em versões `0.x`, mas devem ser descritas claramente.

## Preparação

1. Crie uma branch de release a partir de `main` limpa.
2. Atualize `CHANGELOG.md`, README, manual e roadmap.
3. Confirme licenças dos assets e componentes distribuídos.
4. Confirme a versão homologada do MPC-HC e inclua seus avisos/licença.
5. Valide migração a partir de um banco da versão anterior usando cópia descartável.

## Validação obrigatória

```powershell
dotnet restore AniT.slnx
dotnet build AniT.slnx -c Release --no-restore
dotnet test AniT.slnx -c Release --no-build
```

Checklist manual:

- primeira execução sem banco;
- scan com subpastas;
- pasta vazia e pasta offline;
- revisão de associação ambígua;
- atualização online e fallback offline;
- retomada e conclusão no player;
- avaliação por episódio;
- prévia de organização sem execução;
- operação de organização em mídia descartável;
- 1280×720, 1920×1080 e 3440×1440;
- escala de 100%, 125% e 150%;
- navegação por teclado nos fluxos críticos.

## Empacotamento

O pacote deve conter o aplicativo e, quando aplicável, o diretório `Player/MPC-HC`. Não inclua banco, capas, logs, mídia de teste ou conteúdo da pasta `work`.

Gere hashes SHA-256 dos artefatos publicados e registre o comando/ambiente usados para permitir reprodução.

## Publicação

1. Faça merge do pull request de release.
2. Crie uma tag assinada `vX.Y.Z`.
3. Publique GitHub Release com notas migradas do changelog.
4. Anexe artefatos e hashes.
5. Execute um smoke test usando somente os artefatos publicados.
6. Abra issues separadas para problemas conhecidos; não esconda limitações nas notas.

## Hotfix

Correções urgentes devem partir da tag afetada, conter teste de regressão e voltar para `main`. Não misture novas funcionalidades em hotfix.
