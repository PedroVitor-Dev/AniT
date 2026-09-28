# Política de segurança

## Versões suportadas

| Versão | Suporte |
| --- | --- |
| 1.0.2 | Sim |
| 1.0.1 e anteriores | Atualize antes de relatar |
| Builds não oficiais/modificados | Não garantido |

Correções de segurança são preparadas em privado quando necessário, integradas à `main` e publicadas em uma nova versão suportada.

## Relatar uma vulnerabilidade

Não abra issue pública com detalhes exploráveis. Use **Security → Report a vulnerability** no GitHub. Se o recurso não estiver disponível, contate [@PedroVitor-Dev](https://github.com/PedroVitor-Dev) com uma descrição breve e sem dados pessoais.

Inclua, quando possível:

- versão/commit e componente afetado;
- pré-condições, impacto e passos mínimos;
- prova de conceito segura;
- mitigação sugerida;
- relação com caminhos, mídia, banco, backup, PIN, player ou fonte externa.

## Escopo prioritário

- perda, corrupção ou movimentação indevida de arquivos;
- path traversal, extração insegura ou escrita fora do destino;
- execução de comandos/argumentos inseguros no player;
- exposição de biblioteca, caminhos, perfis, PIN, histórico ou backup;
- evasão de validação de fontes HTTPS e acesso a redes privadas;
- pacote, instalador, atualização ou dependência comprometidos;
- consultas externas que enviem mais dados que o documentado.

## Expectativa de tratamento

O mantenedor tentará confirmar recebimento, avaliar severidade, preparar correção, adicionar regressão e coordenar a divulgação. Prazos dependem de complexidade e disponibilidade, mas o canal privado será mantido sempre que possível.

## Fora de escopo

- engenharia social;
- negação de serviço contra terceiros;
- problemas somente em builds alterados/não suportados;
- relatórios sem impacto reproduzível;
- recuperação de PIN local sem acesso autorizado ao perfil do Windows.

Não teste dados ou sistemas de terceiros sem autorização. Use mídia descartável e preserve originais.

## Integridade de releases

Releases oficiais ficam em `github.com/PedroVitor-Dev/AniT/releases` e incluem `SHA256SUMS.txt`. A versão 1.0.2 ainda não possui assinatura digital; o hash reduz o risco de corrupção, mas não substitui Authenticode. Não execute um instalador cujo hash divergir.
