# Fase 1 — Bandeja e janelinha

**Status:** análise aprovada pelo usuário em 18/11/2026 (P1 e P2 conforme a recomendação); implementada em 18/11/2026; conferida pelo Opus em 18/11/2026; teste do usuário (6.3) aprovado em 18/11/2026
**Implementação:** Sonnet (`medium`) · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também `docs/PLANO.md` (decisões A1 a A8). O repositório ainda não tem código: esta fase cria tudo do zero. Pasta do repositório: `C:\Projetos\Mixer de Audio` (branch `main`, sem commits; **não commitar**).

## 1. Objetivo

Criar o app vazio, mas com o comportamento de bandeja definitivo: ele inicia sem janela, fica só com o ícone na bandeja, abre a janelinha acima do ícone com um clique, fecha ao clicar fora ou com Esc, e libera a janela ao fechar. A fase também mede a memória e a CPU de um app WinUI 3 parado, porque a meta de ~50 MB é o maior risco do projeto e precisa ser conhecida antes da Fase 2.

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | O PDF pede app **empacotado (MSIX)**. | Resolvida (18/11/2026): sem MSIX, como o MeuBloco (A2). |
| P2 | Nomes. | Resolvida (18/11/2026): projeto e `.exe` `MixerDeAudio`; nome exibido "Mixer de Áudio". |

## 3. Referência local

O projeto `C:\Projetos\Notas e Kanban` (MeuBloco) já usa o mesmo ambiente. Copie dele o que servir, sem alterar nada lá:
- `MeuBloco\MeuBloco.csproj`: propriedades WinUI sem MSIX, self-contained, x64/ARM64, `PublishReadyToRun`, `DISABLE_XAML_GENERATED_MAIN`.
- `MeuBloco\Program.cs`: `Main` próprio com `AppInstance.FindOrRegisterForKey` e redirecionamento para a primeira cópia.
- `MeuBloco.Tests\MeuBloco.Tests.csproj`: xUnit com os arquivos de lógica pura entrando por link.
- `tools\testes\lib3.ps1`: UI Automation, `PrintWindow`, abrir e fechar o app.
- `tools\icone\gerar-icone.cs`: geração do `.ico`.

Ambiente: .NET SDK 10.0.402, Windows App SDK 2.5.1 no cache do NuGet, Visual Studio Community 2026, Windows 11 build 26200. Não há template `dotnet new winui`: crie os arquivos à mão.

## 4. Decisões

### 4.1 Projeto

| # | Decisão |
|---|---|
| D1 | Solução `MixerDeAudio.slnx` com `MixerDeAudio\` (app) e `MixerDeAudio.Tests\` (xUnit, `net10.0`, mesmas versões do MeuBloco). |
| D2 | `.csproj` do app como o do MeuBloco: `net10.0-windows10.0.22621.0`, `TargetPlatformMinVersion` **10.0.22000.0** (só Windows 11), `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `EnableMsixTooling=true`, `Platforms x64;ARM64` (só o x64 é testado), `PublishReadyToRun`, `Nullable`, `<Version>0.1.0</Version>`, `ApplicationIcon`. Pacotes: `Microsoft.WindowsAppSDK` 2.5.1, `Microsoft.Windows.SDK.BuildTools` (mesma versão do MeuBloco), `H.NotifyIcon.WinUI` (última estável; confira no NuGet e registre a versão aqui). Nada de NAudio nem de MVVM nesta fase. |
| D3 | `app.manifest` com DPI `PerMonitorV2` e `supportedOS` do Windows 10/11. |
| D4 | `.gitignore` de .NET (`bin/`, `obj/`, `.vs/`, `publicado/`, `*.user`). `README.md` curto (o que é, como compilar, link para `docs/PLANO.md`). |
| D5 | `docs/ESTADO.md`: comandos de build, testes, publish e roteiro (seção 6), e o resultado da medição. |

### 4.2 Ciclo de vida

| # | Decisão |
|---|---|
| D6 | `Program.cs` próprio (`DISABLE_XAML_GENERATED_MAIN`): `WinRT.ComWrappersSupport.InitializeComWrappers()`, instância única com a chave `MixerDeAudio`; a segunda cópia redireciona a ativação (com a linha de comando) para a primeira e sai sem criar nada. |
| D7 | O app **não tem janela principal**. `App.OnLaunched` cria só o ícone da bandeja e define `DispatcherShutdownMode = OnExplicitShutdown`, para o processo não terminar quando a janelinha fecha. Se essa propriedade não existir no SDK 2.5.1, registre aqui a alternativa usada. |
| D8 | **Linha de comando** (`Services\CommandLine.cs`, lógica pura, sem WinUI): sem argumento = abrir (como o clique no ícone); `--abrir`; `--abrir-sem-foco`, `--fechar` e `--sair` (para os roteiros); `--sem-aparar` (desliga D17, para a medição). Ignora o caminho do `.exe` (com ou sem aspas) e maiúsculas; um argumento desconhecido não faz nada. Vale tanto na primeira cópia quanto na redirecionada. Exceção: a primeira cópia aberta **sem argumento** (ex.: pelo "Iniciar com o Windows") só cria o ícone, sem abrir a janela. |
| D9 | **"Sair"** (menu ou `--sair`): fecha a janela, faz `Dispose` do ícone (para não sobrar ícone fantasma na bandeja) e chama `Application.Current.Exit()`. |
| D10 | Log mínimo em `%LOCALAPPDATA%\MixerDeAudio\logs\mixer.log` (`Services\AppPaths.cs`, `Services\Log.cs`): uma linha por evento (`iniciado`, `janela aberta`, `janela fechada (<motivo>)`, `saindo`) e as exceções não tratadas (`Application.UnhandledException`, `AppDomain.UnhandledException`). Gravação síncrona e curta, sem timer. Ao iniciar, se o arquivo passar de 1 MB, renomeie para `mixer.old.log`. |

### 4.3 Ícone da bandeja

| # | Decisão |
|---|---|
| D11 | `Tray\TrayIconService.cs` cria o `TaskbarIcon` do H.NotifyIcon pelo código (sem XAML) e chama `ForceCreate`. Dica: "Mixer de Áudio". Ícone: `Assets\MixerDeAudio.ico` (16 a 256 px), gerado por `tools\icone\gerar-icone.cs` (adaptado do MeuBloco): quadrado de cantos arredondados azul (#2563EB) com um alto-falante branco, visível em barra clara e escura. |
| D12 | **Clique esquerdo** alterna a janela (abre se fechada, fecha se aberta, com a regra D16). **Botão direito**: menu nativo (`ContextMenuMode.PopupMenu`, o mais leve) com "Abrir" e "Sair". |
| D13 | **Modo de eficiência** do Windows (folha verde no Gerenciador de Tarefas): ligado com a janela fechada e desligado enquanto ela está aberta (`EfficiencyModeUtilities` do H.NotifyIcon). A Fase 2 confere que os eventos de áudio continuam chegando na hora. |

### 4.4 Janelinha

| # | Decisão |
|---|---|
| D14 | `Flyout\FlyoutWindow.xaml(.cs)`: **criada a cada abertura e liberada ao fechar** (no `Closed`: desinscrever os eventos e zerar a referência guardada pelo `App`). Tamanho fixo nesta fase: **360 × 480 DIP**. Conteúdo provisório: título "Mixer de Áudio" (`AutomationId FlyoutTitle`) e o texto "Os controles de volume chegam na próxima fase." O elemento raiz tem `AutomationId FlyoutRoot`. Tema do sistema. |
| D15 | **Aparência:** `MicaBackdrop` (pedido do PDF; comparar com Acrylic na Fase 6). Sem barra de título: `OverlappedPresenter` com `SetBorderAndTitleBar(true, false)`, `ExtendsContentIntoTitleBar = true`, não redimensionável, sem minimizar e maximizar, `IsAlwaysOnTop`. Cantos arredondados: `DwmSetWindowAttribute(DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2)`. Fora da barra de tarefas e do Alt+Tab: `AppWindow.IsShownInSwitchers = false`. **Risco:** o WinUI às vezes deixa uma faixa no topo sem a barra de título; conferir na captura do caso 1.2. |
| D16 | **Fechar:** ao perder o foco (`Activated` com `Deactivated`), com Esc e pelo `--fechar`. **Regra de alternância** (`Flyout\FlyoutToggleGate.cs`, lógica pura, relógio injetável): clicar no ícone com a janela aberta faz a janela perder o foco e fechar antes de o clique chegar. Por isso, um pedido de abrir até **400 ms** depois de um fechamento **por perda de foco** é ignorado. Fechar por Esc, `--fechar` ou menu não bloqueia nada. |
| D17 | **Depois de fechar:** 2 s depois (um único `DispatcherQueueTimer` de disparo único, sem repetição), se a janela continuar fechada e o app não tiver recebido `--sem-aparar`, rodar `GC.Collect()`, `GC.WaitForPendingFinalizers()`, `GC.Collect()` e `SetProcessWorkingSetSizeEx(processo, -1, -1, 0)` para devolver a memória ao Windows. É o que mais pesa no número da coluna "Memória" do Gerenciador de Tarefas. O caso 1.5 mede com e sem isso. |
| D18 | **Abrir:** `--abrir-sem-foco` mostra com `AppWindow.Show(false)` (não rouba o foco; uso só nos roteiros). As outras formas mostram com `Activate()` e trazem a janela para a frente. |

### 4.5 Posição (`Flyout\FlyoutPlacement.cs`, lógica pura)

| # | Decisão |
|---|---|
| D19 | Entrada: ponto do cursor no clique, área do monitor e área de trabalho (`DisplayArea.GetFromPoint(..., Nearest)`), escala do monitor (`GetDpiForMonitor` / 96) e tamanho em DIP. Saída: retângulo em pixels físicos, passado ao `AppWindow.MoveAndResize` **antes** de mostrar. Use tipos próprios (`PixelPoint`, `PixelRect`), sem tipos do WinUI, para entrar nos testes. Em `--abrir-sem-foco` e `--abrir`, o "cursor" é o canto inferior direito da área de trabalho do monitor principal. |
| D20 | Margem de **12 DIP**. Lado da barra = lado em que a área de trabalho é menor que o monitor. **Embaixo** (ou nenhum lado, barra oculta automaticamente): `x` centrado no cursor e preso entre as margens, `y = base da área − altura − margem`. **Em cima:** `y = topo da área + margem`. **Esquerda/direita:** encostado na barra com a margem, `y` centrado no cursor e preso. Se a janela for mais alta que a área de trabalho menos as margens, a altura é reduzida. |

## 5. Arquivos

**Novos:** `MixerDeAudio.slnx`; `MixerDeAudio\MixerDeAudio.csproj`, `app.manifest`, `App.xaml(.cs)`, `Program.cs`, `Assets\MixerDeAudio.ico`; `Flyout\FlyoutWindow.xaml(.cs)`, `Flyout\FlyoutPlacement.cs`, `Flyout\FlyoutToggleGate.cs`; `Tray\TrayIconService.cs`; `Services\CommandLine.cs`, `Services\AppPaths.cs`, `Services\Log.cs`, `Services\MemoryTrimmer.cs`; `Interop\NativeMethods.cs` (`DwmSetWindowAttribute`, `GetDpiForMonitor`, `SetProcessWorkingSetSizeEx`, com `LibraryImport`); `MixerDeAudio.Tests\` (testes da seção 6.1, lógica por link); `tools\icone\gerar-icone.cs`; `tools\testes\lib.ps1`, `tools\testes\medir.ps1`, `tools\testes\roteiro-f1.ps1`, `tools\testes\README.md`; `.gitignore`, `README.md`, `docs\ESTADO.md`.
**Alterados:** este documento (resultados) e `docs\PLANO.md` (estado da Fase 1).

## 6. Testes

**Regra geral:** compilar sem erros e sem avisos (`dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q`) e rodar a bateria inteira (`dotnet test MixerDeAudio.Tests -v q`) até passar. Os roteiros de tela usam o **build publicado** (`dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publicado`), porque a medição tem que ser do Release com ReadyToRun.

**O usuário pode estar jogando:** nenhum roteiro envia teclas nem cliques reais, nem traz janelas para a frente. Tudo é feito por linha de comando (`--abrir-sem-foco`, `--fechar`, `--sair`), UI Automation e `PrintWindow`. O que exige clique real fica no teste do usuário (6.3).

### 6.1 Testes unitários (novos)

| Arquivo | Casos |
|---|---|
| `FlyoutPlacementTests` | barra embaixo com o cursor no meio (centrado, base na margem); cursor no canto direito (preso à margem direita); escala 150% (tamanho e margem × 1,5); barra em cima; barra à esquerda; barra à direita; barra oculta (como embaixo); janela mais alta que a área (altura reduzida); monitor secundário com coordenadas negativas. |
| `FlyoutToggleGateTests` | abrir sem fechamento recente (permite); 200 ms depois de fechar por perda de foco (ignora); 600 ms depois (permite); logo depois de fechar por Esc (permite); dois fechamentos seguidos (vale o último). |
| `CommandLineTests` | vazio; cada um dos 5 argumentos; caminho do `.exe` com e sem aspas antes do argumento; maiúsculas; desconhecido (nenhum comando); `--sem-aparar` junto com `--abrir-sem-foco`. |

### 6.2 Roteiro na tela (`tools\testes\roteiro-f1.ps1`, Sonnet roda, Opus confere)

Todos os casos dependem da janela real ou do processo, por isso estão aqui e não nos unitários. Primeira fase: não há regressão a conferir.

| Caso | Por que na tela | Passos | Esperado |
|---|---|---|---|
| 1.1 Repouso | O app precisa viver sem janela. | Abrir `publicado\MixerDeAudio.exe` sem argumento; esperar 3 s. | Processo vivo; nenhuma janela visível do processo (`EnumWindows`); log com `iniciado`. |
| 1.2 Abrir | Posição, tamanho e aparência só existem com a janela. | Guardar `GetForegroundWindow`; rodar o `.exe` com `--abrir-sem-foco`; esperar até 2 s. | Continua **1 processo** (a segunda cópia saiu); `FlyoutRoot` e `FlyoutTitle` via UI Automation; largura = 360 × escala (±2 px); retângulo dentro da área de trabalho, encostado embaixo à direita com a margem; janela em primeiro plano **igual** à de antes (não roubou o foco); log `janela aberta`. **Uma** captura `PrintWindow` em `%TEMP%\mixer-check\f1-janela.png` para o Opus conferir os cantos, o Mica e a faixa do topo (D15). |
| 1.3 Fechar | Liberação da janela. | `--fechar`. | Sem janela visível em até 1 s; log `janela fechada (comando)`. |
| 1.4 Medição | Memória e CPU reais do processo. | `tools\testes\medir.ps1`, duas rodadas: normal e com `--sem-aparar`. Em cada uma: 10 s parado → memória; 30 s parado → CPU média; 20 ciclos (`--abrir-sem-foco`, 1 s, `--fechar`, 3 s) → memória após os ciclos 5 e 20. | Tabela com o conjunto de trabalho privado (a coluna "Memória" do Gerenciador, contador `Working Set - Private`), os bytes privados e a CPU. **Aprovação:** CPU parada < 0,1%; crescimento entre o ciclo 5 e o 20 menor que 5 MB (sem vazamento). Os ~50 MB são **registrados, não reprovam**: acima disso, o Opus leva o número ao usuário antes da Fase 2. |
| 1.5 Sair | Encerramento limpo. | `--sair`. | Processo termina em até 5 s; log `saindo`. |

O roteiro deixa tudo como encontrou: encerra o app no fim (também em caso de erro) e só grava em `%TEMP%\mixer-check`.

### 6.3 Teste do usuário (na entrega)

Com o `publicado\MixerDeAudio.exe` aberto:
1. Clicar no ícone da bandeja: a janelinha abre logo acima do ícone, com cantos arredondados e fundo Mica.
2. Clicar fora: fecha. Abrir de novo e apertar Esc: fecha.
3. Com a janela aberta, clicar no ícone: fecha e **não** reabre.
4. A janela não aparece na barra de tarefas nem no Alt+Tab.
5. Botão direito no ícone: "Abrir" e "Sair"; "Sair" encerra e o ícone some da bandeja.
6. Gerenciador de Tarefas com a janela fechada: anotar a memória e conferir a folha verde (modo de eficiência).

## 7. Resultados

Implementado em 18/11/2026 (Sonnet). Nada commitado.

**Versão do H.NotifyIcon.WinUI:** 2.4.1 (última estável no NuGet; as mais novas são prévias).

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q` → 0 avisos, 0 erros. Publish Release x64 com ReadyToRun: ok.
**Testes unitários:** `dotnet test MixerDeAudio.Tests -v q` → 30 aprovados, 0 falhas, 0 ignorados (11 de posição, 5 do portão de alternância, 14 da linha de comando).

**Alternativas e desvios**

| Ponto | O que foi feito |
|---|---|
| D7 `DispatcherShutdownMode` | Existe no SDK 2.5.1 e funcionou: `DispatcherShutdownMode = OnExplicitShutdown` no `OnLaunched`. Sem alternativa. |
| D15 borda e faixa do topo | Com `SetBorderAndTitleBar(true, false)` o Windows deixou uma moldura invisível de ~7 px nos lados e embaixo (retângulo visível 346 × 473 em vez de 360 × 480, e a janela ficava 19 px das bordas em vez de 12). Troquei por `SetBorderAndTitleBar(false, false)`: o retângulo visível passou a ser exatamente 360 × 480 e a margem de 12 px ficou certa. Sem faixa no topo na captura. Cantos arredondados pelo DWM (`DWMWCP_ROUND`) e Mica ficam para o Opus e o teste do usuário: o `PrintWindow` não mostra o Mica (aparece cinza escuro) e os cantos não ficam evidentes na captura. |
| D17 devolução de memória | Além de 2 s depois de fechar a janela, o mesmo timer único também roda 2 s depois do início (se não houver `--sem-aparar`). Sem isso o app parado logo após abrir (ex.: "Iniciar com o Windows") ficaria com o conjunto de trabalho da partida (~90 MB) até a primeira abertura. |
| D12 menu e clique | `TaskbarIcon.LeftClickCommand` (com `NoLeftClickDelay`) e `ContextFlyout` com `MenuFlyout` em `ContextMenuMode.PopupMenu`; os itens usam `Command`. Não há como testar o menu sem clique real: fica para o teste do usuário. |
| D8 ativação redirecionada | A linha de comando da segunda cópia chega pelo `ILaunchActivatedEventArgs.Arguments` e é lida por `CommandLine.ParseLine` (que ignora o caminho do `.exe`, se vier). Funcionou nos roteiros (`--abrir-sem-foco`, `--fechar`, `--sair`). |
| D13 modo de eficiência | `ForceCreate(enablesEfficiencyMode: false)` e `EfficiencyModeUtilities.SetEfficiencyMode(true/false)` pelo app (ligado fechado, desligado aberto). A folha verde não foi conferida (Gerenciador de Tarefas): fica para o teste do usuário. |
| Roteiro | `FlyoutRoot` (um `Grid`) só aparece na UI Automation com `AutomationProperties.Name` e `AccessibilityView="Content"`. `lib.ps1` (e não `lib3.ps1`) tem os auxiliares. `medir.ps1` lê o conjunto de trabalho privado pelo WMI (`Win32_PerfRawData_PerfProc_Process.WorkingSetPrivate`), porque os contadores de desempenho são traduzidos no Windows em português. |
| `.csproj` | `AllowUnsafeBlocks` ligado (necessário para o `LibraryImport`). |

**Caso 1.4: medição** (build publicado Release/ReadyToRun; MB; "WS privado" = conjunto de trabalho privado, a coluna "Memória" do Gerenciador)

| Rodada | Parado 10 s: WS privado / bytes privados | CPU parada (30 s) | Ciclo 5: WS privado / bytes privados | Ciclo 20: WS privado / bytes privados | Crescimento 5 a 20 (WS privado) |
|---|---|---|---|---|---|
| Normal | 2,8 / 75,4 | 0,007% | 1,5 / 103,7 | 0,9 / 106,2 | -0,6 |
| `--sem-aparar` | 23,5 / 75,9 | 0% | 38,1 / 104,5 | 42,1 / 106,0 | +4,0 |

Aprovação: CPU < 0,1% nas duas rodadas (ok); crescimento 5 a 20 menor que 5 MB nas duas (ok: -0,6 e +4,0). Meta de ~50 MB: o WS privado normal fica em ~3 MB (conjunto de trabalho total 10,2 MB); sem aparar, 23 a 42 MB (conjunto total 92,5 MB parado). Observação para o Opus: os **bytes privados** (memória confirmada) ficam em 75 a 106 MB nas duas rodadas e crescem ~30 MB depois do primeiro ciclo de abrir/fechar, e o `SetProcessWorkingSetSizeEx` não os reduz. Se o Gerenciador de Tarefas ou outra ferramenta mostrar "memória confirmada", o número será bem maior que 50 MB.

**Casos de tela (roteiro-f1.ps1, executado 3 vezes ao final, todas passaram)**

| Caso | Resultado |
|---|---|
| 1.1 Repouso | OK: 1 processo, 0 janelas visíveis, log `iniciado`. |
| 1.2 Abrir | OK: 1 processo; `FlyoutRoot` e `FlyoutTitle` encontrados; retângulo (1548,540)-(1908,1020), 360 × 480 na escala 1,0 (área de trabalho 0,0-1920,1032: margem de 12 px à direita e embaixo); foco em primeiro plano igual ao de antes; log `janela aberta`. Captura: `%TEMP%\mixer-check\f1-janela.png` (`C:\Users\lucas\AppData\Local\Temp\mixer-check\f1-janela.png`). |
| 1.3 Fechar | OK: sem janela visível em menos de 1 s; log `janela fechada (comando)`. |
| 1.4 Medição | OK (tabela acima). |
| 1.5 Sair | OK: processo terminou em ~1 s; log `saindo`. |

Nada ficou pendente nos casos automáticos. Ficam para o teste do usuário (6.3): clique real no ícone, fechar ao clicar fora e por Esc, alternância sem reabrir, ausência no Alt+Tab, menu do botão direito, folha verde do modo de eficiência, e a aparência (cantos e Mica).

### Conferência (Opus, 18/11/2026)

- Build sem avisos e 30 testes unitários aprovados (rodados de novo na conferência). Nenhum processo `MixerDeAudio` ficou aberto.
- Código revisado nos pontos de risco: liberação da janela (eventos desinscritos e referência zerada no `Closed`, aviso de fechamento disparado uma vez só), alternância (o fechamento por perda de foco chega antes do clique e o `FlyoutToggleGate` bloqueia a reabertura), saída (o ícone recebe `Dispose` antes do `Exit`) e o redirecionamento da segunda cópia. Sem problemas.
- Desvio D15, `SetBorderAndTitleBar(false, false)`: aceito. Os cantos arredondados e o Mica não aparecem no `PrintWindow`, então ficam para o teste do usuário (6.3, passo 1).
- **Memória:** a meta de ~50 MB do PDF vale para a coluna "Memória" do Gerenciador (conjunto de trabalho privado), que fica em 1 a 3 MB com a devolução de memória. A memória confirmada (bytes privados) fica entre 75 e 106 MB por causa do WinUI carregado. Ela não ocupa RAM enquanto o app está parado, porque as páginas devolvidas saem da memória física. Decisão: aceitar e reavaliar na Fase 6, com trimming e AOT. Efeito colateral a observar no teste do usuário: a primeira abertura depois de um tempo parado pode demorar um pouco mais, porque as páginas voltam do disco.
