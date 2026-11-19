# Mixer de Áudio — Plano

Fonte original: `Mixer de Áudio para Windows – Documento de Design.pdf` (raiz do repositório). Este arquivo resume o PDF e registra as decisões que o complementam ou alteram.

## Produto

App de bandeja para o Windows 11. Um clique no ícone abre uma janelinha (~360 px, cantos arredondados, Mica) acima do ícone, que fecha ao clicar fora.
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

## Etapas

| Fase | Conteúdo | Estado |
|---|---|---|
| 1 | Projeto WinUI 3, ícone na bandeja, janelinha abrindo e fechando, medição inicial | concluída (teste do usuário aprovado em 18/11/2026) |
| 2 | Apps tocando som, com volume e mudo individuais (NAudio, por eventos) | concluída (teste do usuário aprovado em 19/11/2026) |
| 3 | Volume geral e troca da saída padrão | — |
| 4 | Roteamento de saída por app (IAudioPolicyConfig) | — |
| 5 | Configurações e dispositivos favoritos | — |
| 6 | Polimento visual, otimização final (trimming/AOT) e instalador | — |
