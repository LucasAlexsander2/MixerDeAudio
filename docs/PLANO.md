# Mixer de Áudio — Plano

Fonte original: `Mixer de Áudio para Windows – Documento de Design.pdf` (raiz do repositório). Este arquivo resume o PDF e registra as decisões que o complementam ou alteram.

## Produto

App de bandeja para o Windows 11. Um clique no ícone abre uma janelinha (~420 px, cantos arredondados, Mica) acima do ícone, que fecha ao clicar fora.
- **Topo:** volume geral, mudo e botões dos dispositivos favoritos (o ativo fica destacado; um clique troca a saída padrão; apps com saída própria continuam onde estão).
- **Lista:** cada app que está tocando, com ícone, nome, volume, mudo e botões dos favoritos (um clique troca a saída daquele app).
- **Configurações:** 2 ou 3 favoritos, tema claro/escuro/automático, iniciar com o Windows.

## Metas de leveza (requisito central)

- CPU perto de 0% em repouso; memória alvo de até ~50 MB com a janela fechada.
- Só eventos do Windows, sem polling (`IMMNotificationClient`, `IAudioSessionNotification`, `IAudioSessionEvents`).
- A janela é criada ao clicar no ícone e liberada ao fechar; em repouso fica só o ícone.
- Medidor de nível (VU) desligado por padrão ou só com a janela aberta.
- Cache de ícones; `Dispose` correto dos objetos COM.
- Publish Release com ReadyToRun (trimming e Native AOT avaliados no fim, ver A4).
- Medir memória e CPU em toda fase (`tools\testes\medir.ps1`).

## Decisões de arquitetura

| # | Decisão |
|---|---|
| A1 | **C# / .NET 10 + WinUI 3** (Windows App SDK 2.5.1, o mesmo do MeuBloco). Só Windows 11: `TargetPlatformMinVersion` 10.0.22000.0. |
| A2 | **Sem MSIX** (`WindowsPackageType=None`, Windows App SDK self-contained), **diferente do PDF** ("Packaged"). Motivo: roda direto pelo `.exe`, sem certificado nem Modo de Desenvolvedor, como o MeuBloco. "Iniciar com o Windows" vira a chave `HKCU\...\Run`. O roteamento por app (IAudioPolicyConfig) funciona sem pacote. |
| A3 | **Áudio com NAudio** (etapas 2 e 3) e **IAudioPolicyConfig** próprio (etapa 4, GUID da interface do Windows 11). |
| A4 | **AOT/trimming só no fim (etapa 6):** o NAudio usa COM interop clássico, incompatível com Native AOT. Se a memória exigir, a etapa 6 avalia trocar o NAudio por interfaces COM próprias com `[GeneratedComInterface]`. |
| A5 | **Ícone da bandeja com H.NotifyIcon.WinUI**, menu do botão direito nativo (`PopupMenu`), sem janela principal: o app vive sem janela (`DispatcherShutdownMode.OnExplicitShutdown`). |
| A6 | **Instância única** pelo `AppInstance` do Windows App SDK; abrir o `.exe` de novo abre a janelinha na primeira cópia. |
| A7 | **Lógica pura em arquivos sem WinUI** (posição da janela, regras de alternância, mais tarde o modelo das sessões), compilada também no projeto de testes por link, como no MeuBloco. |
| A8 | **Dados** em `%LOCALAPPDATA%\MixerDeAudio\` (`config.json`, `logs\`). |
| A9 | **Troca da saída padrão com IPolicyConfig** (não documentada, estável desde o Windows 7, usada pelo EarTrumpet e pelo SoundSwitch), escrita com `[GeneratedComInterface]`. A troca muda a saída padrão e a de comunicação. |
| A10 | **Controle pelo celular só na rede de casa** (decidido em 20/11/2026, fora do PDF). O próprio app serve uma página leve (servidor HTTP/WebSocket pequeno, sem ASP.NET completo), que espelha tudo da janelinha: saídas, volume geral e apps com volume, mudo e saída. O celular conecta pelo endereço ou por um QR code e informa um token gerado no PC. O recurso fica desligado por padrão, com um interruptor nas Configurações, e o áudio só é observado enquanto há um celular conectado. Acesso pela internet (site hospedado) fica para depois, se for preciso. |
| A11 | **Controles de música por app** (decidido em 21/11/2026, fora do PDF). Anterior, play/pausa e próxima, com o nome da faixa, na linha de cada app que se registra no controle de mídia do Windows (GSMTC), na janelinha e no celular. O controle só é observado enquanto o núcleo de áudio está ativo (janela aberta ou celular conectado). |
| A12 | **Controle do PC pelo celular** (decidido em 21/11/2026, fora do PDF). A página do celular ganha ferramentas para usar o PC à distância, continuando só na rede de casa (A10).<br>- **Energia:** desligar, reiniciar, suspender e bloquear, na hora ou com timer (com contagem, cancelamento e aviso na tela).<br>- **Monitores:** modo de projeção (só a principal, só a segunda, estender, duplicar) e apagar ou acordar as telas.<br>- **Mouse e teclado:** touchpad, envio de texto e teclas especiais.<br>- **Ver a tela:** imagem atualizada.<br>- **Janelas:** lista das abertas, trazer para a frente e fechar.<br>- **Outros:** atalhos favoritos, timer de sono da música, área de transferência nos dois sentidos, envio de arquivo, estado do PC e modo apresentação/vídeo.<br>**Segurança:** só **um celular conectado por vez**; o pareamento continua como hoje (código de 6 dígitos ou QR code). |

## Etapas

| Fase | Conteúdo | Estado |
|---|---|---|
| 1 | Projeto WinUI 3, ícone na bandeja, janelinha abrindo e fechando, medição inicial | concluída (teste do usuário aprovado em 18/11/2026) |
| 2 | Apps tocando som, com volume e mudo individuais (NAudio, por eventos) | concluída (teste do usuário aprovado em 19/11/2026) |
| 3 | Volume geral e troca da saída padrão | concluída (teste do usuário aprovado em 19/11/2026) |
| 4 | Roteamento de saída por app (IAudioPolicyConfig) | concluída (teste do usuário aprovado em 19/11/2026) |
| 5 | Configurações e dispositivos favoritos | concluída (teste do usuário aprovado em 20/11/2026) |
| 6 | Controle pelo celular na rede de casa (página servida pelo próprio app, token) | concluída (teste do usuário aprovado em 21/11/2026) |
| 7 | Controles de música por app (anterior, play/pausa, próxima) na janela e no celular | concluída (teste do usuário aprovado em 21/11/2026) |
| 8 | Polimento visual, otimização final (trimming/AOT) e instalador | concluída (aprovada pelo usuário em 21/11/2026) |
| 9 | Controle do PC pelo celular (A12), em partes | — |

As pendências que os documentos das fases 1 a 5 marcam para a "Fase 6" passaram para a **Fase 8**, depois das renumerações de 20/11/2026 e 21/11/2026: handles, bytes privados, trimming/AOT, Mica ou Acrylic, altura adaptável e roda do mouse nos sliders.
- **Memória por abertura (medida em 20/11/2026):** cerca de 0,15 MB de bytes privados e 3 handles a mais a cada abertura da janela, igual nas Fases 4 e 5 (150 ciclos: +21 MB). Fica para a Fase 8, comparando com reaproveitar uma janela escondida.
