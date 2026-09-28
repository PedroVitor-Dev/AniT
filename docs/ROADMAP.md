# Roadmap

Este documento comunica direção, não datas garantidas. O ponto de partida é a versão estável **1.0.1**.

## Entregue em 1.0.x

- [x] Instalador Windows x64 autossuficiente, personalizado e por usuário.
- [x] Biblioteca SQLite com múltiplas pastas, scan incremental e subpastas.
- [x] Parsing configurável, revisão, duplicatas, versões e arquivos indisponíveis.
- [x] Busca por títulos, aliases, episódios e gêneros; filtros combinados.
- [x] Metadados e artes de múltiplas fontes, cache, SFW e modo offline.
- [x] Home configurável, Explorar por gêneros, calendário e histórico.
- [x] MPC-HC integrado, retomada, checkpoints e conclusão configurável.
- [x] Favoritos, tags, coleções, avaliações e 100 conquistas.
- [x] Temas escuro/claro/automático, escala, mascotes e redução de movimento.
- [x] Múltiplos perfis locais com seletor inicial e PIN opcional.
- [x] Backup `.anitbackup`, importação validada e religação no novo PC.
- [x] Central de configurações com onze categorias.
- [x] CI com restore travado, auditoria de dependências, testes e cobertura mínima.

## Próximas correções e qualidade

- [ ] Assinar digitalmente instalador e executáveis quando houver certificado.
- [ ] Ampliar automação de interface WPF para fluxos críticos.
- [ ] Realizar auditoria externa de acessibilidade e navegação por teclado.
- [ ] Testar matriz maior de GPUs, escalas e versões do Windows.
- [ ] Adicionar atualização segura dentro do aplicativo, com confirmação do usuário.
- [ ] Melhorar diagnóstico guiado para banco, player e fontes online.

## Evolução de produto

- [ ] Build portátil oficial além do instalador.
- [ ] Localização completa da interface e documentação em inglês.
- [ ] Contrato estável para provedores de metadados e artes.
- [ ] Mais players externos com capacidades declaradas.
- [ ] Sincronização opcional e criptografada entre computadores, sem abandonar o modo local-first.
- [ ] Melhorias contínuas no matching e na religação de bibliotecas migradas.

## Fora de escopo

- streaming, hospedagem ou download de mídia protegida;
- sincronização obrigatória em nuvem;
- telemetria sem consentimento explícito;
- exclusão automática de duplicatas;
- reorganização silenciosa de arquivos.

## Como propor algo

Abra uma issue de proposta, descreva o problema e o público, indique impacto em privacidade/arquivos/compatibilidade e apresente a menor solução útil. Uma ideia no roadmap não dispensa alinhamento de escopo e arquitetura.
