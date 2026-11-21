# Fase 8 — Polimento, otimização e instalador

**Status:** concluída (aprovada pelo usuário em 21/11/2026)
**Implementação:** Sonnet, em três partes, nesta ordem: **8a** janela (`medium`), **8b** otimização (`medium`) e **8c** instalador (`low`) · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também:
- `docs/PLANO.md`: metas de leveza e decisões A2, A4 e A10;
- `docs/fases/fase-7.md`, seção 7: modo `--teste-oculto` e `ReabrirAppDoUsuario`;
- o instalador do MeuBloco, que serve de modelo para a 8c: `C:\Projetos\Notas e Kanban\instalador\MeuBloco.iss`.

Repositório: `C:\Projetos\Mixer de Audio`, branch `main`, último commit `9fdf73f`. **Não commitar.**

## 1. Objetivo

Fechar o app para uso diário:
- **8a, janela:**
  - a memória para de crescer a cada abertura;
  - a altura acompanha o conteúdo;
  - a roda do mouse funciona nos sliders;
  - o fundo passa para Acrylic;
  - os apps da Loja ganham ícone de verdade;
  - os botões de saída não quebram linha na página do celular.
- **8b, otimização:**
  - sai o pacote de IA que vem junto com o Windows App SDK;
  - o NAudio é trocado por interfaces próprias;
  - a publicação passa a ser com trimming e Native AOT (decisão A4).
- **8c, instalador:** um `setup.exe` no padrão do MeuBloco, por usuário e sem administrador, na versão 1.0.0.

Fica fora desta fase:
- medidor de nível (VU);
- atualização automática;
- versão ARM64 do instalador;
- capa do álbum;
- acesso pela internet.

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Firewall e celular.** O controle pelo celular escuta na rede. Na primeira vez, o Windows pergunta se libera o acesso, e liberar pede administrador. | **O instalador não cria regra de firewall.** Ele fica por usuário, sem administrador, igual ao MeuBloco. O Windows pergunta uma vez, quando o controle pelo celular é ligado pela primeira vez. A pasta fixa de instalação faz a liberação valer também nas atualizações. Abaixo do interruptor vai a dica "Se o Windows perguntar, permita o acesso em redes privadas". Alternativa: instalar em "Arquivos de Programas", com administrador em toda instalação, criando a regra só para redes privadas. |
| P2 | **Vale trocar o NAudio para ter AOT?** O NAudio usa um COM antigo, que não funciona com trimming nem com AOT. Ele está em 2 arquivos (`DeviceMonitor.cs` e `AudioSessionMonitor.cs`, cerca de 630 linhas). O resto do código já é compatível: `x:Bind`, JSON com gerador e os COMs próprios com `[GeneratedComInterface]`. | **Sim.** O ganho esperado é uma abertura a frio mais rápida, sem JIT: a primeira abertura leva hoje de 480 a 590 ms. Também diminuem a memória confirmada do app parado (hoje cerca de 75 MB) e a pasta publicada. O risco é quebrar o áudio, e os casos 8.8 a 8.10 testam exatamente essas funções. Se o AOT esbarrar numa biblioteca, há uma saída em degraus (D11). |
| P3 | **Mica ou Acrylic** (pendente desde a Fase 1). | **Acrylic** (`DesktopAcrylicBackdrop`). No Windows 11, Acrylic é o material das superfícies passageiras, como menus e o painel de volume da bandeja, e deixa ver o que está atrás. A Mica é para janelas que ficam abertas. A troca é de uma linha; você confere no teste. |
| P4 | **Reaproveitar a janela.** Hoje a janela é criada a cada clique e destruída ao fechar (PLANO, "metas de leveza"). Cada abertura deixa cerca de 0,15 MB e 3 handles para trás, que ficam no WinUI: em 150 aberturas, +21 MB. | **Criar a janela no primeiro clique e só escondê-la ao fechar.** Com a janela escondida, o áudio continua solto: o núcleo é liberado e as listas esvaziadas, como hoje, e a memória ainda é devolvida ao Windows. O PLANO passa a dizer "janela escondida e reaproveitada". |

## 3. O que foi levantado nesta análise (21/11/2026)

- **`publicado\`:** 284 MB em 442 arquivos.
  - Só do pacote de IA do Windows App SDK 2.x (`onnxruntime.dll`, `DirectML.dll`, `Microsoft.Asg.SemanticIndex…`, `Microsoft.Windows.Search.dll` e outros) são cerca de 50 MB, que o app não usa.
  - `Microsoft.Windows.SDK.NET.dll` (56 MB) é a projeção do Windows inteira, que o trimming corta.
- **O pacote `Microsoft.WindowsAppSDK` 2.5.1 é um "metapacote".** Ele junta `WinUI` 2.3.9, `Foundation` 2.3.12, `InteractiveExperiences`, `DWrite`, `Base`, `AI` 2.5.5, `ML`, `Search` e `Widgets`. Todos já estão no cache do NuGet desta máquina. O app só usa WinUI, AppInstance e Foundation.
- **Compatibilidade com AOT do código atual:**
  - 61 `x:Bind` e nenhum `{Binding}`;
  - `AppSettings` com `JsonSerializerContext`;
  - o protocolo do celular com `Utf8JsonWriter` e `JsonDocument`;
  - `PolicyConfig`, `AppRoutingService` e `AumidResolver` já com `[GeneratedComInterface]`.

  Pontos incompatíveis ou duvidosos:
  - NAudio;
  - `System.Drawing` em `AppIconCache.cs` (ícones) e `TrayIconService.cs` (ícone da bandeja);
  - H.NotifyIcon e QRCoder, que não foram verificados.
- **Ícone genérico do Spotify:** `AppIconCache` usa `Icon.ExtractAssociatedIcon` no `.exe`. Na pasta `WindowsApps`, a leitura é negada, e o resultado é o ícone genérico.
- **Inno Setup 6** está em `%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`; é o mesmo que o MeuBloco usa.
- **"Iniciar com o Windows":** `StartupRegistration.IsEnabled` só considera ligado quando o valor `Run` aponta para o `.exe` em execução. Sem ação nenhuma, o app instalado mostraria "desligado" enquanto o valor ainda aponta para `publicado\`.
- **`lib.ps1`:** `ReabrirAppDoUsuario` só reabre se o app do usuário era o de `publicado\`. Depois de instalado, o app do dia a dia fica em outra pasta.

## 4. Decisões

### 4.1 Parte 8a — janela

| # | Decisão |
|---|---|
| D1 | **Janela reaproveitada (P4).**<br>- `App` cria a `FlyoutWindow` no primeiro pedido. `RequestClose` passa a esconder (`AppWindow.Hide()`) em vez de fechar.<br>- Ao esconder: soltar o núcleo (`_hub.Release`), chamar `Detach()` nas view models (desliga os eventos do núcleo e esvazia as coleções), voltar da página de configurações para a principal (soltando a `SettingsView`, como hoje) e levar a rolagem ao topo.<br>- Ao mostrar: `_hub.Acquire`, `Attach()` nas view models (lê e liga os eventos), reaplicar o tema e posicionar.<br>- Só `Sair` fecha de verdade.<br>- Log: `janela: criada` na primeira vez e `janela: reaproveitada` nas outras. A linha `sessoes carregadas (… ms)` continua a cada abertura, porque os roteiros e o `medir.ps1` a leem.<br>- O `FlyoutToggleGate` e o fechar ao perder o foco não mudam. |
| D2 | **Altura adaptável.**<br>- Uma função pura nova, `FlyoutSize.Height(desejada, alturaUtil, escala)`, limita a altura entre 260 DIP e o menor de 560 DIP e 70% da área útil do monitor.<br>- A altura desejada vem do `Measure` do conteúdo com largura de 420 DIP.<br>- É calculada ao abrir e quando o número de linhas, ou a 3ª linha de mídia, muda com a janela aberta. A mudança é juntada em 150 ms e mantém a borda de baixo, ou seja, a janela cresce para cima.<br>- Passando do máximo, a lista rola como hoje. A página de configurações usa a altura máxima.<br>- `FlyoutPlacement` recebe a altura em vez da constante `HeightDip`, e a linha `janela: posicao calculada x,y,l,a` continua. |
| D3 | **Roda do mouse nos sliders.**<br>- `PointerWheelChanged` no volume geral e no de cada app: cada "dente" (delta 120) vale ±2, limitado a 0..100, com `Handled = true` para a lista não rolar junto.<br>- A conta fica numa função pura, `VolumeStep.Apply(valor, delta)`.<br>- Usa o mesmo caminho do arrastar, com o mesmo filtro de eco da Fase 2. |
| D4 | **Acrylic (P3):** `SystemBackdrop = new DesktopAcrylicBackdrop()`. O tema continua seguindo o conteúdo (Fase 5, D10). |
| D5 | **Ícones pelo shell, sem `System.Drawing`.**<br>- `AppIconCache` extrai com `IShellItemImageFactory.GetImage` (32 px; `SIIGBF_ICONONLY \| SIIGBF_BIGGERSIZEOK`), escrito com `[GeneratedComInterface]` e um `StrategyBasedComWrappers` estático.<br>- O item vem do caminho do `.exe` (`SHCreateItemFromParsingName`) ou, para app da Loja, do AUMID na pasta de apps (`SHCreateItemInKnownFolder`, como no `AumidResolver`).<br>- O HBITMAP vira BGRA (`GetDIBits`) e depois PNG pelo `Windows.Graphics.Imaging.BitmapEncoder`. Ao final, `DeleteObject`.<br>- **Escolha da fonte:**<br>&nbsp;&nbsp;- linha de som cujo processo tem AUMID da Loja (`AumidResolver.ProcessAumid`, já existente) usa o AUMID;<br>&nbsp;&nbsp;- linha só de mídia com AUMID da Loja (com `!`) também usa o AUMID;<br>&nbsp;&nbsp;- o resto usa o `.exe`.<br>- A chave do cache é `aumid:<id>` ou o caminho. A página do celular usa o mesmo cache e ganha o ícone certo.<br>- Falha deixa o PNG vazio, como hoje. |
| D6 | **Página do celular:**<br>- `.routes` com `flex-wrap: nowrap`;<br>- botões com `flex: 1 1 0; min-width: 0`;<br>- nome com reticências.<br>Com até 3 saídas, fica uma linha só em 375 px. |

### 4.2 Parte 8b — otimização

| # | Decisão |
|---|---|
| D7 | **Pacotes do Windows App SDK:** trocar `Microsoft.WindowsAppSDK` 2.5.1 por `Microsoft.WindowsAppSDK.WinUI` 2.3.9 e `Microsoft.WindowsAppSDK.Foundation` 2.3.12, as mesmas versões que o metapacote usa. As dependências deles (Base, InteractiveExperiences, WebView2) vêm sozinhas.<br>**Risco:** o modo autocontido com pacotes separados. Se o app não abrir, voltar ao metapacote, anotar e seguir. |
| D8 | **NAudio fora (P2).**<br>- Novas interfaces em `Audio\CoreAudio\` com `[GeneratedComInterface]`: `IMMDeviceEnumerator`, `IMMDevice`, `IMMDeviceCollection`, `IPropertyStore` (com `PROPVARIANT` próprio e `PropVariantClear`), `IAudioEndpointVolume`, `IAudioSessionManager2`, `IAudioSessionEnumerator`, `IAudioSessionControl2`, `ISimpleAudioVolume`.<br>- Callbacks com `[GeneratedComClass]`: `IMMNotificationClient`, `IAudioEndpointVolumeCallback`, `IAudioSessionNotification`, `IAudioSessionEvents`. São só os métodos que o código usa hoje, na ordem exata da tabela de métodos do SDK.<br>- Um `StrategyBasedComWrappers` estático para tudo (lição da Fase 4).<br>- `DeviceMonitor` e `AudioSessionMonitor` mantêm a interface pública, as threads e a ordem de soltura de hoje. Leia o código atual antes de trocar; a fonte do NAudio 3.3.0 serve de referência.<br>- A soltura determinística passa de `Dispose` para `ComObject.FinalRelease()`.<br>- **Risco:** o `RegisterSessionNotification` precisa que o enumerador de sessões tenha sido pedido antes, e a thread onde é chamado importa. Mantenha exatamente o que o NAudio faz hoje.<br>- Remova `NAudio.Wasapi` do `.csproj`. |
| D9 | **AOT e trimming:**<br>- `PublishAot=true` no lugar de `PublishReadyToRun` (só na publicação; o build Debug continua com JIT);<br>- classes usadas pelo XAML e pelo CsWinRT marcadas como `partial` (aviso CsWinRT1028);<br>- `DllImport` com `string` ou `bool` passa para `LibraryImport` quando o AOT avisar. |
| D10 | **Zero aviso de trimming ou AOT do nosso código** (IL2xxx e IL3xxx), como já vale para o build.<br>Aviso vindo de H.NotifyIcon, QRCoder ou `System.Drawing.Common` (que fica só no ícone da bandeja): anote, e o recurso afetado tem que funcionar no roteiro (QR code, caso 8.11) ou no teste do usuário (bandeja).<br>Suprimir um aviso só com `<NoWarn>` por assembly e com a justificativa no documento. |
| D11 | **Saída em degraus** se o AOT não funcionar em tempo razoável: (1) `PublishTrimmed` + ReadyToRun; (2) só ReadyToRun, ainda sem NAudio e sem o pacote de IA. Registre qual degrau ficou e por quê. |
| D12 | **Medição depois da 8b:** `medir.ps1 -ComSom -Ciclos 20` uma vez, mais o tamanho de `publicado\`. Os números entram no documento e são comparados com as tabelas do `ESTADO.md`, sem rodar o build antigo (sem A/B). |

### 4.3 Parte 8c — instalador

| # | Decisão |
|---|---|
| D13 | **`instalador\MixerDeAudio.iss`**, copiando o modelo do MeuBloco: por usuário (`PrivilegesRequired=lowest`), pasta fixa `{autopf}\MixerDeAudio`, sem página de pasta, pt-BR, `lzma2/ultra64`, só x64.<br>- `AppId` novo e fixo (gerar um GUID e nunca mudar).<br>- Saída `dist\MixerDeAudio-<versão>-x64-setup.exe`.<br>- `[InstallDelete]` e `[UninstallDelete]` só de `{app}`.<br>- Recusa versão mais antiga por cima de uma mais nova. |
| D14 | **`tools\release\gerar.ps1`:**<br>- publica (o mesmo comando do `ESTADO.md`) numa pasta temporária `dist\app`, não em `publicado\`, para não mexer no app dos testes;<br>- lê a versão do `<Version>` do `.csproj` e chama o `ISCC.exe`;<br>- `dist/` entra no `.gitignore`. |
| D15 | **Versão 1.0.0** no `.csproj`. As Configurações ganham no rodapé "Versão 1.0.0", lida do assembly. |
| D16 | **App aberto:** em `InitializeSetup` e no começo da desinstalação, rodar `{app}\MixerDeAudio.exe --sair`, se existir, e esperar até 5 s.<br>Por causa da instância única, isso fecha **qualquer** cópia aberta, inclusive a de `publicado\`. É o comportamento certo numa atualização.<br>`CloseApplications=force` fica como reserva. |
| D17 | **Tarefas:**<br>- "Criar atalho na Área de Trabalho", desmarcada;<br>- **"Iniciar com o Windows", marcada**: grava o valor `MixerDeAudio` em `HKCU\…\Run` com `"{app}\MixerDeAudio.exe"` e apaga o de `StartupApproved\Run`, como faz o `SetEnabled`. Isso também corrige um valor antigo que aponta para `publicado\`.<br>Sem a tarefa, o registro não é tocado. |
| D18 | **Desinstalar**, como no MeuBloco:<br>- remove `Run` e `StartupApproved` só se o caminho estiver dentro de `{app}`;<br>- pergunta se apaga `%LOCALAPPDATA%\MixerDeAudio` (padrão "Não"); no modo silencioso, só apaga com `/APAGARDADOS`. |
| D19 | **Firewall (P1):** nenhuma regra. A dica fica em `SettingsView`, abaixo do interruptor do celular. |
| D20 | **`lib.ps1`:** guardar o caminho do `MixerDeAudio.exe` que estava rodando, seja qual for a pasta, e reabrir esse caminho em `ReabrirAppDoUsuario`. |
| D21 | **Roteiros antigos:** só uma nota no `tools\testes\README.md`. Os casos 1.2, 5.1, 5.4 e 5.5 não valem no modo `--teste-oculto`, e os roteiros f1 a f7 não são mais rodados em cadeia. Eles não serão adaptados. |

## 5. Arquivos

**8a**
- Novos: `Flyout\FlyoutSize.cs`, `ViewModels\VolumeStep.cs` (ambos puros, linkados no projeto de testes), `MixerDeAudio.Tests\FlyoutSizeTests.cs`, `VolumeStepTests.cs`, `tools\testes\roteiro-f8.ps1` (`-Parte a|b|c`).
- Alterados:
  - `App.xaml.cs`
  - `Flyout\FlyoutWindow.xaml(.cs)`
  - `Flyout\FlyoutPlacement.cs` (e os testes dele)
  - `ViewModels\SessionListViewModel.cs`, `DeviceListViewModel.cs`, `MasterVolumeViewModel.cs` (`Attach` e `Detach`)
  - `Audio\AppIconCache.cs`
  - `Interop\NativeMethods.cs`
  - `Remote\RemoteServer.cs` (ícone pela chave nova)
  - `Remote\Web\index.html`

**8b**
- Novos: `Audio\CoreAudio\*.cs`.
- Alterados: `Audio\DeviceMonitor.cs`, `Audio\AudioSessionMonitor.cs`, `MixerDeAudio.csproj`, as classes que precisarem de `partial`.

**8c**
- Novos: `instalador\MixerDeAudio.iss`, `tools\release\gerar.ps1`.
- Alterados:
  - `MixerDeAudio.csproj` (versão)
  - `Flyout\SettingsView.xaml(.cs)` (versão e dica do firewall)
  - `.gitignore`
  - `tools\testes\lib.ps1` (D20)
  - `tools\testes\README.md` (D21)
  - `docs\ESTADO.md` (comando do instalador)

## 6. Testes

### 6.1 Testes unitários (novos)

- **8a:**
  - `FlyoutSize`: limite mínimo e máximo, 70% da área útil, escala 100%, 150% e 200%;
  - `FlyoutPlacement` com altura variável: a borda de baixo fica fixa;
  - `VolumeStep`: ±2 por dente, vários dentes, limites 0 e 100, delta parcial.
- **8b:** conversões puras que surgirem (`PROPVARIANT` de texto, estado da sessão). A bateria inteira tem que continuar passando sem o NAudio.

### 6.2 Regras do roteiro

- **Modo oculto:** `--teste-oculto` sempre, sem janela nem ícone visível, sem teclas, sem cliques reais e sem trazer janela para a frente. O app do usuário é reaberto ao fim (D20).
- **Só esta fase:** o roteiro cobre só o que a fase fez, e os roteiros anteriores não são rodados de novo. A bateria inteira de testes unitários roda a cada parte.
- **Áudio do usuário:**
  - só os tocadores `MX-teste` mudam;
  - nunca trocar a saída padrão nem mexer no mudo geral;
  - o volume geral só ±1%, devolvido num `finally`;
  - nunca apertar mídia de app real.
- **Configuração e registro:** `BackupConfig` e `RestoreConfig` em toda parte que mexe na configuração ou no registro. **Nunca matar um roteiro no meio**: é assim que o `config.json` do usuário já foi perdido.
- **Celular:** o servidor só em `127.0.0.1` (`--celular-local`).
- **Publicar:** antes, rodar `publicado\MixerDeAudio.exe --sair`.
- **Firewall:** o agente **não** instala com administrador nem cria regra de firewall.

### 6.3 Roteiro da 8a (`roteiro-f8.ps1 -Parte a`)

O que não depende da janela fica nos testes unitários. A roda do mouse e o Acrylic precisam de entrada real ou de olho e ficam no teste do usuário.

| Caso | Por que na tela | Passos | Resultado esperado |
|---|---|---|---|
| 8.1 Reaproveitada | O ciclo da janela só existe com a janela. | Abrir e fechar 3 vezes sem foco; antes da 3ª, com a janela fechada, ligar `tocar --nome "MX-teste A"`. | O log tem `janela: criada` uma vez e `janela: reaproveitada` duas. Na 3ª abertura, a linha MX-teste A aparece e é a única linha de teste: nada sobra da abertura anterior. |
| 8.2 Altura | Depende do `Measure` real. | Abrir sem tocadores de teste e ler `a` na linha de posição; ligar `tocar-b` com a janela aberta. | A altura com mais linhas é maior, até o máximo. Ao crescer com a janela aberta, `y + a` fica igual, com 1 px de folga. |
| 8.3 Ícone da Loja | Ícone real servido pelo app. | Só se o Spotify estiver no controle de mídia: ligar o celular local, `celular estado`, baixar o ícone da linha do Spotify. Só leitura, sem tocar no Spotify. | PNG não vazio e diferente do ícone genérico (comparar com o de um `.exe` sem ícone). Sem o Spotify, o caso é pulado e anotado. |
| 8.4 Sem regressão | A view model religada pode perder os eventos. | Depois da reabertura de 8.1, mudar o volume (40) e o mudo da linha MX-teste A por UIA. | O volume é gravado na sessão; o mudo liga e desliga. |
| 8.5 Medição | A fase mexe na abertura. | `medir.ps1 -ComSom -Ciclos 50`, uma vez. | Handles do ciclo 20 ao 50: menos de +15 (Fase 5: +61). Bytes privados do 20 ao 50: menos de 2 MB. Mediana de abertura até 200 ms. CPU parada abaixo de 0,1%. |

### 6.4 Roteiro da 8b (`roteiro-f8.ps1 -Parte b`), com o build publicado em AOT

| Caso | Por que na tela | Passos | Resultado esperado |
|---|---|---|---|
| 8.6 Sessões | Troca do COM das sessões. | `tocar` MX-teste A e `tocar-b`; abrir sem foco. Mudar por UIA o volume (30) e o mudo de A; `sessoes definir` A para 70; encerrar `tocar-b`. | As 2 linhas aparecem com nome. O volume e o mudo chegam à sessão (`sessoes listar`). Os 70 aparecem na linha em até 1 s. A linha de `tocar-b` some em até 2 s. |
| 8.7 Geral e saídas | Troca do COM dos dispositivos. | Ler a lista de saídas e a padrão; volume geral +1% por UIA e depois devolver. | O número e os nomes das saídas são os de `sessoes dispositivos`, e a padrão está destacada. O +1% aparece em `sessoes geral`, e o valor original volta no `finally`. |
| 8.8 Rota | O pid da sessão vem do COM novo. | `ClickRoute` na linha MX-teste A para outra saída e depois para a padrão. | `rota-ler` mostra a escolha e depois a apaga. Apps reais com a mesma escolha antes e depois. |
| 8.9 Celular e QR | QRCoder e servidor sob AOT. | Abrir as configurações sem foco com o celular local ligado; `celular estado`. | A imagem do QR existe (`RemoteQr` com tamanho maior que 0). O estado traz as linhas de teste. |
| 8.10 Medição | O AOT muda o app parado e a abertura. | `medir.ps1 -ComSom -Ciclos 20`, uma vez, e o tamanho de `publicado\`. | CPU parada abaixo de 0,1%. WS privado parado de até 5 MB. Primeira abertura abaixo de 300 ms. Bytes privados parados e tamanho anotados (sem meta; comparar com o `ESTADO.md`). |

### 6.5 Roteiro da 8c (`roteiro-f8.ps1 -Parte c`)

| Caso | Passos | Resultado esperado |
|---|---|---|
| 8.11 Gerar | `tools\release\gerar.ps1`. | `dist\MixerDeAudio-1.0.0-x64-setup.exe` existe, sem erro do ISCC. |
| 8.12 Instalar e desinstalar | Com `BackupConfig`: instalar silencioso em pasta de teste (`/VERYSILENT /CURRENTUSER /DIR="%TEMP%\mixer-check\instalado" /TASKS=""`, sem a tarefa de iniciar com o Windows). Conferir e desinstalar silencioso. | Os arquivos e o `.exe` com versão 1.0.0 estão na pasta. A chave de desinstalação existe em HKCU. Depois da desinstalação, a pasta, a chave e o atalho do Iniciar somem. O valor `Run`, o `config.json` (hash) e o app do usuário (reaberto) ficam como antes. |

### 6.6 Teste do usuário (na entrega)

1. Instalar pelo `setup.exe`, com "Iniciar com o Windows" marcado. O mixer instalado abre, e as Configurações mostram "Versão 1.0.0" e "Iniciar com o Windows" ligado.
2. Abrir e fechar a janelinha várias vezes:
   - o fundo é Acrylic, translúcido;
   - a altura acompanha a quantidade de apps;
   - a roda do mouse sobre um slider muda o volume sem rolar a lista.
3. O Spotify (pausado ou tocando) mostra o ícone dele. Ícone, menu e clique na bandeja continuam funcionando com o app em AOT.
4. Ligar o controle pelo celular. Se o Windows perguntar, permitir em redes privadas. Parear e conferir que os botões de saída ficam numa linha só.
5. Opcional: desinstalar pelo Windows e conferir a pergunta sobre os dados.

## 7. Resultados

### Parte 8a

- **Build:** Debug e Release (x64) sem erros e sem avisos. **Testes unitários:** 358 passaram, 0 falhas (novos: `FlyoutSizeTests`, `VolumeStepTests` e 2 casos de altura variável em `FlyoutPlacementTests`). Publicado em `publicado\` (ReadyToRun, como antes).
- **Roteiro:** `tools	estesoteiro-f8.ps1 -Parte a` (b e c ainda não existem; `-SemMedicao` pula o 8.5). Tempo: 367 s com a medição; 14 s sem. `config.json` e registro voltaram iguais (`diferencas=[]`); app do usuário reaberto.
- **8.1 Reaproveitada: OK.** Log com `janela: criada` 1x e `janela: reaproveitada` 2x; `sessoes carregadas` 3x; 0 linhas de teste na 1ª abertura; na 3ª só `MX-teste A`.
- **8.2 Altura: OK, mas só o limite superior foi exercitado.** Os apps reais do usuário (Discord, Opera GX, Steam, wallpaper64 e Sons do sistema) já pedem 489 a 631 DIP, então a janela abre no máximo (560 px a 100%, limite 70% de 1032 = 722): ligar `tocar-b` não a faz crescer, a linha aparece e `y + a` fica igual (1020 antes e depois, diferença 0 px). O crescimento abaixo do máximo não pôde ser visto na tela (não se mexe nos apps reais); fica coberto pelos testes de `FlyoutSize` e `FlyoutPlacement`. Para ajudar na conferência, o log ganhou `janela: altura desejada X DIP, final Y DIP`.
- **8.3 Ícone da Loja: PULADO** (o Spotify não está no controle de mídia nem tem sessão de som). Não houve app da Loja para exercitar o caminho `aumid:`; ele só tem build e leitura de código, ficando para o teste do usuário. **8.3a (extra, mesmo roteiro): OK**: o caminho novo do shell (sem `System.Drawing`) serve PNG de 32x32 pela página do celular para `MX-teste A` (676 bytes) e Discord (1347 bytes).
- **8.4 Sem regressão: OK.** Depois da reabertura, volume 40 gravado na sessão (0,40; texto da linha "40") e mudo liga e desliga.
- **8.5 Medição: marginal, 1 critério no limite.** `medir.ps1 -ComSom -Ciclos 50` (uma vez): handles do ciclo 20 ao 50 1409 → 1413 (**+4**, limite +15; Fase 5: +61); bytes privados 130,7 → 132,7 MB (**+2,0**, limite "menos de 2": no limite, dentro do ruído visto na Fase 5, que deu +2,8 e +5,4); mediana de abertura **49 ms** em 50 aberturas (máx. 225 ms; limite da mediana 200); CPU parada **0,003%**; parado: WS privado 2,0 MB, bytes privados 77,6 MB; ciclo 5: 131,4 MB. A medição não foi repetida.

**Desvios**
- A janela escondida mantém o `FlyoutWindow` e as view models; `DeviceListViewModel` passou a receber `Func<DeviceTracker>` (a instância do hub muda a cada ativação) e ganhou `Attach`/`Detach`, como a `SessionListViewModel`.
- `AppWindow.Closing` é cancelado (Alt+F4 só esconde); `App.ExitApp` fecha a janela de verdade (`CloseForReal`).
- Em `FlyoutPlacement` nada mudou no código: ele já recebia a altura por parâmetro; só ganhou testes. A altura não vem mais da constante `HeightDip` (removida).
- O ícone é extraído numa thread STA própria (a pasta de apps pede STA) e o `StrategyBasedComWrappers` é o mesmo do `AumidResolver`.
- A altura só cresce para cima mantendo a borda de baixo, também com a barra de tarefas em outro lado (como no D2).

### Parte 8b

- **Build e testes:** Debug e Release (x64) sem erros e sem avisos. Testes unitários: 370 passaram, 0 falhas (12 novos em `CoreAudioValuesTests`: tamanho do `PROPVARIANT` e do `PROPERTYKEY`, texto e inteiro do `PROPVARIANT`, estado da sessão, leitura do `AUDIO_VOLUME_NOTIFICATION_DATA`).
- **D7 (pacotes separados): OK.** `Microsoft.WindowsAppSDK.WinUI` 2.3.9 e `Foundation` 2.3.12, mais `InteractiveExperiences` 2.1.9 (a 2.1.8 que o Foundation pede não existe no NuGet; sem a referência direta saía o aviso NU1603). O pacote `H.NotifyIcon.WinUI` puxa o metapacote `Microsoft.WindowsAppSDK` 1.6, que quebrava o build (props do WinUI importados duas vezes e erro do MSIX tooling). Solução: `Microsoft.WindowsAppSDK` 1.6.250108002 com `ExcludeAssets="all"`, só para o NuGet não trazer o metapacote 2.x (e o pacote de IA) de volta. O app abre autocontido (`sessoes carregadas` no log).
- **D8 (NAudio fora): OK.** Novos em `Audio\CoreAudio\`: `CoreAudioInterfaces.cs` (9 interfaces e 4 callbacks com `[GeneratedComInterface]`), `CoreAudioObjects.cs` (camadas finas `CoreEnumerator`, `CoreDevice`, `CoreEndpointVolume`, `CoreSessionManager`, `CoreSession` e os receptores `[GeneratedComClass]`) e `CoreAudioValues.cs` (`PropVariant`, `PropertyKey`, estado e notificação de volume, puros e testados). `DeviceMonitor` e `AudioSessionMonitor` mantêm interface pública, threads (avisos só enfileiram) e ordem de soltura; a soltura passou a `ComObject.FinalRelease()`. O `StrategyBasedComWrappers` é o estático do `AumidResolver`. `NAudio.Wasapi` saiu do `.csproj` (as ferramentas de teste em `tools\testes` ainda usam NAudio, fora do app).
- **Degrau final: D11, degrau 1 (`PublishTrimmed` + ReadyToRun), sem AOT.** O `PublishAot` não linka nesta máquina: "Platform linker not found". O Visual Studio 18 tem o `link.exe`, mas não há Windows SDK com as `.lib` (`kernel32.lib` só existe dentro de um pacote antigo de UWP). Instalar o SDK C++ seria baixar ferramenta fora do NuGet e mexer no sistema, então não foi feito. Com o SDK instalado o AOT pode ser tentado de novo (trocar `PublishTrimmed`/`PublishReadyToRun` por `PublishAot`; o código já está sem NAudio, com COM gerado, JSON com gerador e `LibraryImport`).
- **Tamanho de `publicado\`:** 89,2 MB em 302 arquivos (antes: 284 MB em 442). O pacote de IA, o `onnxruntime` e a maior parte da projeção do Windows saíram.
- **Avisos de trimming (D10):** zero IL2xxx e IL3xxx do nosso código. De terceiros, só dois avisos de resumo IL2104: `Microsoft.Windows.SDK.NET.dll` e `WinRT.Runtime.dll` (CsWinRT), sem `NoWarn`. H.NotifyIcon, QRCoder e System.Drawing.Common não geraram aviso. O QR code funcionou (8.9). A bandeja (ícone, menu, clique) não aparece no modo oculto: fica para o teste do usuário.
- **8.6 Sessões: OK.** 2 linhas com nome; volume 30 e mudo (liga e desliga) chegam à sessão; `sessoes definir` 70 apareceu na linha em 0,11 s (limite 1 s); a linha de `tocar-b` sumiu em 0,09 s (limite 2 s).
- **8.7 Geral e saídas: OK.** 3 saídas na tela, com os nomes e a ordem de `sessoes dispositivos`; a padrão (Soundbar) está destacada; +1% apareceu em `sessoes geral` (0,67) e o volume voltou a 0,66 no `finally`.
- **8.8 Rota: OK.** `rota-ler` mostra a outra saída e depois `padrao` (destaque acompanha); a escolha dos 5 apps reais ficou igual.
- **8.9 Celular e QR: OK.** `RemoteQr` existe com 160x160; pareamento HTTP 200; o estado traz `MX-teste A` (7 apps no estado). Zero linhas `ERRO` no log nos casos 8.6 a 8.9.
- **8.10 Medição: OK** (`medir.ps1 -ComSom -Ciclos 20`, uma vez, sem A/B). CPU parada 0,007% (limite 0,1). WS privado parado 1,9 MB (limite 5). Primeira abertura 189 ms (limite 300); mediana 49 ms em 20 aberturas. Comparado ao `ESTADO.md` (rodadas com 2 tocadores): bytes privados parados 67,1 MB (antes 74 a 77); ciclo 20: bytes privados 120,8 MB (antes 125 a 138), WS privado 2,5 MB, handles 1359 (antes 1318 a 1379).
- **Tempo do roteiro:** 185 s com a medição; 15 s sem. `config.json` e registro voltaram iguais (`diferencas=[]`).
- **Desvios:** (1) sem AOT (falta o SDK do Windows para o linker; degrau 1 da D11). (2) `InteractiveExperiences` 2.1.9 e o `Microsoft.WindowsAppSDK` 1.6 com `ExcludeAssets="all"` no `.csproj` (explicados na D7). (3) Nas classes que o XAML/CsWinRT usam não foi preciso `partial` extra: não houve CsWinRT1028. (4) O roteiro 8b grava `{"Versao": 1}` no `config.json` (com `BackupConfig`/`RestoreConfig`) para o celular ficar desligado nos casos 8.6 a 8.8 e na medição, e o servidor só em `127.0.0.1`.
- **Fora do roteiro:** o app do usuário estava aberto (`publicado\`) e foi fechado com `--sair` para publicar; reaberto ao fim.

### Parte 8c

- **Build e testes:** Debug e Release (x64) sem erros e sem avisos. Testes unitários: 373 passaram, 0 falhas (3 novos em `VersionTextTests`).
- **Código:** `1.0.0` no `.csproj`; `VersionText.Format` (puro, testado) e `SettingsViewModel.VersionLabel`; rodapé "Versão 1.0.0" e a dica do firewall (`RemoteFirewallHint`) abaixo do interruptor do celular em `SettingsView.xaml` (não conferidos na tela: sem caso no roteiro); `dist/` no `.gitignore`; `instalador\MixerDeAudio.iss` (AppId novo `9D3F7A52-6B1E-4C80-A7D4-2E5B8C1F0A63`, x64, Windows 11) e `tools\release\gerar.ps1`; `lib.ps1` guarda e reabre o caminho real do app (D20); nota no README dos testes (D21); `ESTADO.md` (linha 3 e comando do instalador). Publicado em `publicado\` (1.0.0.0).
- **Tamanho do setup:** 23,8 MB (`dist\MixerDeAudio-1.0.0-x64-setup.exe`).
- **8.11 Gerar: OK** (29 s, sem erro do ISCC).
- **8.12 Instalar e desinstalar: OK.** Instalação silenciosa em `%TEMP%\mixer-check\instalado` (código 0, 304 arquivos, exe 1.0.0, `unins000`, chave HKCU e atalho do Iniciar criados, `Run` intocado). Desinstalação silenciosa (código 0): pasta, chave e atalho sumiram; `Run`, `StartupApproved` e hash do `config.json` iguais; `diferencas=[]`. Tempo do roteiro: 37 s.
- **Desvios:** (1) `{app}` não existe em `InitializeSetup`; o `--sair` usa a pasta da instalação anterior (`Inno Setup: App Path`) ou `{autopf}\MixerDeAudio`. (2) A espera após o `--sair` é fixa de 1,5 s (o Inno não tem espera por processo simples). (3) O `--sair` do exe instalado não chegou a ser exercitado (a pasta de teste não tinha exe na hora do `InitializeSetup`; na desinstalação o app do usuário continuou aberto, então não foi preciso reabri-lo): fica para o teste do usuário. (4) A tarefa "Iniciar com o Windows" não foi exercitada (decisão do caso 8.12).

**Conferência (Opus, 21/11/2026)**
- **8a:** o `AppIconCache` passou a soltar a fábrica de ícones com `FinalRelease` na própria thread STA, sem esperar o finalizador. Bytes privados do ciclo 20 ao 50 no limite (+2,0 MB), aceito: os handles caíram de +61 para +4.
- **8b:** as tabelas de métodos de `CoreAudioInterfaces.cs` (`IAudioEndpointVolume`, `IAudioSessionControl2`, `IAudioSessionManager2`, `IMMDeviceEnumerator`) e o deslocamento da notificação de volume (16 e 20) batem com o SDK. Sem AOT por falta do Windows SDK de C++ (degrau 1 da D11).
- **8c:** a instância única do Windows App SDK vale **por caminho do .exe**. Um `--sair` de outra pasta abriu uma cópia nova em vez de fechar a aberta, como foi conferido com `dist\app`. Corrigido em dois pontos:
  - **instalador:** `InitializeSetup` fecha com `--sair` toda cópia aberta, de qualquer pasta, achando os caminhos pelo WMI (`CloseAllRunning`);
  - **`lib.ps1`:** `StopAll` manda `--sair` pelo caminho de cada cópia e só força o fechamento das cópias de `publicado\`, nunca da instalada.
- **Reexecução da `-Parte c`:** 0 falhas em 39 s. O app do usuário (`publicado\`) foi fechado pelo instalador e reaberto.
- **Observação:** se o usuário abrir à mão a cópia de `publicado\` com a instalada rodando, ficam dois ícones na bandeja.
