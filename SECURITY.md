# Política de segurança

## Versões suportadas

Enquanto o AniT estiver antes da primeira versão estável, correções de segurança serão aplicadas à branch `main`. Releases antigas e builds não oficiais não recebem garantia de suporte.

## Relatar uma vulnerabilidade

Não abra uma issue pública com detalhes exploráveis.

Use preferencialmente **Security → Report a vulnerability** neste repositório, por meio do recurso de private vulnerability reporting do GitHub. Se essa opção não estiver disponível, contate o mantenedor pelo perfil [@PedroVitor-Dev](https://github.com/PedroVitor-Dev) e compartilhe inicialmente apenas uma descrição breve, sem dados pessoais ou mídia de terceiros.

Inclua, quando possível:

- componente e commit/versão afetados;
- pré-condições e impacto;
- passos mínimos para reprodução;
- prova de conceito segura;
- mitigação sugerida;
- se o problema envolve caminhos, arquivos locais, banco ou integração externa.

## Expectativa de tratamento

O mantenedor tentará confirmar o recebimento, avaliar severidade, preparar correção e coordenar divulgação responsável. Como o projeto é mantido pela comunidade, prazos dependem de disponibilidade e complexidade; atualizações serão fornecidas no canal privado sempre que possível.

## Escopo prioritário

- perda, corrupção ou movimentação indevida de arquivos;
- path traversal e escrita fora do destino escolhido;
- execução de código ou argumentos inseguros no player;
- exposição de caminhos, avaliações ou biblioteca local;
- dependências ou atualização de artefatos comprometidas;
- consultas externas que enviem dados além do documentado.

## Fora de escopo

- engenharia social contra mantenedores;
- ataques de negação de serviço em serviços de terceiros;
- problemas exclusivamente em versões modificadas ou não suportadas;
- relatórios sem impacto reproduzível.

Não teste usando dados ou sistemas de terceiros sem autorização. Preserve arquivos originais e prefira ambientes descartáveis.
