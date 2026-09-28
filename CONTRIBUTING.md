# Contribuindo com o AniT

Contribuições em código, design, documentação, testes, acessibilidade e triagem são bem-vindas. Ao participar, você concorda com o [Código de Conduta](CODE_OF_CONDUCT.md).

## Antes de começar

- Dúvidas de uso: [SUPPORT.md](SUPPORT.md).
- Vulnerabilidades: [SECURITY.md](SECURITY.md), nunca issue pública.
- Pesquise issues e PRs existentes.
- Discuta mudanças grandes antes de implementar.
- Leia [Arquitetura](docs/ARCHITECTURE.md) e [Desenvolvimento](docs/DEVELOPMENT.md).

## Fluxo

1. Faça fork e crie uma branch a partir de `main`.
2. Implemente uma alteração focada.
3. Adicione testes de regressão proporcionais ao risco.
4. Atualize manual, configurações, privacidade, changelog ou arquitetura quando afetados.
5. Execute a validação em Release.
6. Abra o PR com contexto, riscos e evidências.

```powershell
git switch -c feat/nome-curto
dotnet restore AniT.slnx --locked-mode
dotnet build AniT.slnx -c Release --no-restore
dotnet test AniT.slnx -c Release --no-build --collect:"XPlat Code Coverage"
```

## Princípios

- Preserve o modelo local-first e o modo offline.
- Não mova, renomeie ou exclua mídia sem prévia e confirmação.
- Não sobrescreva destinos; mantenha rollback quando aplicável.
- Valide qualquer conteúdo externo antes de usar.
- Não adicione telemetria, conta ou nuvem sem discussão e consentimento.
- Persistência de perfil, PIN e backup exige teste de reinicialização/recuperação.
- Mudanças públicas exigem documentação no mesmo PR.

## Testes e segurança

O CI usa restore travado, auditoria de pacotes, build Release, testes e cobertura mínima de 70%. Scanner, banco, backup, perfis, fontes e organizador precisam cobrir caminho feliz e falhas relevantes. Use diretórios e bancos temporários; nunca a coleção pessoal.

Não inclua binários, instaladores, banco, perfil, capas baixadas, logs, backups, resultados de teste ou mídia pessoal no PR.

## Interface

Inclua antes/depois e valide:

- 1280×720, 1920×1080 e 3440×1440;
- escala 100%, 125% e 150%;
- temas escuro, claro e automático;
- estados normal, hover, foco, seleção e desabilitado;
- teclado, contraste, redução de movimento e imagens sem corte.

## Commits

Use Conventional Commits:

```text
feat: add library filter
fix: persist removed profile pin
docs: document backup migration
test: cover malformed backup manifest
chore: update CI action
```

## PR pronto para revisão

O PR explica problema e solução, passa no CI, mantém escopo pequeno, inclui evidências, documenta migração/limitações e não altera dados reais. Mantenedores podem pedir divisão, testes ou alinhamento arquitetural.

## Licença

Contribuições são distribuídas sob [GPL-3.0](LICENSE). Ao enviar, você declara ter direito de contribuir com o material.
