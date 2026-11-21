# Fase 7 — Controles de música por app

**Status:** concluída (teste do usuário aprovado em 21/11/2026)
**Implementação:** Sonnet (`medium`), em duas partes: **7a** (núcleo e janelinha) e depois **7b** (celular) · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também:
- `docs/PLANO.md`, decisão A11;
- `docs/fases/fase-6.md`, seções 4 e 7: `AudioHub`, protocolo, página e `celular.cs`;
- `docs/fases/fase-4.md`, D9, sobre a linha do app.

Pasta do repositório: `C:\Projetos\Mixer de Audio`, branch `main`, último commit `7957f56`. **Não commitar.**

## 1. Objetivo

Na linha de cada app que toca mídia (Opera, Chrome, Edge, Firefox, Spotify, players), a janelinha e o celular mostram:
- **anterior, play/pausa e próxima**;
- o **nome da faixa e o artista**.

A fonte é o controle de mídia do Windows (GSMTC, `Windows.Media.Control`), o mesmo do painel que aparece com as teclas de volume. Apps que não se registram nele não ganham os botões.

Fica fora desta fase:
- capa do álbum;
- barra de posição e tempo da faixa;
- escolher a aba quando o navegador toca em várias abas, porque o Windows mostra uma só;
- teclas de mídia globais;
- instalador e polimento (Fase 8).

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **App com mídia, mas sem linha de som.** Verificado nesta máquina: o Spotify pausado **solta o som do Windows** e some da lista, mas continua no controle de mídia. Sem a linha, não daria para dar play pelo mixer. | **Mostrar uma "linha só de mídia"**, com ícone, nome, faixa e os 3 botões, sem volume, mudo nem saída. Ela fica no fim da lista. Quando o app volta a tocar, vira a linha normal. |
| P2 | **Onde ficam os botões na janelinha.** A linha já tem o nome com os botões de saída e, abaixo, o volume. | **Uma 3ª linha só nos apps com mídia:** ⏮ ⏯ ⏭ à esquerda e "Faixa · Artista" cortado com reticências. A linha passa de 56 para cerca de 86 px só nesses apps. Alternativa: botões no lugar do número do volume, mas aí fica sem o nome da faixa. |
| P3 | **Testes automáticos.** Apertar play/pausa num app real atrapalharia o que você está ouvindo. | Os roteiros **só leem** as sessões reais. Os botões são apertados apenas numa **fonte de mídia de teste**, o tocador `tocar.exe` com `--midia`, que se registra no controle de mídia como os players. Isso foi provado nesta análise: o Windows entregou "próxima" e "pausa" a uma fonte de teste, e Opera e Spotify não foram tocados. |

## 3. O que foi verificado nesta máquina (21/11/2026)

- `GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` funciona em app não empacotado e leva cerca de 40 ms.
- Sessões encontradas:
  - Opera GX: AUMID `OperaSoftware.OperaGXWebBrowser.1786565045`, com estado, título e "canal". Anterior e próxima vêm desligados quando o site não oferece.
  - Spotify (Loja): `SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify`.
- O AUMID **não é o nome do processo**. Para ligar à linha de som, cuja chave é o caminho do `.exe` em minúsculas (`SessionTracker.KeyFor`):
  - **Apps comuns:** a pasta de apps do Windows (`shell:AppsFolder`) resolve o AUMID para o `.exe` em cerca de 13 ms, com `ParseName` e `System.Link.TargetParsingPath`. Exemplos: Opera GX para `...\Opera GX\opera.exe`, `Chrome` para `chrome.exe`, `MSEdge` para `msedge.exe`. Ela também dá o nome amigável ("Navegador Opera GX").
  - **Apps da Loja:** `GetApplicationUserModelId(processo)` do kernel32 devolve o AUMID exato. Spotify confirmado. Em processo comum, devolve o erro 15703.
  - **Sem atalho:** um processo comum sem AUMID próprio aparece com o **nome do `.exe`** (a fonte de teste apareceu como `fonte.exe`).
- Fonte de teste: um console com `Windows.Media.Playback.MediaPlayer` (`CommandManager.IsEnabled = false`), botões ligados, `PlaybackStatus` e `DisplayUpdater`. Ela recebeu `Next` e `Pause` pelo `TrySkipNextAsync` e pelo `TryTogglePlayPauseAsync`, e o título mudou.

## 4. Decisões

### 4.1 Núcleo (7a)

| # | Decisão |
|---|---|
| D1 | **`Audio\MediaMonitor.cs`**, criado e solto pelo `AudioHub` junto com os outros monitores (Fase 6, D1). Com o app parado, nada de mídia é observado.<br>No `Create`, chama `RequestAsync()` sem bloquear. Quando a resposta chega, só a usa se o hub continua ativo **na mesma geração**: um contador incrementado a cada `Create` e `Destroy`. Senão, descarta.<br>Assina `SessionsChanged` e, em cada sessão, `PlaybackInfoChanged` e `MediaPropertiesChanged`, guardando as referências das sessões.<br>Os eventos chegam em threads quaisquer. Os handlers só enfileiram uma releitura na thread da tela, coalescida em 100 ms.<br>No `Destroy`, cancela todas as assinaturas. Não há `Dispose` nos objetos WinRT. |
| D2 | **Estado de mídia:** `MediaInfo(Aumid, Title, Artist, Status, CanPrev, CanNext, CanPlayPause, Key?)`. `Key` é a linha ligada, ou nulo para linha só de mídia. `CanPlayPause` = toggle, ou play quando está pausado, ou pause quando está tocando. O hub expõe `Media` (lista) e o evento `MediaChanged`. Título e artista vêm com `Trim`; título vazio vira o nome do app. |
| D3 | **Ligar a mídia à linha**, com as regras puras em `Audio\MediaMatcher.cs` e testes unitários, nesta ordem:<br>(1) AUMID igual ao `GetApplicationUserModelId` de algum pid do grupo (Loja);<br>(2) `.exe` resolvido pela pasta de apps igual à chave do grupo;<br>(3) mesmo nome de arquivo do `.exe` resolvido. É o caso do Discord, cujo atalho aponta para outra pasta;<br>(4) AUMID igual ao nome do `.exe` do grupo, sem diferenciar maiúsculas.<br>Uma mídia liga a uma linha só, e uma linha recebe uma mídia só; em empate, fica a que está tocando. |
| D4 | **`Audio\AumidResolver.cs`** (parte impura) com cache por AUMID enquanto o hub está ativo:<br>- `ShellResolve(aumid)` devolve nome amigável e `.exe`, por `SHCreateItemInKnownFolder(FOLDERID_AppsFolder, …, IShellItem2)` e `GetString(PKEY_Link_TargetParsingPath)` e `PKEY_ItemNameDisplay`, na thread da tela (STA);<br>- `ProcessAumid(pid)` usa `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)`, `GetApplicationUserModelId` e `CloseHandle`;<br>- para linha só de mídia de app da Loja, o `.exe` (para o ícone) vem do primeiro processo cujo AUMID bate. A varredura só acontece para AUMID com `!` sem linha, uma vez por AUMID.<br>Falha em qualquer passo deixa o campo nulo, sem erro no log. |
| D5 | **Comandos:** `hub.MediaPrevious(aumid)`, `MediaPlayPause(aumid)` e `MediaNext(aumid)` chamam `TrySkipPreviousAsync`, `TryTogglePlayPauseAsync` e `TrySkipNextAsync`. Sem toggle, usam `TryPlayAsync` ou `TryPauseAsync` conforme o estado. São identificados pelo AUMID, não pela linha. Resultado `false` ou exceção vira uma linha `midia: comando recusado (<acao>)` no log, sem quebrar nada. |
| D6 | **Linha só de mídia (P1):** a `SessionListViewModel` mantém, no fim da lista, linhas com `IsMediaOnly = true` para as mídias sem `Key`. Chave interna `midia:<aumid>`, nome da pasta de apps ou do `.exe` (sem extensão) e ícone do `.exe` quando houver. Sem volume, mudo e saída: esses controles ficam `Collapsed`. Some quando a mídia some ou quando ganha uma linha de som. |

### 4.2 Janelinha (7a)

| # | Decisão |
|---|---|
| D7 | **3ª linha na linha do app (P2)**, visível só com mídia:<br>- botões `MediaPrev`, `MediaPlayPause` e `MediaNext` de 28×24, transparentes como o mudo, com os glifos E892, E768 ou E769 (conforme o estado) e E893;<br>- `MediaTitle` com "Título · Artista", `CharacterEllipsis`, opacidade 0,8.<br>Botão desligado quando o app não oferece a ação. A altura da linha passa de `Height="56"` para `MinHeight="56"`.<br>Nomes de acessibilidade: "Faixa anterior em <app>", "Tocar <app>" ou "Pausar <app>", "Próxima faixa em <app>". |
| D8 | **Sem custo parado:** nada muda com a janela fechada e sem celular. Com a janela aberta, os eventos de mídia só releem a mídia daquele app. Posição e tempo da faixa não são lidos (§1). |

### 4.3 Celular (7b)

| # | Decisão |
|---|---|
| D9 | **Protocolo:** `RemoteApp` ganha `midia` (nulo, ou `{titulo, artista, tocando, ant, pp, prox}`) e `soMidia` (bool). As linhas só de mídia entram em `apps` com `soMidia: true`, `key` `midia:<aumid>`, sem volume e sem rotas. Mudança de faixa ou estado entra no estado coalescido de 50 ms (Fase 6, D8).<br>Comando novo: `{"t":"midia","app":"<key>","acao":"ant"|"pp"|"prox"}`. O servidor acha o AUMID pela linha; key desconhecida é ignorada. |
| D10 | **Página:** no cartão do app, uma fileira com ⏮ ⏯ ⏭, botões de 44 px para o dedo, e "Título · Artista". O cartão só de mídia mostra só isso. `index.html` continua abaixo de 40 KB. |
| D11 | **`celular.cs`:** o comando `enviar` passa a aceitar `midia` **só para apps `MX-teste`** ou linha só de mídia cujo nome comece com `tocar`. O resto continua recusado. |

## 5. Arquivos

**7a**
- Novos: `Audio\MediaMonitor.cs`, `Audio\MediaMatcher.cs`, `Audio\AumidResolver.cs`, `Audio\MediaInfo.cs`, `MixerDeAudio.Tests\MediaMatcherTests.cs`.
- Alterados: `Audio\AudioHub.cs` (cria e solta o monitor, `Media`, `MediaChanged` e comandos), `ViewModels\SessionRowViewModel.cs` (mídia, `IsMediaOnly`, glifos e nomes), `ViewModels\SessionListViewModel.cs` (liga mídia às linhas e mantém as linhas só de mídia), `Flyout\FlyoutWindow.xaml` (3ª linha e `MinHeight`).
- Testes de tela:
  - `tools\testes\tocar.cs` ganha `--midia`, que registra a fonte de mídia com "Faixa N · MX-teste", anota cada botão recebido numa linha `botao: <Botao>` do arquivo `--log <arquivo>` e responde como a sonda;
  - ganha também `--sem-audio`, que não toca nada e fica só no controle de mídia;
  - `lib.ps1` ganha funções `Midia*` (UIA Invoke só em linhas `MX-teste*` ou `tocar*`; qualquer outra é recusada com `throw`);
  - `tools\testes\roteiro-f7.ps1 -Parte a|b`.

**7b**
- Alterados: `Remote\RemoteProtocol.cs`, `Remote\RemoteSession.cs`, `Remote\Web\index.html`, `tools\testes\celular.cs`, `MixerDeAudio.Tests\RemoteProtocolTests.cs`, `roteiro-f7.ps1`.

## 6. Testes

### 6.1 Testes unitários (novos)

- **7a:**
  - `MediaMatcher`: cada regra de D3, a ordem entre elas, empate (fica a que toca), sem linha (vira só de mídia), uma mídia por linha;
  - `MediaInfo`: "Título · Artista", título vazio, `CanPlayPause` nos três casos.
- **7b:** serialização de `midia` e `soMidia`; leitura do comando `midia` (ação válida, inválida e key ausente).

### 6.2 Regras do roteiro (P3)

- **Nunca** apertar nem chamar `Try*` em sessão real. O roteiro só invoca botões, por UIA Invoke e sem cliques, em linhas `MX-teste*` ou `tocar*`.
- Sem teclas, sem trazer janela para a frente e com `--abrir-sem-foco`, como nas fases anteriores.
- Testes rodam com `--teste-oculto`: nenhuma janela nem ícone aparece; o app do usuário é reaberto ao fim.
- Roteiro só do que a fase fez; roteiros anteriores não são rodados de novo.
- Use `BackupConfig` e `RestoreConfig`. O servidor só roda em `127.0.0.1` (`--celular-local`).
- Linhas só de mídia de apps reais (ex.: Spotify) podem aparecer na lista. O roteiro ignora essas linhas.

### 6.3 Roteiro da 7a (`roteiro-f7.ps1 -Parte a`)

Só o que depende da janela; o resto está nos testes unitários.

| Caso | Passos | Resultado esperado |
|---|---|---|
| 7.1 Linha com mídia | `tocar --nome "MX-teste A" --midia`; abrir a janela sem foco. | A linha MX-teste A tem `MediaTitle` "Faixa 1 · MX-teste" e os 3 botões ligados. |
| 7.2 Botões | Invoke em `MediaNext` e depois em `MediaPlayPause`. | O log da fonte tem `Next` e depois `Pause`. Em até 1 s, `MediaTitle` passa a "Faixa 2 · MX-teste" e o nome do botão passa a "Tocar MX-teste A". |
| 7.3 Só de mídia | `tocar-b --midia --sem-audio`. | Aparece no fim da lista uma linha "tocar-b" sem `SessionVolume`, `SessionMute` e botões de saída, com a faixa. Invoke em `MediaNext` faz `Next` chegar ao log. Ao encerrar a fonte, a linha some em até 2 s. |
| 7.4 Sem regressão | Com a fonte de 7.1 ligada e a janela aberta, mudar o volume (40) e o mudo da linha MX-teste A por UIA (`SetRowVolume`, `ToggleRowMute`). | Volume gravado na sessão e mostrado na linha; mudo liga e desliga; o `MediaTitle` continua na linha. Sem rodar roteiros de fases anteriores e sem medição (a conferência de memória fica com o Opus). |

### 6.4 Roteiro da 7b (`roteiro-f7.ps1 -Parte b`)

| Caso | Passos | Resultado esperado |
|---|---|---|
| 7.5 Estado | Com a fonte de 7.1 e a de 7.3, `celular estado`. | MX-teste A com `midia.titulo` "Faixa 1"; "tocar-b" com `soMidia: true`. |
| 7.6 Comando | `celular enviar {"t":"midia","app":<key do MX-teste A>,"acao":"prox"}`. | `Next` no log da fonte. O título novo chega ao celular de teste em até 1 s. |
| 7.7 Recusa | `celular enviar` de `midia` para um app que não é de teste. | Recusado pelo próprio `celular.cs`, sem enviar nada. |

A página no navegador, em tamanho de celular, é conferida pelo Opus.

### 6.5 Teste do usuário (na entrega)

1. Toque um vídeo ou música no Opera e abra a janelinha. A linha do Opera mostra o título e os botões. Pause, dê play e, numa playlist do YouTube, use "próxima".
2. Pause o Spotify e espere alguns segundos. Ele aparece como linha só de mídia no fim da lista, e o play pela janelinha volta a tocar. Use anterior e próxima.
3. Faça o mesmo pelo celular.
4. Feche a janelinha e confira que o resto continua igual: volume, mudo e saídas.

## 7. Resultados

### Parte 7a

- **Build:** 0 erros e 0 avisos (Release x64); publicado em `publicado\` depois de `--sair`.
- **Testes unitários:** 328 aprovados, 0 falhas (28 novos de mídia em `MediaMatcherTests.cs` e 2 de `--teste-oculto` em `CommandLineTests.cs`).
- **7.1 Linha com mídia:** OK. `MediaTitle` "Faixa 1 · MX-teste", 3 botões ligados, nomes de acessibilidade conforme D7.
- **7.2 Botões:** OK. Log da fonte `Next`, `Pause`; título passou a "Faixa 2" e o botão a "Tocar MX-teste A" em menos de 1 s.
- **7.3 Só de mídia:** OK. Linha "tocar-b" no fim da lista, sem `SessionVolume`, `SessionMute` e botões de saída; `Next` chegou ao log; a linha sumiu ao encerrar a fonte. Spotify real aparece como linha só de mídia (lida, nunca acionada).
- **7.4 Sem regressão:** OK (volume 40, mudo liga e desliga, `MediaTitle` mantido). Padrão, volume e mudo gerais e apps reais iguais antes e depois. Config e registro restaurados.
- **Tempo total do roteiro (`roteiro-f7.ps1 -Parte a`):** 12,7 s.
- **Medição comparada à Fase 6:** não feita, por ordem do usuário (a conferência de memória fica com o Opus). Fase 6 para referência: abertura máxima 154 ms; bytes privados 20 a 50 de -1,0 MB.
- **Modo `--teste-oculto`:** implementado. Sem ícone na bandeja; a janela (e a de configurações) abre em x = menor x dos monitores - 4000, y = abaixo do último monitor + 4000, sem ativação e no tamanho normal; o app grava `janela: posicao calculada x,y,l,a`. Funcionou com a janela fora das telas, sem precisar do cloak do DWM: UIA (Invoke, Toggle, RangeValue) e `PrintWindow` (capturada a janela inteira) funcionam. `lib.ps1` (`StartApp`) sempre passa o argumento; `ReabrirAppDoUsuario` (chamada no fim do `roteiro-f7`) reabre o app do usuário, sem argumentos, se ele estava rodando (testado).
- **Ajuste da conferência:** `StrategyBasedComWrappers` estático no `AumidResolver`; resolvedor único no hub (campo `readonly`, vive o app inteiro), com `_shell` e `_storeExe` mantidos entre aberturas (.exe conferido com `File.Exists`, senão refaz a consulta) e `Destroy` limpando só `_byPid`. Build 0 erros e 0 avisos; 328 testes aprovados; `roteiro-f7 -Parte a` passou (13,8 s); `medir.ps1 -ComSom -Ciclos 20`: abertura máxima 594 ms (a primeira; as demais até 254 ms, pelo log), bytes privados do ciclo 5 ao 20: 134 para 142,1 MB (+8,1 MB; na rodada anterior 132,9 para 139,5), conjunto de trabalho privado +0,3 MB.

**Desvios**
1. **Sem `Dispose` de objetos WinRT e geração do hub:** a "mesma geração" da D1 é a própria instância do `MediaMonitor` (uma nova a cada `Create`, com flag de descarte); equivale ao contador.
2. **`tocar.cs --sem-audio` não usa `MediaPlayer`:** o `MediaPlayer` cria uma sessão de áudio inativa, e o app a mostrava como linha de som comum. Em `--sem-audio` o controle de mídia vem de uma janela escondida (`SystemMediaTransportControlsInterop.GetForWindow`), sem sessão de áudio.
3. **D4:** `SHCreateItemInKnownFolder` foi usado como na análise, com `IShellItem2` como `GeneratedComInterface` (`AumidResolver.cs`).
4. **Roteiros f1 a f6 não foram adaptados nem rodados de novo (nova regra do usuário).** Casos antigos que ficam incompatíveis com o modo oculto: o 1.2 (posição pelo `GetWindowRect`) falha, pois a janela fica fora das telas; o 5.1 usa `IsOffscreen` (sempre verdadeiro fora das telas) e os 5.4/5.5 comparam brilho da Mica numa janela fora das telas. Antes da regra nova, `roteiro-f1.ps1` foi adaptado para ler a linha de posição do log e passou; essa adaptação (e a do 5.1) ficou nos arquivos, mas só o f1 foi rodado. O f6 e o f5, com `StartApp` oculto, tiveram 6.1 a 6.6 OK, 6.7 com `roteiro-f5` falhando (8 OK, código 1) numa execução interrompida; a causa não foi investigada.
5. **Perda da configuração do usuário:** numa execução da cadeia f7, f6 e f5 interrompida à força, o `config.json` do usuário (celular ligado, código e chaves de pareamento) foi perdido: o `BackupConfig` aninhado do f6 apagou a cópia `.bak` feita pelo f7, e o processo foi morto antes do `finally`. Corrigido em `lib.ps1` (o `BackupConfig` nunca apaga um `.bak` existente). O usuário precisa religar o "Controle pelo celular" e parear de novo.

### Parte 7b

- **Build:** 0 erros e 0 avisos (Release x64); publicado em `publicado\` depois de `--sair`.
- **Testes unitários:** 337 aprovados, 0 falhas (9 novos em `RemoteProtocolTests.cs`: serialização de `midia` e `soMidia`, comando `midia` com 3 ações válidas e 5 entradas inválidas).
- **7.5 Estado:** OK. MX-teste A com `midia.titulo` "Faixa 1", artista "MX-teste", 3 botões e `soMidia: false`; "tocar-b" com `soMidia: true`, chave `midia:tocar-b.exe`, sem rotas, no fim da lista.
- **7.6 Comando:** OK. `prox` no MX-teste A gerou `Next` no log da fonte e o título "Faixa 2" chegou ao celular de teste em 388 ms (medido desde antes do envio). Extra: `prox` na linha só de mídia "tocar-b" também chegou (`Next` no log).
- **7.7 Recusa:** OK. `midia` para um app real (Discord) e para chave inexistente: recusados pelo `celular.cs` (código 3), sem enviar nada; controle: `pp` no MX-teste A aceito (`Pause` no log, só 1 botão novo).
- **Página (7.8, extra):** `index.html` com 12.936 bytes (< 40 KB), sem URL externa, com a fileira ⏮ ⏯ ⏭ de 44 px e o texto "Título · Artista". Conferência visual em tamanho de celular fica com o Opus.
- **Tempo total do roteiro (`roteiro-f7.ps1 -Parte b`):** 9,7 s (uma execução; config e registro restaurados; 0 erros no log; apps reais iguais antes e depois).
- **Desvios:** (1) `RemoteApp` ganhou `Media` e `MediaOnly` como parâmetros opcionais no fim, para não quebrar os usos existentes; (2) `RemoteServer.ServeIconAsync` também resolve o ícone das linhas só de mídia (`midia:<aumid>`), não previsto no documento; (3) `celular.cs` aceita `midia` também para linha só de mídia (`soMidia`) cujo nome começa com `tocar`, como a D11 pede, e continua recusando o resto; (4) o `--sair` antes de publicar fechou o app do usuário e o `ReabrirAppDoUsuario` do roteiro não o reabriu (ele só reabre se estava aberto ao iniciar o roteiro); reabri à mão ao fim.

**Conferência (Opus, 21/11/2026):** 7a: o `AumidResolver` passou a ter um só `StrategyBasedComWrappers` e a viver o app inteiro, para não repetir a consulta à pasta de apps nem a varredura dos processos a cada abertura. A abertura ficou igual com e sem mídia (mediana de 161 e 189 ms). O `BackupConfig` passou a guardar cópias com data e hora, porque uma execução da cadeia apagou o `config.json` do usuário. 7b: página aberta no navegador embutido em 375×812. O MX-teste A mostra "Faixa 1 · MX-teste" e "próxima" pela página chegou à fonte (`botao: Next`, "Faixa 2"). A linha só de mídia "tocar-b" e o Spotify pausado aparecem no fim. O Opera mostra anterior e próxima desligados, como o site informa. 0 erros no log; o app do usuário foi reaberto. Observação: o ícone do Spotify (Loja) sai genérico.
