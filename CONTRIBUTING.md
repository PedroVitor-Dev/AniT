# Contribuindo com o AniT

Obrigado por querer melhorar o AniT. Contribuições são bem-vindas em código, design, documentação, testes, acessibilidade e triagem.

Ao participar, você concorda com o [Código de Conduta](CODE_OF_CONDUCT.md).

## Antes de começar

- Para dúvidas de uso, consulte [SUPPORT.md](SUPPORT.md).
- Para vulnerabilidades, não abra issue pública; siga [SECURITY.md](SECURITY.md).
- Procure issues e pull requests existentes para evitar trabalho duplicado.
- Mudanças grandes devem começar com uma proposta de issue.

## Encontrando uma contribuição

Boas primeiras tarefas:

- ampliar casos do parser de nomes;
- adicionar testes de regressão;
- corrigir documentação;
- melhorar contraste, foco e navegação por teclado;
- validar layout em diferentes resoluções e escalas;
- tornar mensagens de erro mais acionáveis.

Issues adequadas para iniciantes podem receber a label `good first issue`; tarefas que precisam de apoio podem usar `help wanted`.

## Ambiente

Siga [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). O ciclo básico é:

```powershell
dotnet restore AniT.slnx
dotnet build AniT.slnx -c Release --no-restore
dotnet test AniT.slnx -c Release --no-build
```

## Fluxo de contribuição

1. Faça fork do repositório.
2. Crie uma branch a partir de `main`:

   ```powershell
   git switch -c feat/nome-curto
   ```

3. Implemente uma alteração focada.
4. Adicione ou atualize testes.
5. Atualize documentação afetada.
6. Execute build e testes em Release.
7. Faça commits claros.
8. Abra um pull request preenchendo o template.

## Escopo e design

- Resolva um problema por pull request.
- Prefira evolução incremental a grandes reescritas.
- Preserve o princípio local-first.
- Não mova, renomeie ou exclua mídia sem confirmação e prévia.
- Integrações externas devem falhar de forma segura e manter o modo offline.
- Mudanças visuais precisam funcionar em resoluções e escalas diferentes.
- Não adicione telemetria, conta ou envio de dados sem discussão explícita.

## Testes esperados

Uma correção de bug deve incluir teste de regressão quando a lógica for testável. Recursos de scanner, banco ou organizador precisam cobrir o caminho feliz e os modos de falha relevantes.

Para mudanças visuais, inclua:

- antes/depois;
- resolução e escala usadas;
- estados normal, hover, foco e desabilitado quando aplicável;
- confirmação de que conteúdo e bordas não são recortados.

## Commits

Use Conventional Commits:

```text
feat: add persistent favorites
fix: avoid duplicate review entries
docs: expand backup instructions
test: cover offline library root
chore: update CI action
```

Evite commits genéricos como `changes`, `fix` ou `update`.

## Pull request pronto para revisão

Um PR está pronto quando:

- explica o problema e a solução;
- mantém o escopo pequeno;
- compila sem erros;
- passa nos testes;
- inclui evidência visual quando necessário;
- atualiza documentação;
- não inclui binários, banco local, capas baixadas, logs ou mídia pessoal;
- informa riscos, migrações e limitações conhecidas.

Mantenedores podem pedir divisão do PR, testes adicionais ou alinhamento de arquitetura. Feedback técnico deve discutir a mudança, nunca a pessoa.

## Licença das contribuições

Ao enviar uma contribuição, você concorda que ela será distribuída sob a licença [GPL-3.0](LICENSE) do projeto e declara ter direito de submetê-la.
