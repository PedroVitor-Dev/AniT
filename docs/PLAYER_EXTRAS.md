# Recursos extras do player

## Baki-Pi

Assistência local por regras de estado; não envia dados a serviços de IA. Esperas
prolongadas de abertura, busca e buffering recebem explicação contextual. A indicação
de entrada abaixo do consumo só aparece quando há medição disponível. Na reprodução
normal e em pausas manuais o assistente fica recolhido. Pode ser dispensado por episódio
ou desativado no perfil. “Acumular” pausa para formar reserva; “Reproduzir” é uma ação
explícita. Nunca retoma sozinho nem promete corrigir falta de fontes.

## Momentos

O botão Momentos ou B abre a lista do episódio atual. Marcar captura um quadro sem
legendas, salva uma miniatura PNG de 320 pixels e uma anotação opcional de até 500
caracteres. É possível editar, excluir e ir ao ponto marcado. Os arquivos ficam em
`Online/<perfil>/moments.json` e `moments-thumbnails/<id>.png`, separados por perfil e
identificados pelo GUID estável do episódio. A troca/saída do episódio fecha a ferramenta
e cancela operações pendentes.

A proteção vem ligada: notas e miniaturas à frente da posição confirmada não entram
na árvore visual; a imagem protegida não é carregada. “Revelar” libera somente aquele
marcador naquela abertura da janela. Reabrir ou rebobinar volta a proteger os demais
momentos futuros. Desligar a proteção é uma escolha explícita do perfil. Trata-se de
proteção contra spoilers na interface, não criptografia dos arquivos locais.

Há limite de 200 momentos por episódio e 1000 por perfil. Metadados usam substituição
atômica e backup. A leitura admite o tamanho máximo gerado por notas Unicode escapadas.
Apagar um momento remove só sua miniatura; os vídeos originais não são alterados.

## Central de legendas

Botão próprio e entrada no menu de legendas/opções. Importa SRT, ASS, SSA e VTT por
seletor ou arraste na central e nos controles WPF do player. A superfície nativa do
vídeo não aceita arrastes que trocariam acidentalmente o episódio. Reimportar o mesmo
arquivo seleciona a faixa existente. Remover uma faixa externa não apaga o arquivo.
As importações valem para a sessão do episódio e sobrevivem à recarga técnica do índice.

Ajustes: fonte, tamanho, posição, contorno, cor e atraso. A aparência pode ser lembrada
no perfil; o atraso continua específico do episódio. O estilo ASS original é preservado
por padrão. A substituição deve ser habilitada explicitamente; Restaurar padrão volta
a respeitar o ASS. Legendas bitmap não permitem mudar a fonte. Fontes incorporadas no
MKV continuam sujeitas à disponibilidade dos anexos durante o streaming.

A central permite aplicar e conferir no vídeo mantendo a ferramenta aberta. A prévia
é aproximada para texto simples. Popup/rolagem, campos e caixas de seleção seguem os
temas claro/escuro e texto ampliado. As miniaturas usam proporção preservada, sem herdar
o corte configurado para capas da biblioteca.

## Validação

`--check-player-extras` em AniT.UiChecks testa o motor real com vídeo sintético:
assistência durante buffering, pausa sem retomada automática, captura, ocultação de
nota/imagem, revelação explícita e temporária, importação sem duplicação, SRT/ASS,
aparência, persistência durante seek e restauração do ASS. Oito configurações de
ferramentas combinam escuro/claro, texto 100/140% e janelas compactas. Os PNGs incluem
os controles de aparência rolados e o quadro renderizado com ASS original.

Os testes de unidade cobrem o relógio do assistente, isolamento/limites/backup dos
momentos, proteção de spoilers e normalização/persistência da aparência.

## Registro e anotação dos momentos

O player possui um botão de câmera para registrar a cena atual e abrir a lista;
o botão Momentos continua abrindo a consulta sem criar um registro. A captura
direta impede cliques repetidos enquanto salva e mostra confirmação na janela.
O registro com nota continua disponível por Registrar momento no rodapé.
Durante buffering/seek/início, a janela informa por que a captura deve aguardar.

A anotação tem template local PlayerNoteInput: largura integral do cartão,
altura mínima de 112 DIP, várias linhas, alinhamento à esquerda, padding de 14,
cantos arredondados, placeholder e contorno de foco. O contador de 500 caracteres
fica fora do texto. O mesmo campo é usado ao editar uma anotação existente.
O formulário/lista rola em janelas pequenas; a ação Registrar momento permanece
no rodapé. Nenhuma linha decorativa atravessa o interior do editor.

Validação com mpv real: botão de câmera registrou e abriu Momentos; o botão do
formulário salvou uma segunda cena com nota e limpou o editor. Spoilers continuam
sem texto ou miniatura antecipados. A largura real de PART_ContentHost foi
verificada nos temas claro/escuro e texto 100/140%. Oito layouts dos recursos
extras e doze layouts do player passaram. Nenhum instalador foi gerado.
