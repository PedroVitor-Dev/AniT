# Processo de release

Processo de preparação da distribuição 2.0.1. A publicação depende da validação do artefato e da situação da assinatura.

## Versionamento

Use [Versionamento Semântico](https://semver.org/lang/pt-BR/):

- `MAJOR`: incompatibilidade intencional;
- `MINOR`: funcionalidade compatível;
- `PATCH`: correção compatível.

A versão pública usa três componentes (`2.0.1`). Metadados internos do Windows podem representar `FileVersion` com quatro componentes (`2.0.1.0`) sem alterar o nome público.

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
.\scripts\Build-Installer.ps1 -Version 2.0.1 -CertificateThumbprint SEU_THUMBPRINT
```

A distribuição exige certificado Authenticode válido com chave privada e finalidade Code Signing em `Cert:\CurrentUser\My`. Sem certificado, o script encerra antes de limpar artefatos. Para testes locais, use explicitamente `-AllowUnsigned`; esses builds não devem ser publicados como release oficial:

```powershell
.\scripts\Build-Installer.ps1 -Version 2.0.1 -AllowUnsigned
```

O script assina e verifica `AniT.exe` e as DLLs próprias (`AniT.dll`, `AniT.*.dll`), usa SHA-256 e timestamp RFC 3161 e configura o Inno Setup para assinar o instalador e o desinstalador embutido. O instalador é verificado antes de calcular o hash. Binários de terceiros preservam suas assinaturas originais. Não coloque senha, chave privada ou arquivo `.pfx` no repositório.

O script publica `win-x64` autossuficiente, prepara assets, inclui o MPC-HC homologado e compila:

```text
artifacts\installer\AniT-Setup-2.0.1-win-x64.exe
artifacts\installer\SHA256SUMS.txt
```

Não inclua bancos, perfis, capas baixadas, logs, backups, `TestResults`, diretórios `work` ou mídia. Consulte [Assinatura digital](CODE_SIGNING.md) para obter e configurar o certificado. Não apresente um build de teste sem certificado como versão assinada.

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

Use assinatura Authenticode na distribuição oficial; prefira também tags anotadas/assinadas no Git.

## Hotfix

Parta do estado afetado, adicione teste de regressão, incremente `PATCH` e devolva a correção para `main`. Não misture funcionalidades novas. A 1.0.2 é exemplo: sincroniza o catálogo com arquivos removidos, protege raízes indisponíveis e repara o fluxo de organização.
