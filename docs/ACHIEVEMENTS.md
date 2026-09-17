# Sistema de conquistas

O AniT possui 100 conquistas persistentes, retroativas e locais. Elas dão contexto à jornada do usuário sem introduzir moeda, compra ou monetização. O total obtido é chamado de **AniPoints** e funciona como um placar pessoal no estilo Gamerscore.

## Organização

As conquistas usam IDs estáveis de 1 a 100 e estão distribuídas entre:

- primeiros passos;
- consistência;
- maratona;
- avaliações;
- biblioteca;
- gêneros;
- tempo assistido;
- rankings;
- perfil e backup;
- secretas e lendárias.

Marcos relacionados formam cadeias de progressão. Cada marco continua sendo uma conquista independente, com data, pontuação, imagem e notificação próprias. A interface resume a cadeia, mas nunca apaga as etapas anteriores.

## AniPoints

AniPoints não são moeda, nível nem item consumível. A pontuação é calculada somente a partir das conquistas desbloqueadas:

| Raridade | Pontos |
| --- | ---: |
| Comum | 10 |
| Incomum | 20 |
| Rara | 40 |
| Épica | 80 |
| Secreta | 100 |
| Lendária | 150 |
| Lendária suprema | 500 |

O total nunca é salvo separadamente. Isso evita divergência: ele é sempre a soma do histórico persistido.

## Conquistas secretas

As conquistas 91 a 100 quebram deliberadamente o padrão visual. Antes do desbloqueio, as secretas aparecem como `???`, sem nome, descrição, condição ou arte. Após o desbloqueio, a definição e o Baki-Pi correspondente são revelados.

A conquista 100 é o encerramento da jornada atual: exige as outras 99 e usa raridade lendária suprema, arte e apresentação próprias.

## Fluxo técnico

```text
ação do usuário
  → evento de domínio
  → atualização/reconstrução das métricas
  → AchievementEngine
  → estado + histórico no SQLite
  → fila de notificações
  → popup não ativável
```

- `AchievementCatalog` é a fonte de verdade das 100 definições.
- `AchievementEngine` é determinístico, independente da interface e idempotente.
- `AchievementService` reconstrói o que for possível a partir da biblioteca, episódios, progresso, avaliações e histórico, além de persistir métricas de eventos que não podem ser deduzidas.
- `AchievementNotificationQueue` serializa os popups para que vários desbloqueios retroativos não se sobreponham.
- o popup é uma janela sem ativação e não rouba o foco do player.

## Persistência

O esquema 4 do banco local acrescenta:

- `UserAchievements`: progresso atual, desbloqueio, data e estado da notificação;
- `AchievementHistory`: registro imutável do primeiro desbloqueio e dos AniPoints recebidos;
- `AchievementMetrics`: contadores de eventos que não existem naturalmente nas entidades da biblioteca.

Ao atualizar uma instalação anterior, as tabelas e índices são criados automaticamente. O recálculo é retroativo: conquistas compatíveis com os dados já existentes são concedidas uma única vez.

## Eventos e métricas

As métricas deriváveis são lidas diretamente do banco: episódios iniciados/concluídos, animes da biblioteca, favoritos, notas, críticas, tempo assistido, dias ativos e sequências. Eventos explícitos cobrem sessões, perfil, avatar, backup, restauração, importação e navegação para o próximo episódio.

O tempo da conquista secreta noturna é contado por checkpoints reais do player entre 00:00 e 06:00. Saltos manuais no vídeo são limitados pelo tempo de parede entre checkpoints, evitando progresso artificial.

## Adaptações ao modelo atual

- A avaliação existente no AniT usa cinco Baki-Pi. Por isso, a condição textual “mudar de 1 para 10” é detectada tecnicamente como **1 estrela para 5 estrelas**, mantendo o mesmo significado na escala disponível.
- O asset fornecido para a conquista 93 não possui extensão. Ele é empacotado como recurso pelo wildcard de badges e identificado pelo conteúdo da imagem.
- As conquistas 71 e 72 usam a mesma arte fornecida, pois não há um segundo asset distinto na pasta atual.
- Métricas de listas personalizadas, tags, gêneros persistidos e rankings próprios permanecem em zero até que uma ação real desses módulos seja registrada. O motor não inventa progresso para recursos ainda não utilizados.

## Interface e acessibilidade

A central permite filtrar por status, categoria e raridade, ordenar a coleção, abrir detalhes e recalcular o progresso. O perfil mostra AniPoints e um resumo da jornada. O som do desbloqueio pode ser ativado, desativado e ter o volume ajustado.

Cor nunca é o único indicador: textos, ícones, porcentagens e estados explícitos acompanham as variações visuais. Em `DEBUG`, a central exibe um laboratório para simular métricas, desbloqueios e popups sem alterar o comportamento da versão publicada.

## Testes

Os testes cobrem o catálogo de 100 IDs/códigos únicos, cadeias, limites, reset de sequência, secretas, conquista máxima, idempotência e migração/persistência SQLite. Execute:

```powershell
dotnet test AniT.slnx
```

Ao adicionar uma conquista, mantenha o ID e o código estáveis, acrescente o asset correspondente e crie um teste de limite. Alterações de regra devem atualizar este documento no mesmo pull request.
