# Processo de release

Este processo foi validado na publicação da versão 1.0.1.

## Versionamento

Use [Versionamento Semântico](https://semver.org/lang/pt-BR/):

- `MAJOR`: incompatibilidade intencional;
- `MINOR`: funcionalidade compatível;
- `PATCH`: correção compatível.

A versão pública usa três componentes (`1.0.1`). Metadados internos do Windows podem representar `FileVersion` com quatro componentes (`1.0.1.0`) sem alterar o nome público.

## Preparação

1. Crie uma branch a partir de `main` limpa e abra PR.
2. Atualize versão, `CHANGELOG.md` e documentação.
3. Confirme licenças de assets e componentes.
4. Confirme a versão/hash homologados do MPC-HC.
5. Valide migração, perfil/PIN e importação de backup a partir da versão anterior.

## Validação automatizada

```powershell
dotnet restore AniT.slnx --locked-mode
dotnet list AniT.slnx package --vulnerable --include-transitive
dotnet build AniT.slnx -c Release --no-restore
dotnet test AniT.slnx -c Release --no-build --collect:"XPlat Code Coverage"
```

O CI em `.github/workflows/ci.yml` executa restore travado, auditoria NuGet, build, testes e piso de 70% de cobertura.

## Checklist manual

- instalação limpa, atualização e desinstalação preservando dados;
- primeira execução, seleção/criação de perfil, PIN correto/incorreto/removido;
- scan com subpastas, pasta vazia, disco offline e revisão ambígua;
- busca por título, alias, gênero e número de episódio;
- filtros combinados e painel recolhível da Biblioteca;
- fontes online, cache, SFW e modo totalmente offline;
- retomada, checkpoints, conclusão e próximo episódio;
- calendário, histórico, avaliação, conquistas e notificações;
- organização/renomeação somente após prévia, sem sobrescrita;
- exportação, prévia, importação e religação de `.anitbackup`;
- temas escuro, claro e automático após reiniciar;
- 1280×720, 1920×1080 e 3440×1440 em 100%, 125% e 150%;
- teclado, foco, contraste, redução de movimento e imagens sem corte.

## Gerar o instalador

Instale Inno Setup 7 e execute:

```powershell
.\scripts\Build-Installer.ps1 -Version 1.0.1
```

O script publica `win-x64` autossuficiente, prepara assets, inclui o MPC-HC homologado e compila:

```text
artifacts\installer\AniT-Setup-1.0.1-win-x64.exe
artifacts\installer\SHA256SUMS.txt
```

Não inclua bancos, perfis, capas baixadas, logs, backups, `TestResults`, diretórios `work` ou mídia. Na ausência de certificado, declare claramente que o instalador não é assinado.

## Smoke test do artefato

1. Verifique o SHA-256 do instalador gerado.
2. Instale em um usuário/ambiente limpo.
3. Confirme ícone, idiomas, atalhos, perfil inicial e MPC-HC.
4. Atualize sobre a versão anterior e confirme preservação de `%LOCALAPPDATA%\AniT`.
5. Desinstale e confirme que dados pessoais foram preservados.
6. Opcionalmente valide modo silencioso com `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.

## Publicação no GitHub

1. Faça merge do PR de release.
2. Crie a tag `vX.Y.Z` apontando exatamente para o commit de `main` aprovado.
3. Crie uma GitHub Release com notas derivadas do changelog.
4. Anexe instalador e `SHA256SUMS.txt`.
5. Baixe novamente os anexos públicos e repita hash/smoke test.
6. Marque como latest somente depois da validação.

Quando houver suporte de assinatura, prefira tag anotada/assinada e assinatura Authenticode dos binários.

## Hotfix

Parta do estado afetado, adicione teste de regressão, incremente `PATCH` e devolva a correção para `main`. Não misture funcionalidades novas. A 1.0.1 é exemplo: corrigiu a persistência da remoção do PIN e a arte de episódio sem avaliação.
