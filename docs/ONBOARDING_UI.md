# Onboarding Fluent

O onboarding usa WPF-UI 4.3.0, fixado no lock, somente nesta janela. `ThemesDictionary` e `ControlsDictionary` são recursos locais de `OnboardingWindow`; não entram em `App.xaml`. As outras telas conservam os estilos existentes.

O adapter `OnboardingWindow.Appearance.cs` aplica claro/escuro/automático, acento, escala de interface/texto e cantos. `AppearanceManager` encaminha exclusivamente essa janela ao adapter e evita reaplicar a escala legada aos textos de seus templates. Não há aplicação global de tema WPF-UI nem alteração do framework das janelas. Este piloto não adiciona Mica, transparência ou animações próprias.

A lateral mostra marca, mascote e etapas da configuração. Foram removidos os slogans e substituídos os títulos e descrições por instruções de perfil, pasta e revisão. O logo colorido usa um viewport da imagem existente, sem modificar o arquivo original.

Escolha de foto, nome opcional, pasta, pular pasta, revisão e salvamento continuam nos handlers existentes. Os testes de UI usam perfil fictício e nunca concluem o salvamento nem abrem banco, scanner ou player.

## Validação

- Restore com lock e build Release, sem avisos.
- 144 combinações de layout, quatro etapas cada: claro, escuro e automático; escala de interface/texto; viewports lógicos grandes e compactos.
- Verificações de controles Fluent, isolamento dos recursos globais, escala idempotente, campo de nome preservado ao voltar e avanço explícito sem pasta.
- Regressão dos recortes da Home e 183 testes funcionais.
- O teste de tamanhos lógicos não substitui a validação de DPI físico em outros computadores.

Prévia sem instalação: `dotnet run --project tests/AniT.UiChecks -c Release -- --preview` (escuro) ou acrescente `--light` (claro).

Licença e avisos do WPF-UI acompanham a saída em `Licenses/WpfUi/`. Não gerar instalador nem enviar alterações ao GitHub sem solicitação do mantenedor.


## Primeira abertura e instância única

O primeiro passo é exibido antes da criação do banco, da preparação do cache de artes, das conquistas e do player. O banco é preparado fora da thread da interface quando o usuário conclui a configuração; fechar antes mantém o marcador pendente e retoma o assistente na próxima abertura. Não há varredura da biblioteca antes do onboarding.

Uma segunda abertura pelo atalho avisa a instância existente para ativar a janela, em vez de iniciar outra interface sobre os mesmos perfis e banco. Reinícios após troca de perfil/restauração aguardam a instância anterior encerrar. A preparação do SQLite também usa exclusão por caminho entre processos, incluindo a cópia de segurança legada e a criação/atualização do esquema. Bancos na versão atual não repetem verificações de colunas nem criam backups de migração desnecessários em cada contexto.

O log local mostrou `table "AchievementHistory" already exists` em `EnsureCreated`, compatível com inicializações concorrentes. A verificação de integridade do banco atual passou; não foram apagados nem reparados dados do usuário. Erros fatais na inicialização agora encerram a instância, em vez de deixá-la invisível em execução.

Validação: compilação Release sem avisos, 302 testes funcionais (incluindo inicialização concorrente e preservação de dados), testes de ativação/reinício entre processos e onboarding sem banco/player antes do primeiro frame. Use `--check-startup` e `--check-onboarding-only` no executável de `AniT.UiChecks`. A medição local de primeiro frame após começar a construir a janela foi de 461 ms, excluindo o início do processo e do WPF; não é um tempo garantido de abertura em outros computadores. O script do Inno foi compilado com um payload mínimo isolado, sem instalar ou substituir o pacote de distribuição.
