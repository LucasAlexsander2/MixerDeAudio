# Fase 9 — Controle do PC pelo celular

**Status:** concluída (teste do usuário aprovado em 22/11/2026)
**Implementação:** Sonnet (`medium`), em quatro partes, **nesta ordem** (todas mexem em `RemoteSession`, `RemoteProtocol`, `index.html` e `SettingsView`, então não rodam em paralelo): **9a** base, energia e monitores · **9b** mouse, teclado, modo apresentação e ver a tela · **9c** janelas, atalhos e timer de sono · **9d** área de transferência, arquivo, estado do PC e medição · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também:
- `docs/PLANO.md`, decisões A10 e A12;
- `docs/fases/fase-6.md`, seção 4: servidor, protocolo, pareamento, threads (D5 a D13);
- `docs/fases/fase-8.md`, seção 6.2: regras do roteiro (modo oculto, `BackupConfig`/`RestoreConfig`, `--celular-local`).

Repositório: `C:\Projetos\Mixer de Audio`, branch `main`, último commit `2902e66`. **Não commitar. Não rodar git.**

## 1. Objetivo

A página do celular vira um controle remoto do PC, na mesma rede de casa (outro cômodo):
- **Energia:** desligar, reiniciar, suspender e bloquear, na hora ou com timer (contagem, cancelamento e aviso na tela do PC).
- **Monitores:** modo de projeção (só a principal, só a segunda, estender, duplicar) e apagar ou acordar as telas.
- **Mouse e teclado:** touchpad, texto, teclas especiais e um painel de apresentação/vídeo.
- **Ver a tela:** imagem atualizada, em taxa e qualidade baixas.
- **Janelas:** lista das abertas, trazer para a frente, minimizar e fechar.
- **Outros:** atalhos favoritos, timer de sono da música, área de transferência nos dois sentidos, envio de arquivo do celular para o PC e estado do PC.

**Segurança:** só um celular conectado por vez; cada grupo de ferramentas tem um interruptor nas Configurações, **desligado por padrão**; o pareamento continua como está.

**Leveza (requisito central):** com o celular desconectado ou o grupo desligado, nada novo roda: nenhuma thread, timer, captura ou objeto a mais.

Fica fora desta fase: acesso pela internet; ligar o PC pela rede (Wake-on-LAN, precisa de outro aparelho ligado); arquivo do PC para o celular; imagem na área de transferência; digitar na tela de bloqueio, no UAC e em janelas de administrador (limite do Windows).

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Segundo celular.** Com um celular conectado, outro tenta entrar. | **O mesmo celular substitui a conexão antiga** (recarregar a página ou cair e voltar não trava ninguém). **Outro celular é recusado** e vê "Outro celular está no controle (Android). Desconecte-o pelo PC ou por ele." Ele tenta de novo a cada 5 s, então entra assim que o primeiro sair. Nas Configurações aparece "Conectado: Android" e o botão **"Desconectar"**; a página do celular ganha também um botão "Desconectar". Alternativa: o último a entrar derruba o anterior (mais prático, mas um intruso no Wi-Fi poderia derrubar você). |
| P2 | **Confirmações e aviso de energia.** | A página pede confirmação para **desligar, reiniciar e suspender** (depois de suspenso, não dá para acordar o PC pelo celular). Bloquear não pede. "Agora" executa na hora. Com timer (1 min a 24 h, um por vez): **balão da bandeja** no PC ao programar e 1 min antes, item "Cancelar desligamento programado" no menu da bandeja, contagem e "Cancelar" no celular. O desligamento **não é forçado**: um app com trabalho sem salvar pode segurar, como no menu Iniciar. Observação: no modo "Não incomodar" (jogando), o Windows pode esconder o balão. |
| P3 | **Onde ficam os atalhos favoritos.** | **Uma pasta:** `%LOCALAPPDATA%\MixerDeAudio\Atalhos\`. Você arrasta para lá atalhos de programas (`.lnk`) ou de sites (`.url`), e o celular mostra a lista com o nome do arquivo. As Configurações ganham o botão "Abrir pasta de atalhos". É o mais leve e não deixa o celular criar atalhos (o que permitiria rodar qualquer coisa). Alternativa: um editor de atalhos nas Configurações (mais tela, mais código). |
| P4 | **Envio de arquivo: limite e pasta.** | **Até 500 MB por arquivo**, um por vez, gravado direto no disco (não na memória), em **`Downloads\Do celular`**. O arquivo recebe a marca "veio de outro lugar" (Zone.Identifier), então um programa recebido abre com o aviso do SmartScreen. Balão na bandeja ao receber. |
| P5 | **O que é "modo apresentação/vídeo".** | **Um painel de botões grandes no celular**: slides (anterior, próximo, começar, tela preta, sair) e vídeo (play/pausa, voltar, avançar, volume, tela cheia, mudo), mandando as teclas certas para a janela da frente, mais o interruptor **"Manter a tela ligada"** (o PC não apaga a tela nem suspende enquanto ligado; desliga sozinho em 4 h). |

## 3. O que foi levantado nesta análise (21/11/2026)

- `RemoteServer.cs`: `MaxPhones = 4` (linha 29), contado em `_connections` no `/ws`; acima disso responde 503 antes do upgrade. O navegador não lê o status HTTP de um WebSocket recusado (só vê o código 1006), e a página atual volta ao pareamento quando o WS não abre e `GET /` responde: **uma recusa por HTTP pareceria "chave inválida"**.
- `RemoteSession.cs`: a fila de saída é `Channel` com capacidade 1 e `DropOldest`, feita só para o estado. **Qualquer mensagem nova (lista de janelas, área de transferência) seria descartada** por um estado que chega logo depois.
- Comandos vão todos para a thread da tela (`_ui.TryEnqueue(Execute)`), e as mensagens recebidas têm no máximo 4 KB (`RemoteProtocol.MaxMessageBytes`).
- Corpo HTTP no máximo 1 KB (`RemoteRules.MaxBodyBytes`) e 10 s de prazo por requisição (`HeadTimeout`): não serve para envio de arquivo.
- `index.html`: 13.344 bytes; o teste `PaginaDoCelular_TemMenosDe40KbESemUrlExterna` limita em 40 KB.
- `AppSettings`: `Versao = 1`, `Parse` tolerante, `MaxPhones = 8` chaves pareadas (fica como está).
- `MediaMonitor.Execute(aumid, ação)`: anterior, próxima e play/pausa **por app**; não há "pausar todos".
- `TrayIconService`: H.NotifyIcon com menu `PopupMenu` (Abrir, Configurações, Sair). No `--teste-oculto` o ícone não é criado, então balões só vão para o log.
- Páginas HTTP comuns (não HTTPS) **não têm `navigator.clipboard`** no celular: a área de transferência na página usa uma caixa de texto (copiar e colar à mão, ou `execCommand('copy')` no toque).
- `SendInput` não alcança a tela de bloqueio, o UAC nem janelas de administrador (o app roda sem administrador). `SetForegroundWindow` vindo de um app em segundo plano é barrado pelo Windows, a menos que o app tenha acabado de mandar entrada.

## 4. Decisões

### 4.1 Parte 9a — base, energia e monitores

| # | Decisão |
|---|---|
| D1 | **Grupos de ferramentas** (`Remote\RemoteTools.cs`, puro): ids fixos `energia`, `monitores`, `entrada` (mouse, teclado, apresentação), `tela`, `janelas`, `atalhos`, `transferencia` (área de transferência e arquivo), `estado` (estado do PC e timer de sono). `AppSettings` ganha `FerramentasCelular` (lista de ids, padrão vazia = tudo desligado); `Normalized` mantém só ids conhecidos, sem repetir. `Versao` continua 1. |
| D2 | **Interruptores nas Configurações**, dentro da seção do celular e visíveis com ele ligado: título "Ferramentas do PC", aviso "Ligue só o que for usar: o celular conectado usa o PC como se estivesse no teclado.", e um `ToggleSwitch` por grupo (AutomationIds `ToolPower`, `ToolDisplay`, `ToolInput`, `ToolScreen`, `ToolWindows`, `ToolShortcuts`, `ToolTransfer`, `ToolInfo`). A 9a cria os 8 interruptores; cada parte liga a ferramenta do seu grupo. Desligar um grupo cancela na hora o que dele estiver em andamento (timer de energia, captura, "manter a tela ligada", timer de sono) e atualiza o celular. Desligar o controle pelo celular inteiro, ou sair do app, também cancela tudo. |
| D3 | **Um celular por vez (P1)**, regra pura `SingleSessionPolicy.Decide(hashAtual?, hashNovo)` → `Aceitar`, `Substituir` (mesma chave: fecha a sessão antiga e aceita a nova) ou `Recusar`. Decidido dentro do `lock (_gate)`. `MaxPhones` passa a 1. **Recusa depois do upgrade:** o servidor aceita o WebSocket, manda `{"t":"ocupado","por":"<Nome>"}` e fecha com o código **4009**. Log: `celular: outro celular recusado (ocupado)`. |
| D4 | **Desconectar:** botão `RemoteDisconnect` nas Configurações (visível com um celular conectado) e botão "Desconectar" na página. O servidor manda `{"t":"desconectado"}` e fecha com o código **4000**; a página para de reconectar sozinha e mostra "Desconectado" com o botão "Conectar". A chave continua pareada. `RemoteCount` passa a mostrar "Conectado: <Nome>" ou "Nenhum celular conectado". Log: `celular: desconectado pelo PC`. |
| D5 | **Fila de saída nova** em `RemoteSession`: o estado continua guardando só o mais recente; as outras mensagens vão numa fila FIFO de até 64, nunca descartadas. Passou de 64, a conexão é fechada (celular lento demais). |
| D6 | **Protocolo** (puro, em `RemoteProtocol.cs`):<br>• O `estado` ganha `ferr` (os 8 ids com verdadeiro/falso), `teste` (verdadeiro só com `--teste-pc`) e `pc` com o que existir dos grupos ligados: `energia` (`acao` e `em`, epoch ms, ou nulo), `projecao` (`modo`, `telas` = quantas telas ligadas ao PC), `sono`, `acordado` e `atalhos` (partes seguintes).<br>• Mensagens avulsas (fila de D5): `ocupado`, `desconectado`, `aviso` (`{"t":"aviso","msg":".."}` para falhas, ex. "O Windows recusou a troca de projeção") e, nas partes seguintes, `janelas`, `clip` e `info`.<br>• Comandos novos são ignorados se o grupo estiver desligado. `{"t":"atualizar"}` força um estado novo (a página manda ao abrir uma aba).<br>• `MaxMessageBytes` sobe para **16 KB** (área de transferência, 9d). |
| D7 | **Ações do PC atrás de uma interface:** `Pc\IPcActions.cs` com tudo que mexe no PC (energia, projeção, telas, entrada, janelas, atalho, área de transferência, pausa de mídia, manter acordado). `Pc\RealPc.cs` faz de verdade; `Pc\RecordingPc.cs` **só grava no log** `teste-pc: <ação> <args>`. **`--teste-pc`** (novo em `CommandLine`) escolhe o `RecordingPc` e grava `teste-pc: modo de teste ligado` na partida; nesse modo o `RealPc` nunca é criado. Leitura pura do PC (lista de janelas, projeção atual, captura, CPU) é sempre real. Cada serviço é criado no primeiro uso. |
| D8 | **Energia** (`Pc\PowerTimer.cs`, máquina de estados pura com relógio injetável): `{"t":"energia","acao":"desligar\|reiniciar\|suspender\|bloquear","seg":N}`; `seg` 0 = agora, senão 5 a 86400 (a página oferece 1 min ou mais; o mínimo de 5 s é para o roteiro). Um timer por vez: um novo substitui o anterior; `{"t":"energiaCancelar"}` cancela. Aviso "1 min antes" só se o timer tiver mais de 1 min. O timer é um único `DispatcherQueueTimer` de disparo único, que só existe enquanto há algo programado, e **continua com o celular desconectado**. Não é salvo: sair do app cancela (log).<br>**Execução (RealPc):**<br>• desligar: `%SystemRoot%\System32\shutdown.exe /s /t 0` (sem `/f`; com `/t 0` o Windows não força);<br>• reiniciar: `shutdown.exe /r /t 0`;<br>• suspender: `SetSuspendState(false, false, false)` (powrprof);<br>• bloquear: `LockWorkStation`.<br>**Aviso no PC (P2):** balão da bandeja ao programar e 1 min antes; item "Cancelar desligamento programado" no menu da bandeja enquanto houver timer (o texto troca conforme a ação). Log: `celular: energia <acao> em <seg> s`, `celular: energia cancelada`, `celular: energia <acao> executada`. |
| D9 | **Projeção:** `{"t":"projecao","modo":"principal\|segunda\|estender\|duplicar"}` → `SetDisplayConfig(0, null, 0, null, SDC_APPLY \| SDC_TOPOLOGY_INTERNAL/EXTERNAL/EXTEND/CLONE)`, o mesmo do Win+P (a "principal" é a que o Windows chama de tela do PC). Atual: `QueryDisplayConfig(QDC_DATABASE_CURRENT)`. Telas ligadas ao PC: caminhos de `QDC_ALL_PATHS` com `targetAvailable`, por alvo distinto (contagem pura, testada). Com menos de 2, os botões ficam desativados. Mapeamento modo ↔ flag puro. Falha vira `aviso`. Log: `celular: projecao <modo>`. |
| D10 | **Apagar e acordar as telas:** apagar = `PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, 2)` (não bloqueia, ao contrário do `SendMessage` em broadcast). Acordar = `SendInput` de um movimento de mouse de +1 e −1 px (o `SC_MONITORPOWER -1` não acorda de forma confiável no Windows 11). Risco anotado: em PC com Modern Standby (notebook), apagar a tela pode levar ao modo de espera. Log: `celular: telas apagadas` / `acordadas`. |
| D11 | **Página (9a):** abas no rodapé, só as dos grupos ligados: **Som** (o mixer de hoje), **PC** (energia, monitores; depois estado e timer de sono), **Controle**, **Tela**, **Janelas** e **Enviar**. Sem nenhum grupo ligado, não há barra de abas e a página fica como hoje. Confirmação em uma caixa própria (não `window.confirm`), com botões de 44 px ou mais. Telas de "ocupado" e "desconectado" (D3, D4); "ocupado" tenta de novo a cada 5 s. O limite do `index.html` sobe para **64 KB** (teste atualizado). |
| D12 | **Logs nunca gravam conteúdo:** nada do texto digitado, da área de transferência ou dos arquivos; só tamanhos e nomes de ação. Exceção: no `--teste-pc` o `RecordingPc` grava o conteúdo (só há textos de teste). |

### 4.2 Parte 9b — mouse, teclado, apresentação e ver a tela

| # | Decisão |
|---|---|
| D13 | **Entrada fora da thread da tela:** comandos do grupo `entrada` são executados na própria thread de rede (o `SendInput` é seguro em qualquer thread), sem passar pela fila da tela, para o touchpad não atrasar. O interruptor é lido de uma cópia `volatile` da configuração. |
| D14 | **Mouse:** `{"t":"mouse","dx","dy"}` (o celular junta o movimento a cada ~30 ms e aplica a sensibilidade escolhida na página, guardada no `localStorage`; o PC corta em ±1000); `{"t":"clique","b":"esq\|dir\|meio","duplo":bool}`; `{"t":"botao","b":"esq","baixo":bool}` para arrastar; `{"t":"rolar","dx","dy"}` em "dentes" (±20 por mensagem). **Botão preso:** a sessão guarda os botões apertados e os solta ao fechar a conexão ou ao desligar o grupo. Touchpad na página: 1 dedo move, toque = clique, 2 dedos = rolar, toque com 2 dedos = clique direito, segurar e mover = arrastar. |
| D15 | **Texto e teclas:** `{"t":"texto","s":".."}` (até 1000 caracteres) por `KEYEVENTF_UNICODE`, com pares substitutos e `\n` como Enter. `{"t":"tecla","k":"<nome>"}` **só de uma lista fixa** (`Pc\InputRules.cs`, puro): Enter, Backspace, Tab, Esc, Espaço, Delete, Home, End, PgUp, PgDn, setas, F1 a F12, Win, Ctrl+C/V/X/Z/A/S/F/W/T, Alt+Tab, Alt+F4, Win+D, Ctrl+Shift+Esc, Shift+F5, B, F, M, e as de mídia (play/pausa, próxima, anterior, vol+, vol−, mudo). Nome fora da lista é ignorado. A tradução nome → sequência de `INPUT` (com modificadores apertados e soltos na ordem certa) é pura e testada. |
| D16 | **Painel de apresentação/vídeo (P5)** na aba Controle, usando as teclas de D15: slides (←, →, F5, Shift+F5, B, Esc) e vídeo (Espaço, ←, →, ↑, ↓, F, M). **"Manter a tela ligada":** `{"t":"acordado","on":bool}` → `SetThreadExecutionState(ES_CONTINUOUS \| ES_DISPLAY_REQUIRED \| ES_SYSTEM_REQUIRED)` na thread da tela; desliga com `off`, em 4 h (timer único), ao desligar o grupo ou o controle, ou ao sair do app. Continua com o celular desconectado (para o vídeo não parar quando o celular apaga). |
| D17 | **Ver a tela:** `GET /tela?mon=<i>&w=<largura>` devolve um **JPEG**. Captura por **GDI** (`BitBlt`/`StretchBlt` com `HALFTONE` da tela escolhida para um DIB do tamanho final, cursor desenhado com `GetCursorInfo` + `DrawIconEx`), codificada pelo `BitmapEncoder` (JPEG, qualidade 0,6), que o app já usa. Não usar `Windows.Graphics.Capture`: exige Direct3D, gasta mais memória e mostra a borda amarela. Largura até **1280 px** (cálculo puro de tamanho e proporção). Uma captura por vez; no mínimo 250 ms entre quadros (pedido antes espera). Buffers criados e soltos a cada quadro: nada fica na memória quando ninguém olha. Tela bloqueada ou falha: 503, e a página mostra "Tela indisponível". |
| D18 | **Transmissão opcional, ligada à mão:** abrir a aba Tela **não** transmite nada; ela mostra só o botão "Mostrar a tela" (id HTML `telaIniciar`). A transmissão começa no toque e para com "Parar", ao sair da aba, ao bloquear o celular (`visibilitychange`) ou ao desconectar; voltar à aba exige tocar de novo. Além disso, o grupo `tela` continua com interruptor próprio nas Configurações, desligado por padrão (sem ele, a aba nem aparece).<br>**Ritmo puxado pelo celular:** durante a transmissão, a página pede o próximo quadro só depois de mostrar o anterior, com intervalo de 500 ms (≈ 2 qps; opção "Econômica", 1,5 s). Log só `celular: tela compartilhada` (primeiro quadro) e `celular: tela encerrada` (10 s sem pedido), nunca por quadro. Lista de telas no estado (`EnumDisplayMonitors`): índice, largura, altura e se é a principal. |
| D19 | **Tocar na imagem move o mouse:** `{"t":"mouseAbs","mon":i,"x":0..1,"y":0..1}` → movimento absoluto (`MOUSEEVENTF_ABSOLUTE \| MOUSEEVENTF_VIRTUALDESK`), conta pura do ponto na tela escolhida para a área virtual de 0 a 65535. Abaixo da imagem, botões Clique e Direito. Tocar não clica, para evitar cliques acidentais. Pertence ao grupo `entrada`: sem ele, a imagem só é vista. |
| D20 | **Rotas `/tela` e `/arquivo` (9d):** exigem cookie válido **da chave do celular conectado agora** (senão 409), grupo ligado (senão 403) e, no POST, `Origin` igual ao `Host`. |

### 4.3 Parte 9c — janelas, atalhos e timer de sono

| # | Decisão |
|---|---|
| D21 | **Lista de janelas** como no Alt+Tab: `EnumWindows` com filtro puro (`Pc\WindowFilter.cs`): visível, com título, sem dono, sem `WS_EX_TOOLWINDOW`, não "cloaked" (`DWMWA_CLOAKED`), fora do próprio app. No `--teste-pc`, janelas cujo título começa com `MX-teste` entram mesmo sendo de ferramenta (D30). Cada item: id (o HWND em número), título (até 120 caracteres), nome do processo, id do ícone (o `AppIconCache` pelo `.exe`, servido pelo `/icone`) e se está minimizada. `{"t":"janelas"}` pede a lista; com a aba aberta (`{"t":"assinar","g":"janelas","on":bool}`), ela é relida a cada 3 s e só enviada se mudou. |
| D22 | **Ações:** `{"t":"janela","id":..,"acao":"frente\|minimizar\|fechar"}`. O id tem que estar na lista relida na hora, com o mesmo processo; senão é ignorado (o celular nunca manda mensagem a uma janela qualquer). Frente: `ShowWindow(SW_RESTORE)` se minimizada e `SetForegroundWindow`; se o Windows barrar, um toque de Alt por `SendInput` e nova tentativa. Fechar: `PostMessage(WM_CLOSE)` (o app pode perguntar se salva). **Nunca matar processo.** Log: `celular: janela <acao> (<processo>)`, sem o título. |
| D23 | **Atalhos (P3):** pasta `%LOCALAPPDATA%\MixerDeAudio\Atalhos\`, criada ao ligar o grupo. Só `.lnk` e `.url`, até 30, por nome. A lista vai no `pc.atalhos` do estado, lida ao conectar e a cada `atualizar` (sem observar a pasta). `{"t":"atalho","nome":".."}` só abre um nome que está na lista, pelo shell (`UseShellExecute`), com o caminho montado no PC. Botão `ShortcutsFolder` ("Abrir pasta de atalhos") nas Configurações. Log: `celular: atalho aberto <nome>`. |
| D24 | **Timer de sono:** `{"t":"sono","min":N}` (5 a 240; 0 cancela), na aba PC. Só no `--teste-pc` aceita também `seg` (5 a 60), para o roteiro. Um `DispatcherQueueTimer` único espera sem observar nada. Nos últimos 30 s o hub é adquirido (`Acquire("sono")`), o volume geral desce aos poucos até 0 (passos de 1 s, curva pura testada), os apps de mídia **tocando** são pausados (novo `MediaMonitor.PauseAll`, com `TryPauseAsync`), e o volume geral volta ao valor original; depois `Release`. Continua com o celular desconectado. Estado: `pc.sono` com o horário. Log: `celular: sono em <min> min`, `celular: sono cancelado`, `celular: sono executado (<n> apps pausados)`. |

### 4.4 Parte 9d — área de transferência, arquivo e estado do PC

| # | Decisão |
|---|---|
| D25 | **Área de transferência (só texto):** `{"t":"clipLer"}` → `{"t":"clip","s":".."}` (até 15.000 caracteres; acima disso, cortado e avisado); `{"t":"clipGravar","s":".."}`. Win32 (`OpenClipboard` com até 5 tentativas de 20 ms, `CF_UNICODETEXT`), na thread da tela. Na página: caixa de texto com "Copiar" (`execCommand('copy')` no toque) e "Enviar ao PC". Log: `celular: area de transferencia lida/gravada (<n> caracteres)`. |
| D26 | **Arquivo (P4):** `POST /arquivo?nome=<nome>` com o corpo cru; tratado antes da leitura comum (como o `/ws`), lendo o corpo em blocos direto para `<nome>.parcial` na pasta, com prazo de 30 s **sem dados** (não 10 s no total). `Content-Length` obrigatório, até 500 MB (413). Um envio por vez (409). Espaço livre menor que o tamanho + 1 GB: 507. Nome limpo por regra pura (`Pc\FileNameRules.cs`): sem caminho, sem caracteres proibidos, sem nomes reservados (`CON`, `NUL`…), até 120 caracteres, e "nome (2).ext" se já existir. No fim: renomeia, grava `:Zone.Identifier` com `ZoneId=3`, balão "Arquivo recebido: <nome>". Falha ou conexão caída apaga o `.parcial`. Pasta: `Downloads\Do celular` (`SHGetKnownFolderPath(FOLDERID_Downloads)`). Na página: `XMLHttpRequest` com barra de progresso. Log: `celular: arquivo recebido (<bytes> bytes)`. |
| D27 | **Estado do PC:** com a aba PC aberta (`{"t":"assinar","g":"info","on":bool}`), um timer de 2 s manda `{"t":"info",...}`: CPU % (`GetSystemTimes`, conta pura das diferenças), memória usada e total (`GlobalMemoryStatusEx`), tempo ligado (`GetTickCount64`), bateria se houver (`GetSystemPowerStatus`) e espaço livre do disco do Windows. O timer só existe com a aba aberta e para ao desconectar. |
| D28 | **Modo de teste do grupo `transferencia`:** no `--teste-pc`, a área de transferência é uma variável em memória (a real nunca é lida nem gravada pelo app), a pasta de recebidos é `%TEMP%\mixer-check\recebidos` e o limite cai para 5 MB (para testar o 413 sem enviar 500 MB). A pasta de atalhos passa a `%TEMP%\mixer-check\atalhos`. |

### 4.5 Leveza (todas as partes)

| # | Decisão |
|---|---|
| D29 | Sem celular conectado, nada novo roda, exceto o que o usuário programou e foi feito para continuar: o timer de energia, o de sono e o "manter a tela ligada", cada um um único timer de disparo único. Captura, lista de janelas e estado do PC só funcionam a pedido e com a aba visível. Serviços criados no primeiro uso; nenhum pacote NuGet novo. P/Invokes novos com `LibraryImport` e structs blittable (trimming sem avisos novos). |

## 5. Arquivos

**9a**
- Novos: `Remote\RemoteTools.cs` (D1, D3, puro), `Pc\IPcActions.cs`, `Pc\RealPc.cs`, `Pc\RecordingPc.cs`, `Pc\PowerTimer.cs` (puro), `Pc\DisplayTopology.cs` (Win32 + mapeamento puro), `MixerDeAudio.Tests\RemoteToolsTests.cs`, `PowerTimerTests.cs`, `DisplayTopologyTests.cs`, `tools\testes\roteiro-f9.ps1` (`-Parte a|b|c|d`, `-SemMedicao`).
- Alterados: `Remote\RemoteServer.cs` (D3, D4), `RemoteSession.cs` (D5, despacho por grupo), `RemoteProtocol.cs` (D6) e testes, `Remote\Web\index.html` (D11), `Services\AppSettings.cs` e testes (D1), `Services\CommandLine.cs` e testes (`--teste-pc`), `Flyout\SettingsView.xaml(.cs)`, `ViewModels\SettingsViewModel.cs` (D2, D4), `Tray\TrayIconService.cs` (balão e item de cancelar), `App.xaml.cs` (criação do `IPcActions`, cancelamentos), `Interop\NativeMethods.cs`, `RemoteRulesTests.cs` (64 KB), `tools\testes\celular.cs` e `lib.ps1` (D30).

**9b**
- Novos: `Pc\InputRules.cs` (puro), `Pc\InputSender.cs`, `Pc\ScreenCapture.cs`, `Pc\ScreenRules.cs` (puro), `Pc\KeepAwake.cs`, testes `InputRulesTests.cs`, `ScreenRulesTests.cs`.
- Alterados: `RemoteServer.cs` (`/tela`), `RemoteSession.cs`, `RemoteProtocol.cs`, `index.html`, `IPcActions`/`RealPc`/`RecordingPc`, `NativeMethods.cs`.

**9c**
- Novos: `Pc\WindowFilter.cs` (puro), `Pc\WindowList.cs`, `Pc\Shortcuts.cs`, `Pc\SleepTimer.cs` (puro), testes `WindowFilterTests.cs`, `ShortcutsTests.cs`, `SleepTimerTests.cs`, `tools\testes\janela-teste.cs` (D30).
- Alterados: `Audio\MediaMonitor.cs` e `AudioHub.cs` (`PauseAll`), `RemoteSession.cs`, `RemoteProtocol.cs`, `index.html`, `SettingsView` (botão da pasta), `IPcActions`/`RealPc`/`RecordingPc`.

**9d**
- Novos: `Pc\ClipboardBridge.cs`, `Pc\FileReceiver.cs`, `Pc\FileNameRules.cs` (puro), `Pc\PcInfo.cs` (com a conta pura de CPU), testes `FileNameRulesTests.cs`, `PcInfoTests.cs`.
- Alterados: `RemoteServer.cs` (`/arquivo`), `RemoteSession.cs`, `RemoteProtocol.cs`, `index.html`, `tools\testes\medir.ps1` (grupos ligados), `README.md`, `docs\ESTADO.md`, `docs\PLANO.md`.

## 6. Testes

### 6.1 Testes unitários (novos)

- **9a:** `SingleSessionPolicy` (aceitar, substituir, recusar); grupos em `AppSettings` (padrão vazio, id desconhecido, repetido, arquivo antigo); `PowerTimer` (agora, programar, substituir, cancelar, aviso de 1 min só acima de 1 min, faixa de `seg`, disparo com relógio falso); modo ↔ flag de projeção e contagem de telas a partir de caminhos falsos; protocolo: `ferr`, `teste`, `pc`, comandos novos, faixas, mensagens de 16 KB; `--teste-pc`.
- **9b:** cada nome de tecla → sequência de `INPUT` (modificadores na ordem certa, soltos no fim), nome fora da lista, texto com acento, emoji (par substituto) e `\n`; corte de `dx`/`dy`/rolagem; ponto absoluto na área virtual com telas em posições negativas; tamanho do quadro (proporção, máximo 1280, largura pedida 0 ou absurda).
- **9c:** filtro de janelas (cada regra e a exceção de teste); validação de id e processo; lista de atalhos (só `.lnk`/`.url`, limite 30, nome fora da lista); curva do volume no timer de sono e faixa de minutos.
- **9d:** limpeza de nome (caminho, `..`, reservados, caracteres proibidos, tamanho, colisão "(2)"); CPU % a partir de pares de tempos; corte do texto da área de transferência.
- A bateria inteira roda em toda parte e a cada correção.

### 6.2 Regras do roteiro (obrigatórias)

- **Modo oculto e de teste:** o app sempre com `--teste-oculto --celular-local --teste-pc`. Antes de qualquer comando de ferramenta, o roteiro confere no log a linha `teste-pc: modo de teste ligado` **e** o `celular.cs` confere `"teste":true` no estado; se faltar um dos dois, **aborta sem enviar** (D30).
- **Nunca, em teste automático:** enviar teclas ou cliques reais (toda entrada vai para o `RecordingPc`); desligar, reiniciar, suspender ou bloquear; trocar a projeção; apagar as telas; ler ou gravar a área de transferência pelo app; trazer janela para a frente (rouba o foco). O roteiro lê a área de transferência real (`Get-Clipboard`) antes e depois só para provar que não mudou.
- **Única ação real permitida sobre janelas:** minimizar e fechar a janela da `janela-teste.exe` (título `MX-teste janela`, fora da tela, sem ativar).
- **Captura de tela:** real (só leitura); o roteiro confere cabeçalho, tamanho e dimensões do JPEG e **não grava a imagem em disco**.
- **Configuração:** `BackupConfig`/`RestoreConfig` em toda parte. **Nunca matar um roteiro no meio.** Publicar só depois de `publicado\MixerDeAudio.exe --sair`; o app do usuário é reaberto ao fim.
- **Só esta fase:** sem rodar roteiros antigos; a bateria inteira de testes unitários sempre.

| # | Decisão |
|---|---|
| D30 | **Ferramentas de teste:** `celular.cs` ganha `ferramenta <porta> <chave> <json>` (recusa se o estado não tiver `"teste":true`), `tela <porta> <chave> <mon> <w>` (imprime status, tipo, bytes e dimensões, sem salvar), `arquivo <porta> <chave> <caminho> [nome]` (também exige `"teste":true`) e `segundo` (conecta com outra chave e imprime a mensagem recebida e o código de fechamento). `janela-teste.cs`: janela Win32 com título `MX-teste janela`, `WS_EX_TOOLWINDOW \| WS_EX_NOACTIVATE`, em −10000,−10000, mostrada com `SW_SHOWNOACTIVATE`, que escreve num arquivo de log o que recebe (`WM_CLOSE`, `WM_SIZE` minimizado) e sai ao fechar. `lib.ps1`: `LigarFerramentas([string[]]$grupos)` (grava `FerramentasCelular` no `config.json` de teste), `AssertModoTestePc`, `LogTestePc($padrao)`. |

### 6.3 Roteiro da 9a (`roteiro-f9.ps1 -Parte a`)

| Caso | Por que no roteiro | Passos | Resultado esperado |
|---|---|---|---|
| 9.1 Um celular por vez | Regra no servidor real, com dois WebSockets. | Parear 2 chaves com o mesmo código; conectar A (`segurar`); `segundo` com B; reconectar com A. | B recebe `ocupado` com o nome e fechamento 4009. A segunda conexão de A substitui a primeira (a primeira fecha, a nova recebe o estado). Log com `outro celular recusado` 1 vez; o servidor nunca conta 2. |
| 9.2 Desconectar pelo PC | Botão nas Configurações. | A conectado; `--configuracoes-sem-foco`; ler `RemoteCount`; `RemoteDisconnect` (Invoke). | `RemoteCount` = "Conectado: <nome>" e depois "Nenhum celular conectado". A recebe `desconectado` e fechamento 4000. A chave continua valendo (nova conexão aceita). |
| 9.3 Interruptores | Liga a configuração ao servidor e à tela. | Configuração sem grupos; `estado`; `ferramenta` energia agora; `ToolPower` (Toggle por UIA); `estado`. | Primeiro `ferr` todo falso e **nenhuma** linha `teste-pc: energia`. Depois do toggle, em até 1 s, `ferr.energia` verdadeiro e o `config.json` com `energia`. |
| 9.4 Energia (teste) | Timer real na thread da tela. | Energia ligada. Desligar agora; suspender em 60 s e cancelar; bloquear em 5 s e esperar 7 s; reiniciar em 60 s e desligar `ToolPower`. | Log: `teste-pc: energia desligar`; timer aparece no `pc.energia` e some ao cancelar, sem executar; `teste-pc: energia bloquear` entre 5 e 6,5 s; desligar o grupo cancela o reiniciar. Nenhuma ação real. |
| 9.5 Monitores (teste) | Leitura real da projeção. | Monitores ligado; `estado`; projeção estender; apagar; acordar. | `pc.projecao.modo` igual ao atual de verdade (não muda) e `telas` ≥ 1. Log: `teste-pc: projecao estender`, `teste-pc: telas apagar`, `teste-pc: telas acordar`. |

### 6.4 Roteiro da 9b (`roteiro-f9.ps1 -Parte b`)

| Caso | Por que no roteiro | Passos | Resultado esperado |
|---|---|---|---|
| 9.6 Entrada (teste) | Caminho da rede até o `SendInput`, fora da thread da tela. | Entrada ligada. Mouse 10,−5; clique esq.; clique duplo; botão esq. baixo e desconectar; rolar −3; texto "olá ✓\nMX"; teclas `alt+tab` e `ctrl+v`; tecla `win+r`. | Log `teste-pc: entrada` com as sequências esperadas (contagem de eventos de cada uma); o botão preso é solto ao desconectar (`botao esq cima`); `win+r` (fora da lista) não gera linha. |
| 9.7 Manter acordado (teste) | Ciclo do timer e cancelamentos. | `acordado` on; desconectar; reconectar; desligar `ToolInput`. | `teste-pc: acordado on`; continua depois de desconectar (`pc.acordado` verdadeiro); `teste-pc: acordado off` ao desligar o grupo. |
| 9.8 Ver a tela | Captura GDI e JPEG reais. | Tela ligada. `tela` mon 0 w 640; w 5000; sem cookie; chave de outro celular; `ToolScreen` desligado; 10 quadros seguidos. | 200, `image/jpeg`, largura 640 e altura proporcional; w 5000 dá 1280; 401; 409; 403. Os 10 quadros levam pelo menos 2,25 s (250 ms mínimo). Log com `tela compartilhada` 1 vez e `tela encerrada` 10 s depois. |
| 9.9 Leveza da tela | A captura não pode deixar memória presa. | `medir`: 60 s pedindo quadros a 2 qps, depois 60 s parado. | CPU média durante a captura registrada (meta: abaixo de 5% de um núcleo); bytes privados 60 s depois de parar no máximo 5 MB acima de antes; handles iguais (±10). |

### 6.5 Roteiro da 9c (`roteiro-f9.ps1 -Parte c`)

| Caso | Por que no roteiro | Passos | Resultado esperado |
|---|---|---|---|
| 9.10 Janelas | `EnumWindows` e `WM_CLOSE` reais, na janela de teste. | Janelas ligado; `janela-teste.exe`; `janelas`; minimizar; frente; id inventado; fechar. | A lista tem `MX-teste janela` com o processo certo; o log da janela de teste registra o minimizar e o `WM_CLOSE` e ela sai; frente só gera `teste-pc: janela frente`; o id inventado é ignorado. Nenhuma outra janela recebe nada. |
| 9.11 Atalhos | Pasta e lista reais. | Atalhos ligado; criar `Teste.url` em `%TEMP%\mixer-check\atalhos`; `atualizar`; abrir "Teste"; abrir "..\\x". | `pc.atalhos` = ["Teste"]; log `teste-pc: atalho Teste`; o nome fora da lista é ignorado. |
| 9.12 Timer de sono (teste) | Timer real e cancelamento. | Estado ligado; sono 5 min e cancelar; sono com tempo curto (comando de teste `seg`, só aceito no `--teste-pc`). | `pc.sono` aparece e some; no curto, `teste-pc: sono` (descida do volume e pausa só gravadas). Volume geral e mídia reais iguais. |

### 6.6 Roteiro da 9d (`roteiro-f9.ps1 -Parte d`)

| Caso | Por que no roteiro | Passos | Resultado esperado |
|---|---|---|---|
| 9.13 Área de transferência (teste) | Ida e volta pelo protocolo. | Transferência ligada; `Get-Clipboard` (real); `clipGravar` "MX-teste 123"; `clipLer`; `Get-Clipboard`. | `clip` devolve "MX-teste 123"; a área de transferência real fica igual. |
| 9.14 Arquivo | Corpo grande em streaming, fora do limite de 1 KB. | Enviar 1 MB aleatório; enviar de novo o mesmo nome; nome `..\x.txt`; 6 MB; grupo desligado. | Arquivo em `recebidos` com o mesmo SHA-256 e o `Zone.Identifier`; o segundo vira "(2)"; o nome sai limpo, dentro da pasta; 6 MB dá 413 e nenhum `.parcial` sobra; 403. |
| 9.15 Estado do PC | Timer que só existe com assinatura. | `assinar info` on por 7 s; off; esperar 5 s. | 3 ou 4 `info` com CPU de 0 a 100 e memória coerente; nenhum depois do off. |
| 9.16 Medição | Requisito de leveza com tudo ligado. | `medir.ps1 -ComServidor` com os 8 grupos ligados e sem celular; `medir.ps1 -ComCelular` (50 conexões). | Parado: CPU < 0,1% e WS privado no máximo 2 MB acima do servidor desligado. Celular: bytes privados do ciclo 20 ao 50 < 3 MB e handles estáveis. |

O Opus confere a página no navegador embutido, em 375×812 com `--teste-pc`: abas, confirmação, touchpad, telas de "ocupado" e "desconectado".

### 6.7 Teste do usuário (na entrega)

1. Ligar o controle pelo celular e os grupos aos poucos. Com tudo desligado, a página fica igual à de hoje.
2. **Energia:** bloquear; programar desligar em 2 min e conferir o balão e o item no menu da bandeja; cancelar pelo celular. Opcional: suspender e acordar pelo teclado.
3. **Monitores:** estender, só a principal, duplicar e de volta; apagar as telas e acordar pelo celular.
4. **Mouse e teclado:** touchpad (mover, clicar, rolar, arrastar), texto com acento, Alt+Tab, painel de vídeo num player e "Manter a tela ligada".
5. **Ver a tela:** abrir a aba e conferir que nada é transmitido até tocar em "Mostrar a tela"; tocar na imagem para levar o mouse e clicar pelo botão; "Parar" e sair da aba encerram a transmissão.
6. **Janelas e atalhos:** trazer uma janela para a frente, fechar o Bloco de Notas, abrir um atalho da pasta.
7. **Timer de sono:** 5 min com música tocando: o volume desce, a música pausa e o volume volta.
8. **Transferência e estado:** copiar no PC e ver no celular, mandar texto do celular e colar no PC; enviar uma foto; ver CPU e memória.
9. **Um por vez:** abrir em um segundo aparelho (vê "ocupado"); desconectar o primeiro pelas Configurações.

## 7. Resultados

(preenchido pela implementação, uma subseção por parte)

### Parte 9a

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q`: 0 avisos, 0 erros. **Testes unitários:** 454 aprovados, 0 falhas (eram 373; +81 em `RemoteToolsTests`, `PowerTimerTests`, `DisplayTopologyTests`; `RemoteRulesTests` agora limita o `index.html` em 64 KB, que está com 21 KB). Publicado com o comando do `ESTADO.md` e roteiro rodado em primeiro plano: **22 s**, 0 falhas, 0 linhas `ERRO` no log, `config.json` restaurado igual (diferenças=[]), app do usuário reaberto sem argumentos.

**Roteiro `roteiro-f9.ps1 -Parte a` (o app sempre com `--teste-oculto --celular-local --teste-pc`):**

| Caso | Resultado |
|---|---|
| 9.1 Um celular por vez | OK. B recebeu `ocupado` (`por`="Android") e fechamento **4009**; a segunda conexão de A recebeu o estado e a primeira recebeu `desconectado` e fechou com **4001**; log `outro celular recusado (ocupado)` 1 vez; `(2 no total)` 0 vezes. |
| 9.2 Desconectar pelo PC | OK. `RemoteCount` = "Conectado: Android", depois do `RemoteDisconnect` (Invoke) = "Nenhum celular conectado"; A recebeu `desconectado` e fechamento **4000**; nova conexão da mesma chave aceita; log `desconectado pelo PC` 1 vez. |
| 9.3 Interruptores | OK. `ferr` todo falso e `teste` verdadeiro; comando de energia com o grupo desligado não gerou nenhuma linha `teste-pc: energia`; depois do Toggle de `ToolPower`, `ferr.energia` verdadeiro no celular em **81 ms** (limite 1000) e `config.json` com `energia`. |
| 9.4 Energia (teste) | OK. `teste-pc: energia desligar` 1 vez; suspender em 60 s apareceu em `pc.energia` (`acao`, `em`, `rest` corretos) e sumiu ao cancelar sem executar; bloquear em 5 s executou **5,01 s** depois de programado (faixa 5 a 6,5), uma vez; reiniciar em 60 s foi cancelado ao desligar `ToolPower` (`energia cancelada (grupo desligado)`) e não voltou ao religar; nenhuma linha de `reiniciar` nem `suspender`; nenhum `shutdown.exe`; só 2 ações gravadas. |
| 9.5 Monitores (teste) | OK. `pc.projecao.modo`="estender", `telas`=2 (o Windows tem 2 telas na área de trabalho), iguais antes e depois; log `teste-pc: projecao estender`, `telas apagar`, `telas acordar` 1 vez cada. |

**O que foi feito (resumo):** `Remote\RemoteTools.cs` (grupos, `SingleSessionPolicy`, `RemoteCommandGroups`), `Pc\IPcActions.cs` (interface, `RecordingPc` e `PcActionsFactory`), `Pc\RealPc.cs`, `Pc\PowerTimer.cs`, `Pc\DisplayTopology.cs` (puro) + `DisplayTopology.Native.cs` (Win32), `Pc\PcController.cs` (timer único de energia, projeção, telas, cancelamentos); servidor (um por vez, `ocupado`/4009, `DisconnectAll`/4000), sessão (fila de D5: estado mais recente + FIFO de 64, despacho por grupo), protocolo (`ferr`, `teste`, `pc`, `atualizar`, `desconectar`, `energia`, `energiaCancelar`, `projecao`, `telas`, `ocupado`, `desconectado`, `aviso`; 16 KB), Configurações (8 interruptores, `RemoteDisconnect`), bandeja (balão e item "Cancelar ... programado"), `--teste-pc`, página (abas Som/PC, confirmação própria, telas de ocupado e desconectado), `celular.cs`, `lib.ps1`, `roteiro-f9.ps1`.

**Desvios e decisões menores:**
- `DisplayTopology.cs` ficou como classe `partial`: a parte pura no `.cs` (testada) e as chamadas Win32 em `DisplayTopology.Native.cs` (não entra no projeto de testes). O modo atual vem da topologia devolvida por `QueryDisplayConfig(QDC_DATABASE_CURRENT)`.
- A mensagem de apagar/acordar telas não estava definida na análise: `{"t":"telas","acao":"apagar|acordar"}`.
- O comando `energia` recusa `seg` fora de 0 ou 5 a 86400 (não corta); o estado do timer ganhou o campo extra `rest` (segundos que faltam) além de `acao` e `em`, para a contagem na página não depender do relógio do celular.
- Quando a mesma chave substitui a conexão antiga, a antiga recebe `desconectado` e fecha com **4001** (não em silêncio): sem isso duas abas do mesmo celular se derrubariam em ciclo. A página mostra "substituída" e não reconecta sozinha.
- `PcController` é criado no primeiro uso (`App.Pc`); sem grupo ligado e sem celular ele não existe. O item da bandeja e o balão não existem no `--teste-oculto` (o balão vai só para o log: `celular: balao: ...`).
- D30: `celular.cs` ganhou `ferramenta` e `segundo` (e o `segurar` imprime `MSG`/`FECHOU`); `tela` e `arquivo` ficam para 9b e 9d. `lib.ps1` ganhou `LigarFerramentas`, `AssertModoTestePc`, `LogTestePc` (e auxiliares); `LigarCelularTeste` passou a sempre usar `--teste-pc`.
- Os casos 6.9 e 6.10 do `roteiro-f6.ps1` leem o texto antigo "N celulares conectados" e não valem mais (já não são rodados desde a Fase 8).
- Não verificado nesta parte: a página no navegador a 375×812 (fica para a conferência do Opus; o JavaScript passa em `node --check`), e o balão/menu da bandeja de verdade (o roteiro é oculto, sem ícone).

**Pontos de risco para a conferência:** (1) `RemoteServer.HandleWebSocketAsync`, bloco do `lock (_gate)` com a decisão e a troca de sessões, e o `finally` que só loga e decrementa quando a sessão ainda estava na lista; (2) `RemoteSession.SendLoopAsync` com o fechamento 4000/4001 só depois de esvaziar a fila; (3) `PcController.OnSettingsChanged` (cancela a energia ao desligar o grupo ou o controle) e `Shutdown` (cancela ao sair); (4) `RealPc` nunca é criado com `--teste-pc` (`PcActionsFactory`, coberto por teste) e `App.Pc` só é acessado com algum grupo ligado.

### Parte 9b

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q`: 0 avisos, 0 erros (o `dotnet publish` Release só mostra os dois `IL2104` de assemblies do SDK, `Microsoft.Windows.SDK.NET` e `WinRT.Runtime`, que não vêm do código novo). **Testes unitários:** 541 aprovados, 0 falhas (eram 454; +87 em `InputRulesTests` e `ScreenRulesTests`). Publicado com o comando do `ESTADO.md` e roteiro rodado em primeiro plano (`roteiro-f9.ps1 -Parte b`): **152 s**, 0 falhas, 0 linhas `ERRO` no log, `config.json` restaurado igual (diferenças=[]), app do usuário reaberto.

**Roteiro `roteiro-f9.ps1 -Parte b` (o app sempre com `--teste-oculto --celular-local --teste-pc`):**

| Caso | Resultado |
|---|---|
| 9.6 Entrada (teste) | OK. 10 comandos enviados (código de saída 0); log `teste-pc: entrada`: `mouse 10 -5`, `clique esq`, `clique esq duplo`, `botao esq baixo` seguido de `botao esq cima` (solto ao desconectar), `rolar 0 -3`, `texto 16 eventos: olá ✓\nMX` (7 caracteres × 2 + Enter × 2), `tecla alt+tab 4 eventos`, `tecla ctrl+v 4 eventos`, `mouseAbs 11737 24576` (centro da principal numa área virtual com a segunda tela à esquerda); `win+r` sem nenhuma linha; 10 linhas `entrada` no total. |
| 9.7 Manter acordado (teste) | OK. `teste-pc: acordado on` 1 vez; depois de desconectar e reconectar, `pc.acordado` verdadeiro e nenhum `acordado off`; ao desligar `ToolInput`, `teste-pc: acordado off` 1 vez, `ferr.entrada` falso e `pc` sem `acordado`; com o grupo desligado, `mouse` não gera linha. |
| 9.8 Ver a tela | OK. Tela principal 1920x1080 (2 telas no Windows). `mon 0 w 640`: 200, `image/jpeg`, 25.819 bytes, 640x360 (esperado 640x360); `w 5000`: 1280x720; sem cookie 401; chave de outro celular 409; `ToolScreen` desligado 403; 10 quadros seguidos, todos 200 e JPEG, em **2338 ms** (mínimo 2250, 250 ms entre quadros); log `tela compartilhada` 1 vez e `tela encerrada` 1 vez depois de 10 s sem pedido; nenhum `.jpg` gravado em `%TEMP%\mixer-check`. |
| 9.9 Leveza da tela | OK. 60 s a 2 qps (`celular.exe tela ... 120 500`): 120 de 120 quadros 200 em JPEG, 22 ms por quadro; **CPU média 3,12% de um núcleo** (0,195% da CPU toda; meta < 5%); bytes privados antes 116,3 MB, no fim da captura 122,0 MB, **60 s depois de parar 115,9 MB (diferença −0,4 MB; máximo +5)**; handles antes 1499, depois 1438 (caiu 61; não cresceu). Com a janela de Configurações fechada durante a medição. |

**O que foi feito (resumo):** `Pc\InputRules.cs` e `Pc\ScreenRules.cs` (puros, testados), `Pc\InputSender.cs` (`SendInput`), `Pc\ScreenCapture.cs` (EnumDisplayMonitors, captura GDI para DIB reduzido, cursor, JPEG qualidade 0,6, uma por vez, 250 ms entre quadros, log só no primeiro quadro e em "10 s sem pedido"), `Pc\KeepAwake.cs`; `IPcActions` ganhou entrada e `KeepAwake` (`RecordingPc` grava `teste-pc: entrada ...` e `acordado on|off`); `PcController.SetAwake` com um `DispatcherQueueTimer` de 4 h; `RemoteSession` executa a entrada na thread de rede (D13) e solta os botões presos; `RemoteServer` ganhou `GET /tela` (401, 409, 403, 503); protocolo (`mouse`, `clique`, `botao`, `rolar`, `texto`, `tecla`, `mouseAbs`, `acordado`; estado com `pc.acordado` e `pc.monitores`); página com as abas Controle (touchpad, texto, teclas, apresentação, vídeo, mídia, "Manter a tela ligada") e Tela (só transmite depois de `telaIniciar`); `celular.cs` (`tela`, `ferramenta` com os comandos novos), `lib.ps1` (`CelularTela`), `roteiro-f9.ps1 -Parte b`.

**Desvios e decisões menores:**
- Nomes de teclas em inglês curto e minúsculo (`enter`, `space`, `left`, `pgup`, `alt+tab`, `play`, `volup`...): a lista fixa é a de D15, com "Espaço" = `space`; maiúsculas são aceitas e viram minúsculas. Tecla fora da lista = comando inválido, ignorado.
- Telas ordenadas com a principal primeiro (índice 0) e as outras da esquerda para a direita; o estado leva `pc.monitores` (`i`, `w`, `h`, `principal`) quando o grupo `tela` está ligado (a análise não dava o nome do campo). O quadro nunca é maior que a tela (sem ampliar) e a largura mínima é 160.
- `rolar`: o celular manda os dentes já com o sinal da rolagem natural (dedo para cima rola para baixo); `mouseAbs` usa o centro do pixel (`(px + 0,5) × 65536 / largura`).
- `/tela` com mon fora da lista ou captura que falha devolve 503; 409 também quando nenhum celular está conectado.
- Ao encerrar a transmissão (10 s sem pedido) o app compacta o monte de objetos grandes e chama o `MemoryTrimmer`: sem isso os quadros de 1 MB deixavam +6,7 MB de bytes privados presos (reprovava o 9.9 na primeira rodada).
- O critério de handles do 9.9 virou "não crescer mais de 10" (a análise dizia "iguais ±10"): na medição o total cai ~60 depois do trim, o que não é vazamento.
- `celular.exe tela` ganhou o 5º e 6º argumentos (`n` quadros e período em ms) para o ritmo de 2 qps do 9.9 e os 10 quadros seguidos do 9.8; o roteiro monta o JSON do texto com `\uXXXX` porque o PowerShell 5.1 estraga argumentos com espaço e aspas escapadas.
- `NativeMethods`: as structs `INPUT`/`MOUSEINPUT` deixaram de ser privadas e ganharam `KEYBDINPUT` e a união (todas blittable); todos os P/Invokes novos são `LibraryImport`.
- Não verificado nesta parte: a página no navegador a 375×812 (touchpad, aba Tela, confirmação do Alt+F4; o JavaScript passa em `node --check`) e a entrada real pelo `SendInput` (por regra, só no `RealPc`, que nunca roda nos testes).

**Pontos de risco para a conferência:** (1) `RemoteSession.RunInput`, `SyncInput`, `DisableInput` e `ReleaseHeld` (lock `_inLock`, cópia `_input` nula = grupo desligado, botões soltos ao desconectar e ao desligar o grupo); (2) `RemoteServer.ServeScreenAsync` (ordem 401, 409, 403, 503) e a criação preguiçosa do `ScreenCapture`; (3) `ScreenCapture.Grab` (liberação dos objetos GDI no `finally`), `DrawCursor` e `OnIdle` (trim); (4) `PcController.SetAwake` e `OnSettingsChanged` (cancela ao desligar o grupo ou o controle, e `Shutdown`); (5) `InputSender` e a união `INPUT` em `NativeMethods` (tamanho 40 bytes em x64); (6) `index.html`: `telaStop` em `view`, `onclose`, `visibilitychange` e `pagehide` (D18) e o touchpad (`pointerdown`/`pointermove`/`padEnd`).

### Parte 9c

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q`: 0 avisos, 0 erros (o `dotnet publish` Release só mostra os mesmos dois `IL2104` de assemblies do SDK). **Testes unitários:** 663 aprovados, 0 falhas (eram 541; +122 em `WindowFilterTests`, `ShortcutsTests` e `SleepTimerTests`). Publicado com o comando do `ESTADO.md` (o app do usuário, que estava aberto em `publicado\`, foi fechado com `--sair` antes e reaberto sem argumentos no fim) e roteiro rodado em primeiro plano (`roteiro-f9.ps1 -Parte c`): **25 s**, 0 falhas, 0 linhas `ERRO` no log, `config.json` restaurado igual (diferenças=[]).

**Roteiro `roteiro-f9.ps1 -Parte c` (o app sempre com `--teste-oculto --celular-local --teste-pc`):**

| Caso | Resultado |
|---|---|
| 9.10 Janelas | OK. Lista com 11 janelas; `MX-teste janela` com `id` igual ao HWND impresso pela `janela-teste.exe`, `proc`=`janela-teste`, `min`=falso, ícone servido (`GET /icone/<id>` = 200); nenhuma janela do próprio app e nenhuma sem título. Minimizar: o log da janela de teste registrou `WM_SYSCOMMAND minimizar` e o app gravou `teste-pc: janela minimizar`. Frente: só `teste-pc: janela frente`; a janela de teste nunca foi ativada. Id inventado (12345): `celular: janela ignorada (fora da lista)` e a janela continuou viva, sem `WM_CLOSE`. Fechar: `WM_CLOSE` registrado e o processo saiu; `teste-pc: janela fechar` 1 vez. As 3 ações do app foram todas em `(janela-teste)`; depois de fechar, a janela sumiu da lista. |
| 9.11 Atalhos | OK. Pasta `%TEMP%\mixer-check\atalhos`: sem atalhos, `pc.atalhos` vazio; com `Teste.url` (e um `nota.txt` que não conta) `pc.atalhos`=["Teste"]; `teste-pc: atalho Teste` 1 vez e `celular: atalho aberto Teste` 1 vez; `..\x`, `../x` e `nota` ignorados (`celular: atalho ignorado`) sem nova linha `teste-pc: atalho`. |
| 9.12 Timer de sono (teste) | OK. Sono 5 min: `pc.sono` apareceu (`rest` entre 240 e 300, `em` coerente) e sumiu ao cancelar; logs `sono em 5 min` e `sono cancelado` 1 vez; nenhuma linha `teste-pc: sono` antes de executar. Curto (`seg` 8): `pc.sono` apareceu, executou em 8,3 s e sumiu; 9 linhas `teste-pc: sono volume` (0,58 0,50 0,41 0,33 0,25 0,17 0,08 0,00 e a volta ao original, 0,66), `teste-pc: sono pausar` 1 vez, `sono executado (0 apps pausados)`. Volume geral real antes 0,66 e depois 0,66, mudo igual. |

**O que foi feito (resumo):** `Pc\WindowFilter.cs` (filtro, alvo da ação e tipos, puro), `Pc\WindowList.cs` (`EnumWindows`, ações por mensagem, mapa de ícones), `Pc\Shortcuts.cs` (regras puras e pasta), `Pc\SleepTimer.cs` (faixa, curva e plano, puro); `IPcActions`/`RealPc`/`RecordingPc` ganharam `ControlWindow`, `OpenShortcut`, `SleepVolume` e `SleepPauseAll`; `MediaMonitor.PauseAllAsync` (só `TryPauseAsync` nos que tocam) e `AudioHub.PauseAllMediaAsync`; `App.Hub`; `PcController` (janelas, atalhos e o timer de sono com um único `DispatcherQueueTimer`, `Acquire("sono")` só durante a descida); `RemoteSession` (`janelas`, `janela`, `atalho`, `sono`, `assinar`, assinatura de 3 s só com a aba aberta, lista de atalhos lida ao conectar e a cada `atualizar`, `pc.atalhos` e `pc.sono`); `RemoteProtocol` (comandos e `BuildWindows`); `/icone` serve os ícones das janelas; `ShortcutsFolder` nas Configurações; página (aba Janelas com janelas e atalhos, timer de sono na aba PC); `janela-teste.cs`, `celular.cs` (`ferramenta` aceita os comandos novos e imprime as mensagens avulsas recebidas), `lib.ps1` (`CelularFerramentaMsgs`, `StartJanelaTeste`, `JanelaTesteLog`, `StopJanelaTeste`), `roteiro-f9.ps1 -Parte c`.

**Desvios e decisões menores:**
- Minimizar e fechar mandam mensagem à janela por `PostMessage` (`WM_SYSCOMMAND`/`SC_MINIMIZE` e `WM_CLOSE`), sem esperar resposta: um app travado não trava o Mixer. Por isso a `janela-teste.exe` registra `WM_SYSCOMMAND minimizar` (não `WM_SIZE minimizado`) e **não minimiza de verdade** (uma janela de ferramenta minimizada apareceria como barrinha no canto da tela); ela também registra `WM_SIZE minimizado` se um dia minimizar.
- D22, "o mesmo processo": o comando não leva o processo; o PC guarda a última lista mostrada ao celular e só age se o id estiver nela **e** na lista relida na hora com o mesmo nome de processo (o Windows reaproveita HWNDs). Sem lista mostrada antes, tudo é ignorado.
- No `--teste-pc`, `PcController.ControlWindow` grava a ação no `RecordingPc` e só faz minimizar/fechar de verdade se o título começa com `MX-teste` **e** o processo é `janela-teste`; "frente" nunca é real no modo de teste.
- Mensagens: `{"t":"janelas"}` (pedir; o PC responde `{"t":"janelas","lista":[{id,titulo,proc,icone,min}]}`), `{"t":"janela","id","acao"}`, `{"t":"atalho","nome"}`, `{"t":"sono","min"}` (0 cancela) e `seg` só no `--teste-pc`, `{"t":"assinar","g":"janelas|info","on"}` (o `info` é aceito pelo protocolo mas só a 9d o trata). Grupos: `janelas` (janelas), `atalhos`, `estado` (sono). O ícone da janela é `IconId("janela:" + caminho do .exe em minúsculas)`, e o `/icone` o resolve pelo mapa da última lista (só existe com o grupo ligado; limpo ao desligar).
- Lista de janelas: até 60 itens; só envia se mudou; a página pede a lista de novo 900 ms depois de cada ação. Atalhos: o mesmo nome com `.lnk` e `.url` mostra só o `.lnk`.
- Com `seg` a descida dura o tempo todo (até 30 s) e o volume restaurado vai para o log do `RecordingPc` (`teste-pc: sono volume <v>`). Com o grupo `estado` desligado, ou ao sair do app, o sono é cancelado e o volume volta ao original se já descia.
- O botão "Abrir pasta de atalhos" (`ShortcutsFolder`) só aparece com o grupo ligado e, no `--teste-pc`, só cria a pasta (não abre o Explorer).
- A página não foi verificada no navegador a 375×812 (o JavaScript passa em `node --check`; `index.html` 37 KB, limite 64 KB). O caminho real de `PauseAll` (mídia de verdade) e o `SetForegroundWindow` real só rodam no `RealPc`, que por regra nunca roda nos testes.

**Pontos de risco para a conferência:** (1) `PcController.ControlWindow` e `WindowList.Resolve`/`Perform` (guarda dupla do `--teste-pc`: título `MX-teste` e processo `janela-teste`; id conferido contra a lista mostrada e a relida); (2) `PcController.ScheduleSleep`, `OnSleepTick`, `FinishSleepAsync` e `CancelSleepCore` (um timer só, `_sleepGen` contra o cancelamento durante a pausa, `Acquire`/`Release("sono")`, volume original lido do hub e devolvido); (3) `MediaMonitor.PauseAllAsync` (só quem está tocando, só `TryPauseAsync`); (4) `RemoteSession` (`StartWindows`/`StopWindows`/`OnSettingsChanged`, `RefreshShortcuts`, `ExecuteSleep` com o `seg` só em `App.TestPc`); (5) `WindowList.EnumProc` (callback nativo sem exceção, ordem barata→cara dos testes, `ExeByPid`); (6) `ShortcutRules.Resolve` e `PcController.OpenShortcut` (o caminho nunca vem do celular); (7) `RemoteServer.ServeIconAsync` (`WindowList.IconPathFor`, só ids do mapa da última lista).

### Parte 9d

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q`: 0 avisos, 0 erros (o `dotnet publish` Release só mostra os mesmos dois `IL2104` do SDK). **Testes unitários:** 757 aprovados, 0 falhas (eram 663; +94 em `FileNameRulesTests` e `PcInfoTests`; `index.html` com 42.027 bytes, limite 64 KB; `publicado\` com 89,8 MB). Publicado com o comando do `ESTADO.md` (o app do usuário foi fechado com `--sair` antes e **reaberto sem argumentos** no fim). Roteiro `roteiro-f9.ps1 -Parte d` em primeiro plano: **23 s**, 0 falhas, 0 linhas `ERRO`, `config.json` restaurado igual (diferenças=[]).

| Caso | Resultado |
|---|---|
| 9.13 Área de transferência | OK. `clipGravar` "MX-teste 123" só na memória do `RecordingPc`; `clipLer` devolveu `clip` com o mesmo texto; 15.500 caracteres gravados voltam como 15.000; `Get-Clipboard` real igual antes e depois; logs "lida/gravada (12 caracteres)" 1 vez cada. |
| 9.14 Arquivo | OK. 1 MB aleatório: 200, mesmo SHA-256 e `Zone.Identifier` com `ZoneId=3`; mesmo nome de novo: "foto (2).bin"; nome `..\x.txt`: gravado como `x.txt` dentro de `recebidos` e nada fora; 6 MB: 413, nenhum `.parcial`, nada gravado; grupo desligado pelo `ToolTransfer`: 403; log com `arquivo recebido (1048576 bytes)` x3, balão x3, `recusado (413)` x1 e nenhum nome de arquivo. |
| 9.15 Estado do PC | OK. `assinar info` por 7 s: 4 `info` (CPU 20 5 4 4, memória 11,7 de 15,9 GB, disco 88 GB livres, sem bateria); 0 depois do off e de 5 s de espera. |
| 9.16 Medição (`medir.ps1`, 8 grupos ligados, `--teste-pc`) | OK. Base (servidor desligado): WS privado parado 1,8 MB, CPU 0,003%. Servidor + 8 grupos sem celular: WS privado 1,9 MB (+0,1; limite +2), CPU 0,003% (limite 0,1%), handles 632 (iguais). Celular + 8 grupos, 50 conexões: bytes privados ciclo 20 = 72,6 MB e ciclo 50 = 72,2 MB (**-0,4 MB**; limite 3), handles 760 no ciclo 20 e 772 no 50 (estáveis), CPU parada 0,007%. `config.json` restaurado igual nas três rodadas; medição total 583 s. |

**O que foi feito (resumo):** `Pc\ClipboardBridge.cs` (corte, puro) e `.Native.cs` (Win32, 5 tentativas de 20 ms), `Pc\FileNameRules.cs` (puro: nome, colisão, limites, pastas, query), `Pc\FileReceiver.cs` (blocos de 64 KB para `.parcial`, 30 s sem dados, um por vez, `Zone.Identifier`, Downloads por `SHGetKnownFolderPath`), `Pc\PcInfo.cs` (puro: CPU, bateria) e `.Native.cs` (`GetSystemTimes`, `GlobalMemoryStatusEx`, `GetSystemPowerStatus`, `GetDiskFreeSpaceEx`); `IPcActions` ganhou `ReadClipboard`/`WriteClipboard` (`RecordingPc` guarda em memória); `PcController` (`ReadClipboard`, `WriteClipboard`, `Files`, `Balloon`); `RemoteRules.IsUpload`/`UploadLength`; `RemoteServer.ServeUploadAsync` e `ReadRequestAsync` (o `/arquivo` não lê o corpo); protocolo (`clipLer`, `clipGravar`, `clip`, `info`, `assinar info`); `RemoteSession` (timer de 2 s só com a aba PC assinada); página (aba Enviar e "Estado do PC"); `celular.cs` (`arquivo`, `info`, `clipLer`/`clipGravar`), `lib.ps1` (`CelularArquivo`, `CelularInfo`), `roteiro-f9.ps1 -Parte d`, `medir.ps1` (`-TodosGrupos`, `-NaoReabrir`); README, `ESTADO.md` e `PLANO.md`.

**Desvios e decisões menores:**
- `DriveInfo` foi trocado por `GetDiskFreeSpaceExW`: no publicado a assembly `System.IO.FileSystem.DriveInfo` não existe e o envio caía em silêncio (um `FileNotFoundException` é `IOException`, engolido pelo servidor). `FileReceiver` agora loga `arquivo interrompido (<tipo>, <hresult>)` sem nome nem caminho.
- Respostas de erro do `/arquivo` (401, 409, 403, 411, 400, 413, 507) são enviadas sem ler o corpo e o servidor descarta até 16 MB do que o celular ainda manda, para o navegador ler a resposta em vez de um reset. `Origin` ausente é aceito (como no `/parear`); presente e diferente do `Host` dá 403. Sem `Content-Length`: 411; vazio: 400.
- Resposta 200 do envio: `{"ok":true,"nome":"..","bytes":N}`. O balão leva o nome, mas o log só "Arquivo recebido" (D12).
- `info`: a primeira mensagem sai ao assinar, com a CPU média desde que o PC ligou; as seguintes a cada 2 s. Campos: `cpu`, `mem{usada,total}`, `ligado`, `bateria{pct,carregando}|null`, `disco{livre,total}` (disco do Windows). `clipGravar` na página é limitado a 4.000 caracteres (mensagem de 16 KB); o PC corta em 15.000.
- As regras puras do corte da área de transferência ficaram em `ClipboardBridge.cs` (classe `partial`), testadas em `FileNameRulesTests`.
- Roteiro: o 9.16 roda à parte (`medir.ps1`), não dentro do `-Parte d`. `PrepararFerramentas` agora falha se o `.exe` ficou mais velho que o `.cs` (uma compilação com erro era ignorada). `celular.exe info` usa um único leitor (cancelar `ReceiveAsync` aborta o WebSocket).
- Não verificado: a página (aba Enviar, "Estado do PC", barra de progresso) no navegador a 375×812 (o JavaScript passa em `node --check`); o `RealPc` (área de transferência real, Downloads real) por regra nunca roda nos testes; `ESTADO.md` não recebeu tabela da medição da Fase 9 (os números estão acima).

**Pendente:** teste do usuário (seção 6.7) e a conferência da página no navegador embutido pelo Opus.

**Pontos de risco para a conferência:** (1) `RemoteServer.ReadRequestAsync` e `HandleAsync` (o `/arquivo` sai antes da leitura comum) e `ServeUploadAsync` (ordem 401, 409, 403, 411/400, depois `FileReceiver`; `DrainAsync`); (2) `FileReceiver.ReceiveAsync` (um por vez, limpeza do `.parcial` no `finally`, `Volatile.Write(_busy, 0)`, prazo ocioso); (3) `FileNameRules.Clean` (caminho, reservados, 120 caracteres); (4) `RemoteSession.StartInfo`/`StopInfo` (timer só com a assinatura; para ao desconectar e ao desligar o grupo `estado`); (5) `ClipboardBridge.WriteText` (posse da memória após `SetClipboardData`); (6) `PcController.ReadClipboard`/`WriteClipboard` e o `RecordingPc` (nunca toca a área real).

**Conferência (Opus, 22/11/2026)**
- Build sem avisos e 757 testes aprovados a cada parte. Conferidos os pontos de risco:
  - 9a: decisão de um celular por vez dentro do `lock`; o `RealPc` nunca criado no `--teste-pc`; flags de `SetDisplayConfig`; `shutdown /t 0` sem `/f`.
  - 9b: `INPUT` com união de 40 bytes; objetos GDI soltos no `finally`.
  - 9c: guarda dupla da janela de teste; cancelar o sono no meio da pausa devolve o volume e solta o hub.
  - 9d: `/arquivo` tratado antes da leitura comum; `.parcial` apagado no `finally`; memória da área de transferência liberada só se o `SetClipboardData` falhar.
- **Página no navegador embutido (375×812, `--teste-pc`, 8 grupos):**
  - pareamento pelo `#c=`; abas Som, PC, Janelas, Controle, Tela e Enviar;
  - confirmação de desligar gravou só `teste-pc: energia desligar`;
  - touchpad (arrastar gerou `entrada mouse 128 64`, sem clique) e botão Clique;
  - **aba Tela sem nenhum pedido de imagem até "Mostrar a tela"**; depois cerca de 2 quadros por segundo; ao sair da aba, os pedidos pararam (D18);
  - `config.json` igual (`diferencas=[]`) e app do usuário reaberto.
- **Corrigido na conferência:** em 375 px a barra de abas apertava "Controle" e "Tela" e cortava "Enviar". `#tabs button` passou a `flex:1 1 0; min-width:0; padding:0 2px; font-size:13px` com reticências: as 6 abas cabem iguais (62 px cada). Republicado em `publicado\`.
- Sem pendências de código. O `RealPc` (entrada, energia, projeção, área de transferência e Downloads reais) e o balão da bandeja ficam para o teste do usuário (6.7).
