# Fase 2 — Apps com volume e mudo

**Status:** análise aprovada pelo usuário em 19/11/2026 (P1 e P2 conforme a recomendação: mostrar); implementada em 19/11/2026; conferida pelo Opus em 19/11/2026; teste do usuário (6.4) aprovado em 19/11/2026
**Implementação:** Sonnet (`medium`) · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também `docs/PLANO.md` (decisões A1 a A8) e `docs/fases/fase-1.md` (seções 4 e 7: como a janela, a linha de comando, o log e a devolução de memória funcionam). Pasta do repositório: `C:\Projetos\Mixer de Audio` (branch `main`, último commit `c237199`; **não commitar**).

## 1. Objetivo

A janelinha passa a listar os apps com som, cada um numa linha com ícone, nome, botão de mudo, slider de volume e o valor em %. Mexer no slider ou no mudo muda o som do app na hora. Uma mudança feita fora do app (Mixer de Volume do Windows, o próprio app) aparece na janela na hora. Tudo é feito por eventos do Windows, sem timer. Com a janela fechada, o app continua sem nenhum objeto de áudio (a leveza da Fase 1 não muda).

Fora desta fase: volume geral e troca da saída padrão (Fase 3), botões de dispositivo por app (Fase 4), medidor de nível (VU, fica desligado; avaliar na Fase 6), altura variável da janela e roda do mouse no slider (Fase 6).

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Apps parados.** Um app com o som pausado (ex.: vídeo do YouTube pausado) tem uma sessão "inativa". Mostrar ou esconder? | Resolvida (19/11/2026): **mostrar** (como o Mixer de Volume do Windows): assim dá para ajustar o volume antes de dar play, e as linhas não somem e voltam a cada pausa. A linha some quando o app fecha. |
| P2 | **"Sons do sistema"** (avisos do Windows). Mostrar? | Resolvida (19/11/2026): **mostrar**, sempre como a última linha. |

## 3. Contexto do código atual

- `App.xaml.cs` cria a `FlyoutWindow` a cada abertura (`Open`) e recebe o fechamento em `OnFlyoutClosed`. A devolução de memória (`MemoryTrimmer`) roda 2 s depois de fechar.
- `Flyout\FlyoutWindow.xaml` tem só o `Root` (`AutomationId FlyoutRoot`) com o título e um texto provisório. O tamanho é fixo em 360 × 480 DIP.
- O projeto de testes (`MixerDeAudio.Tests`) compila por link os arquivos de lógica pura (`<Compile Include="..\MixerDeAudio\...">`).
- Auxiliares de teste em `tools\testes\lib.ps1` (`StartApp`, `Cmd`, `MainProc`, `Windows`, `WaitFor`, `StopAll`, `LogLines`, `LogCount`, `FindById`, `Shot`) e `tools\testes\medir.ps1`.
- `docs\ESTADO.md` ainda diz "aguardando conferência" na Fase 1. Corrija para "concluída".

## 4. Decisões

### 4.1 Quando o áudio é observado

| # | Decisão |
|---|---|
| D1 | **Só com a janela aberta.** Ao abrir, o monitor de áudio é criado, lista as sessões e se inscreve nos eventos. Ao fechar, tudo recebe `Dispose`, antes da devolução de memória. Em repouso não sobra nenhum objeto COM de áudio nem nenhuma inscrição. A Fase 4 não precisa de nada ligado em repouso, porque o próprio Windows guarda a saída escolhida por app. |
| D2 | **Todas as saídas ativas** (`EnumerateAudioEndPoints(Render, Active)`), não só a padrão. Assim um app que a Fase 4 mandar para outra saída continua na lista. A lista de saídas é lida ao abrir. Saídas ligadas ou desligadas com a janela aberta ficam para a Fase 3, que traz o `IMMNotificationClient`. |

### 4.2 Biblioteca e threads

| # | Decisão |
|---|---|
| D3 | Pacote **`NAudio.Wasapi`** (2.2.1 ou a última estável; registre a versão na seção 7), não o `NAudio` completo: só o que a fase usa (`MMDeviceEnumerator`, `AudioSessionManager`, `AudioSessionControl`, `SimpleAudioVolume`, `IAudioSessionEventsHandler`). |
| D4 | **Toda chamada COM acontece na thread da tela.** Os eventos do Windows chegam em threads de fundo. O tratador só copia os valores recebidos e enfileira na `DispatcherQueue`. Exceção: em `OnSessionCreated`, o objeto `AudioSessionControl` pode ser criado ali, antes de enfileirar. |
| D5 | **Nunca cancelar inscrição nem dar `Dispose` dentro de um tratador de evento**, porque o Windows trava (documentado pela Microsoft). A remoção de uma sessão expirada é enfileirada na thread da tela. |
| D6 | Depois do `Dispose` do monitor, um evento atrasado que ainda chegue é ignorado (sinalizador `_disposed`, verificado na thread da tela). |
| D7 | Para a notificação de sessão nova funcionar, a lista de sessões precisa ser lida antes de se inscrever (exigência do Windows; o `AudioSessionManager` do NAudio faz isso no `RefreshSessions`). Confira no código do NAudio e registre na seção 7. |

### 4.3 Modelo (lógica pura, sem NAudio e sem WinUI, entra nos testes)

| # | Decisão |
|---|---|
| D8 | `Audio\SessionTracker.cs` recebe eventos simples: `Add(SessionInfo)`, `UpdateVolume(id, volume, mudo)`, `UpdateState(id, estado)`, `UpdateName(id, nome)`, `Remove(id)`. Mantém os **grupos** (uma linha por app) e avisa a camada de cima o que mudou: grupo novo, grupo alterado ou grupo removido. `SessionInfo`: id da sessão (`GetSessionInstanceIdentifier`), id do processo, caminho do `.exe`, nome, volume, mudo, estado, se é "Sons do sistema". |
| D9 | **Agrupamento:** chave = caminho do `.exe` em minúsculas. "Sons do sistema" tem uma chave fixa. Sem caminho (acesso negado ou processo 0), a chave é o próprio id da sessão. Duas cópias do mesmo programa ficam numa linha só, como no Mixer do Windows 11. |
| D10 | **Volume e mudo do grupo:** o volume mostrado é o da sessão mais antiga do grupo. Mexer no slider aplica o valor a todas as sessões do grupo. O grupo aparece mudo só se todas as sessões estiverem mudas, e o botão aplica o mudo (ou o tira) de todas. |
| D11 | **Estados:** sessões ativas e inativas aparecem (P1). Uma sessão expirada ou desconectada sai do grupo, e um grupo sem sessões sai da lista. |
| D12 | **Ordem:** alfabética pelo nome, sem diferenciar maiúsculas (`StringComparer.CurrentCultureIgnoreCase`), com "Sons do sistema" sempre por último (P2). A ordem é estável: uma linha só muda de lugar se o nome mudar. |
| D13 | **Nome** (`Audio\AppNameResolver.cs`): "Sons do sistema" para a sessão do sistema. Senão, nesta ordem: o `DisplayName` da sessão, se não vier vazio (começando com `@`, é um texto de recurso que se resolve com `SHLoadIndirectString`; se falhar, ignore); a descrição do `.exe` (`FileVersionInfo.FileDescription`); o nome do processo; e por fim "App desconhecido". A parte pura (escolher entre os candidatos já obtidos) é testada. A leitura do `.exe` fica fora do modelo. |
| D14 | **Eco do próprio ajuste** (`Audio\VolumeEchoFilter.cs`): o NAudio não repassa o contexto do evento, então o app também recebe de volta os valores que ele mesmo gravou. Num arraste rápido, um eco atrasado (50 chegando depois de 52) faria o slider pular para trás. Regra: guardar por grupo os últimos 8 valores gravados no último 1 s. Um evento de volume com valor a até 0,005 de um deles é ignorado. Qualquer outro valor é mudança de fora e atualiza a linha. O relógio é injetável. |
| D15 | **Ícone do volume** (`Audio\VolumeGlyph.cs`): mudo ou volume 0 mostra o alto-falante cortado (`E74F`). Volume até 33% usa `E993`, até 66% `E994` e acima disso `E995`, da fonte Segoe Fluent Icons. |

### 4.4 Camada do Windows

| # | Decisão |
|---|---|
| D16 | `Audio\AudioSessionMonitor.cs` (NAudio → `SessionTracker`) cria o `MMDeviceEnumerator`, abre cada saída ativa e o `AudioSessionManager` dela, lê as sessões e registra um tratador por sessão (`RegisterEventClient`) e o `OnSessionCreated`. Expõe `SetVolume(chave, valor)` e `SetMute(chave, mudo)`, que chamam o filtro D14 antes de gravar. `Dispose` faz, na ordem: desinscrever os tratadores de sessão e o `OnSessionCreated`, e depois dar `Dispose` nas sessões, nos gerenciadores, nas saídas e no enumerador. Erro COM ao gravar (ex.: sessão que acabou de expirar) é registrado no log e ignorado, sem derrubar o app. |
| D17 | `Audio\ProcessInfo.cs`: caminho do `.exe` com `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` e `QueryFullProcessImageName` (funciona também para processos com mais privilégio), e a descrição com `FileVersionInfo`. Cache por id do processo enquanto a janela estiver aberta. |
| D18 | `Audio\AppIconCache.cs`: ícone com `System.Drawing.Icon.ExtractAssociatedIcon(caminho)`, convertido em PNG (32 px). Os bytes do PNG ficam num cache **estático**, por caminho, que sobrevive ao fechar a janela, com no máximo 64 itens (o mais antigo sai). Cada abertura cria um `BitmapImage` novo a partir dos bytes, nunca guarda objetos XAML no cache. A extração roda fora da thread da tela (`Task.Run`) para não atrasar a abertura. A linha aparece primeiro com um ícone genérico (glifo `E7F4`), que é trocado quando o ícone fica pronto. Sem caminho ou com erro, o genérico fica. Sons do sistema usam o glifo `E7F5`. |

### 4.5 Tela

| # | Decisão |
|---|---|
| D19 | `ViewModels\ObservableObject.cs` (base mínima com `INotifyPropertyChanged`, sem pacote MVVM), `ViewModels\SessionListViewModel.cs` (`ObservableCollection` ordenada pelo D12, alimentada pelos avisos do `SessionTracker`) e `ViewModels\SessionRowViewModel.cs` (nome, ícone, volume 0 a 100, mudo, glifo, texto do valor). Inserir, remover ou mover na posição certa, sem recriar a lista inteira. |
| D20 | **Linha** (cerca de 56 DIP): ícone 24 DIP, nome numa linha com reticências, e embaixo o botão de mudo (glifo D15), o slider (0 a 100, passo 1, sem tooltip) e o valor ("75", 3 caracteres de largura fixa). Slider → `SetVolume(valor / 100)`. Mudo → `SetMute`. Atualizar a linha a partir de um evento de fora não pode disparar uma nova gravação (sinalizador de "atualizando pelo sistema" no VM). |
| D21 | **Janela:** título "Mixer de Áudio" em cima. Embaixo dele, a lista num `ScrollViewer` vertical, com o tamanho fixo de 360 × 480 DIP mantido. Lista vazia: o texto "Nenhum app com som agora." O texto provisório da Fase 1 sai. |
| D22 | **Acessibilidade e testes:** lista `AutomationId SessionList`. Cada linha tem `AutomationId SessionRow` e `AutomationProperties.Name` igual ao nome do app. Dentro da linha, `SessionVolume` (Slider, `RangeValuePattern`), `SessionMute` (ToggleButton, `TogglePattern`, `Name` "Silenciar <app>" ou "Ativar som de <app>") e `SessionValue`. Texto vazio: `EmptyText`. |
| D23 | **Log:** `sessoes carregadas (apps=<n>, sessoes=<m>, <ms> ms)` ao abrir, medindo do início do `Open` até a lista pronta, sem contar os ícones. Erros de COM entram no log. Nada é registrado por evento de volume, porque o arraste geraria centenas de linhas. |

## 5. Arquivos

**Novos:** `MixerDeAudio\Audio\SessionInfo.cs`, `SessionTracker.cs`, `VolumeEchoFilter.cs`, `AppNameResolver.cs`, `VolumeGlyph.cs`, `AudioSessionMonitor.cs`, `ProcessInfo.cs`, `AppIconCache.cs`; `MixerDeAudio\ViewModels\ObservableObject.cs`, `SessionListViewModel.cs`, `SessionRowViewModel.cs`; testes `SessionTrackerTests`, `VolumeEchoFilterTests`, `AppNameResolverTests`, `VolumeGlyphTests`; `tools\testes\tocar.cs`, `tools\testes\sessoes.cs`, `tools\testes\roteiro-f2.ps1`.
**Alterados:** `MixerDeAudio.csproj` (`NAudio.Wasapi`); `Flyout\FlyoutWindow.xaml(.cs)` (lista; criar o monitor ao abrir e dar `Dispose` no `Closed`); `Interop\NativeMethods.cs` (`OpenProcess`, `QueryFullProcessImageName`, `CloseHandle`, `SHLoadIndirectString`); `MixerDeAudio.Tests.csproj` (links novos: `SessionInfo`, `SessionTracker`, `VolumeEchoFilter`, `AppNameResolver`, `VolumeGlyph`); `tools\testes\lib.ps1` (funções de sessão, 6.2), `tools\testes\medir.ps1` (`-ComSom`), `tools\testes\README.md`; `docs\ESTADO.md`, `docs\PLANO.md`, este documento (seção 7).

## 6. Testes

**Regra geral:** como na Fase 1. Build sem erros e sem avisos, a bateria inteira de testes unitários passando e os roteiros no build publicado em `publicado\`.

**O usuário pode estar jogando.** Os roteiros não enviam teclas nem cliques e não trazem janelas para a frente, como na Fase 1. Além disso:
- **Só mexer no volume dos tocadores de teste.** Os apps reais do usuário podem aparecer na lista: o roteiro só lê essas linhas e nunca muda o volume nem o mudo delas, nem pela janela nem pelo `sessoes.cs`.
- **Os tocadores tocam silêncio** (amostras zeradas): a sessão fica ativa sem fazer barulho.

### 6.1 Testes unitários (novos)

| Arquivo | Casos |
|---|---|
| `SessionTrackerTests` | sessão nova cria um grupo; duas sessões do mesmo `.exe` ficam num grupo só; `.exe` diferentes ficam em grupos separados; sem caminho, a chave é o id da sessão; "Sons do sistema" fica sempre por último; ordem alfabética sem diferenciar maiúsculas; o volume do grupo vem da sessão mais antiga; mudo do grupo só com todas mudas; sessão inativa continua; expirada e desconectada saem; o grupo some quando sai a última sessão; mudar o nome reposiciona; id repetido em `Add` não duplica; evento de sessão desconhecida é ignorado. |
| `VolumeEchoFilterTests` | eco exato é ignorado; eco a até 0,005 é ignorado; eco atrasado de um valor anterior do arraste é ignorado; valor diferente de todos passa; depois de 1 s o eco passa (vira mudança de fora); só os últimos 8 valores contam; grupos não se misturam. |
| `AppNameResolverTests` | sistema dá "Sons do sistema"; `DisplayName` preenchido vence; `DisplayName` vazio ou em branco cai para a descrição; texto `@` resolvido é usado; `@` que não resolveu cai para o próximo; sem descrição, usa o nome do processo; sem nada, "App desconhecido". |
| `VolumeGlyphTests` | mudo; volume 0; 1; 33; 34; 66; 67; 100. |

### 6.2 Ferramentas de teste

- `tools\testes\tocar.cs` (app de arquivo único, `dotnet run tocar.cs -- --nome "MX-teste A" --segundos 120`, com `#:package NAudio.Wasapi`): toca silêncio na saída padrão (`WasapiOut`, modo compartilhado) e define o `DisplayName` da própria sessão com o nome recebido. Imprime o id do processo e espera até acabar o tempo ou o processo ser encerrado.
- `tools\testes\sessoes.cs`:
  - `listar` mostra uma linha por sessão de todas as saídas ativas (`pid|nome|estado|volume|mudo`).
  - `definir <pid> <volume> [mudo|som]` muda a sessão daquele processo de fora do app, para testar os eventos, e **recusa** processos que não sejam tocadores de teste (só aceita quando o `DisplayName` começa com `MX-teste`).
- Funções novas em `lib.ps1`: `StartPlayer`, `StopPlayers` (encerra só os tocadores iniciados pelo roteiro), `Rows` (nomes das `SessionRow` pela UI Automation), `RowVolume`, `SetRowVolume` (`RangeValuePattern.SetValue`, sem clique), `ToggleRowMute` (`TogglePattern`), `SessionOf <pid>` (pelo `sessoes.cs`).
- Para um segundo app com outro `.exe`, o roteiro copia `tocar.cs` para `%TEMP%\mixer-check\tocar-b.cs` e roda essa cópia, que vira um `.exe` diferente.

### 6.3 Roteiro na tela (`tools\testes\roteiro-f2.ps1`, Sonnet roda, Opus confere)

Os casos abaixo dependem da janela real e do áudio do Windows. O resto da lógica está nos unitários.

| Caso | Por que na tela | Passos | Esperado |
|---|---|---|---|
| 2.1 Lista ao abrir | Leitura real das sessões e montagem das linhas. | Iniciar o tocador "MX-teste A" e o app; `--abrir-sem-foco`. | Uma linha `MX-teste A` com o volume igual ao do `sessoes.cs`; log `sessoes carregadas` com o tempo (registrar; esperado abaixo de 300 ms). |
| 2.2 Slider muda o app | Caminho tela → Windows. | `SetRowVolume "MX-teste A" 30`. | `sessoes.cs` mostra 0,30 (±0,01) para o tocador. Os outros apps continuam com o mesmo volume de antes (comparar com o `listar` do início). |
| 2.3 Mudo | Caminho tela → Windows e glifo. | `ToggleRowMute` duas vezes. | Mudo `sim` e depois `não` no `sessoes.cs`; o `Name` do botão alterna. |
| 2.4 Mudança de fora | Caminho Windows → tela (eventos). | `sessoes.cs definir <pid A> 0,8`. | Em até 1 s a linha mostra 80, sem gravar de volta (o `sessoes.cs` continua em 0,80). |
| 2.5 App novo e app que fecha | `OnSessionCreated` e expiração com a janela aberta. | Com a janela aberta, iniciar "MX-teste B" (`tocar-b.cs`); depois encerrar o processo dele. | A linha B aparece em até 2 s, na ordem alfabética. Ela some depois que o processo fecha (até 10 s, porque o Windows demora para expirar a sessão). |
| 2.6 Mesmo `.exe`, uma linha | Agrupamento com sessões reais. | Dois tocadores `tocar.cs` ao mesmo tempo; abrir; `SetRowVolume` 50. | Uma linha só; os dois processos com 0,50 no `sessoes.cs`. |
| 2.7 Fechar libera tudo | D1 e D5 com objetos COM reais. | `--fechar` com dois tocadores tocando; depois `definir` num deles. | Nenhuma exceção ou erro no log; o processo continua vivo; nenhum `sessoes carregadas` novo até reabrir. |
| 2.8 Sem regressão | A janela da Fase 1 continua igual. | Rodar `roteiro-f1.ps1`. | 1.1 a 1.5 OK. |
| 2.9 Medição | Vazamento de objetos COM a cada abertura. | `medir.ps1 -ComSom`: como a Fase 1, mas com dois tocadores tocando durante os 20 ciclos. | CPU parada com a janela fechada < 0,1%; crescimento do ciclo 5 ao 20 < 5 MB (WS privado **e** bytes privados); registrar a tabela. |

Ao terminar: encerrar o app e todos os tocadores, inclusive em caso de erro. Conferir que nenhum `tocar`/`tocar-b` ficou aberto.

### 6.4 Teste do usuário (na entrega)

Com um jogo ou vídeo tocando e o YouTube num navegador:
1. Abrir pelo ícone: os dois apps aparecem com ícone e nome certos.
2. Arrastar o slider de um deles: o som muda suavemente, sem pular para trás.
3. Silenciar e tirar o silêncio pelo botão.
4. Mudar o volume pelo Mixer de Volume do Windows com a janela aberta: o slider acompanha.
5. Pausar o vídeo: a linha continua (P1). Fechar o navegador: a linha some.

## 7. Resultados

Implementado em 19/11/2026 (Sonnet). Nada commitado.

**Versão do NAudio.Wasapi:** 3.3.0 (a última estável no NuGet; só existe em `net9.0` e funciona no `net10.0`). Também entrou `System.Drawing.Common` 10.0.0 (para `Icon.ExtractAssociatedIcon`, que não vem no WinUI sem o pacote).

**Conferência do D7:** o código-fonte do NAudio não está no pacote; o `AudioSessionManager` é usado lendo `Sessions` antes de assinar `OnSessionCreated`, como o D7 pede, e o caso 2.5 confirma que o aviso de sessão nova chega com a janela aberta.

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q` → 0 avisos, 0 erros. Publish Release x64 ReadyToRun: ok.
**Testes unitários:** `dotnet test MixerDeAudio.Tests -v q` → 72 aprovados, 0 falhas, 0 ignorados (30 da Fase 1 + 42 novos: 17 `SessionTrackerTests`, 8 `VolumeEchoFilterTests`, 9 `AppNameResolverTests`, 8 `VolumeGlyphTests`).

**Alternativas e desvios**

| Ponto | O que foi feito |
|---|---|
| D4 `OnSessionCreated` | No NAudio 3.x o evento entrega a sessão nova como `IAudioSessionControl`, que é interna (não dá para criar o `AudioSessionControl` no tratador). O tratador só enfileira na thread da tela, que chama `RefreshSessions()` e adiciona as sessões ainda desconhecidas (`Rescan`). Os demais tratadores seguem o D4 (copiam e enfileiram). |
| D21 ligação da lista | O `x:Bind` no nível da `Window` só é aplicado no `Loading` e a lista ficava vazia na abertura sem foco. `ItemsSource` e a visibilidade do `EmptyText` são ligados pelo código no construtor; o `x:Bind` dentro do `DataTemplate` das linhas funciona normalmente. |
| D14 eco | Um evento de volume que é eco é ignorado por inteiro (inclusive o mudo que viria nele). O `SetVolume` e o `SetMute` já atualizam o `SessionTracker` com o valor gravado, para o grupo não ficar com valor velho. |
| Ferramentas | O `dotnet run` de arquivo único roda pela `dotnet.exe`, então o roteiro publica `tocar`, `tocar-b` (cópia, outro `.exe`) e `sessoes` com `dotnet publish` em `%TEMP%\mixer-check\ferr` (`#:property PublishAot=false`). `lib.ps1` ganhou também `Sessoes`, `DefinirSessao`, `PlayersAbertos`, `RowValueText`, `RowMuteName`; `SetRowVolume` e `ToggleRowMute` recusam linhas que não comecem com `MX-teste`. |
| Roteiro 2.6 | A linha do grupo leva o nome da sessão mais antiga que o NAudio lista, que pode ser `MX-teste A` ou `MX-teste A2`: o teste confere que há uma linha só `MX-teste*`. |

**Tempo de abertura** (log `sessoes carregadas`, do início do `Open` até a lista pronta, sem ícones): 145 ms no roteiro final (7 apps, 12 sessões); 246 a 259 ms nas primeiras aberturas a frio. Meta < 300 ms: ok.

**Caso 2.9: medição** (`medir.ps1 -ComSom`, build publicado; dois tocadores tocando durante a rodada; MB)

| Parado 10 s: WS privado / bytes privados | CPU parada (30 s) | Ciclo 5: WS privado / bytes privados | Ciclo 20: WS privado / bytes privados | Crescimento 5 a 20 (WS privado / bytes privados) |
|---|---|---|---|---|
| 2,8 / 74,7 | 0,003% | 2,5 / 115,8 | 1,4 / 124,8 | -1,1 / +9,0 |

CPU < 0,1% e WS privado sem crescimento: ok. **Bytes privados: +9,0 MB do ciclo 5 ao 20, acima do critério de 5 MB.** Investigação (série de 50 ciclos com dois tocadores, bytes privados): ciclo 5 = 120,3; 10 = 126,0; 20 = 127,3; 30 = 128,2; 40 = 125,8; 50 = 126,2. Ou seja, sobe nos primeiros ciclos (JIT, caches, carga das DLLs do NAudio e do System.Drawing) e fica estável depois, sem vazamento contínuo. Com as rodadas anteriores (os tocadores acabavam antes do fim, por um limite de 120 s que foi corrigido) o crescimento foi de +4,1 e +5,3. Sem tocadores, 30 ciclos: 111,1 → 117,3. A contagem de handles do processo sobe cerca de 4 por abertura, com ou sem tocadores (1180 → 1280 em 25 ciclos sem tocadores): vem do WinUI ao criar a janela, não do áudio; fica para a Fase 6. Os bytes privados parados passaram de ~75 MB para ~115 a 125 MB depois do primeiro uso, por causa do NAudio, do System.Drawing e do WinUI carregados (não ocupam RAM física parado).

**Casos de tela** (`roteiro-f2.ps1`, executado inteiro com sucesso; as medições do 2.9 foram feitas à parte)

| Caso | Resultado |
|---|---|
| 2.1 Lista ao abrir | OK: 7 linhas (Discord, MX-teste A, Opera GX, PowerToys.Peek.UI, Steam, wallpaper64, Sons do sistema) em ordem alfabética com "Sons do sistema" por último; volume da tela = volume do `sessoes` (100); abertura em 145 ms. Captura: `C:\Users\lucas\AppData\Local\Temp\mixer-check\f2-janela.png` (ícones carregados). |
| 2.2 Slider muda o app | OK: `SetRowVolume 30` → 0,30 no `sessoes`; volumes dos outros apps iguais aos do início. |
| 2.3 Mudo | OK: mudo `sim` e depois `não`; `Name` do botão alterna entre "Ativar som de MX-teste A" e "Silenciar MX-teste A". |
| 2.4 Mudança de fora | OK: a linha mostrou 80 em 0,11 s; o `sessoes` ficou em 0,80 (sem gravar de volta). |
| 2.5 App novo e que fecha | OK: linha B apareceu logo depois do início do tocador, logo após a A; sumiu imediatamente ao encerrar o processo (limite 10 s). |
| 2.6 Mesmo .exe, uma linha | OK: uma linha `MX-teste*`; os dois processos com 0,50. |
| 2.7 Fechar libera tudo | OK: `definir` nos dois tocadores com rc 0, processo vivo, 0 erros no log, nenhuma leitura nova. |
| 2.8 Sem regressão | OK: `roteiro-f1.ps1` com 1.1, 1.2, 1.3 e 1.5 aprovados. |
| 2.9 Medição | CPU e WS privado ok; bytes privados acima de 5 MB entre os ciclos 5 e 20, mas estáveis depois (tabela e série acima). Para o Opus decidir. |

Nenhum caso foi pulado. Ao final nenhum processo `MixerDeAudio`, `tocar` ou `tocar-b` ficou aberto. Ficam para o teste do usuário (6.4): ícones e nomes dos apps reais, arrasto suave do slider (sem pular para trás), mudança pelo Mixer de Volume do Windows com a janela aberta, linha de vídeo pausado e fechar o navegador.

### Conferência (Opus, 19/11/2026)

- Build sem avisos e 72 testes unitários aprovados (rodados de novo na conferência). Nenhum processo `MixerDeAudio` ou `tocar` ficou aberto.
- Código revisado nos pontos de risco do `AudioSessionMonitor`:
  - os tratadores só enfileiram na thread da tela;
  - não há cancelamento de inscrição nem `Dispose` dentro de um evento;
  - o sinalizador `_disposed` é verificado antes e depois de enfileirar;
  - o `Dispose` segue a ordem do D16;
  - a sessão que não entra na lista recebe `Dispose` no `finally`.
- Risco do desvio do D4 (o `Rescan` relê todas as sessões e descarta as repetidas): ele poderia estragar o objeto das sessões que já estavam na lista. **Verificado com um teste à parte:** depois que a B apareceu, a A continuou recebendo o volume gravado (0,40) e o evento de fora (70), sem nenhum erro no log.
- **Bytes privados +9 MB do ciclo 5 ao 20:** aceito. Na série de 50 ciclos eles se estabilizam (128,2 MB no ciclo 30 e 126,2 MB no 50), ou seja, são o aquecimento do NAudio, do System.Drawing e do WinUI, não um vazamento. O critério "5 → 20" passa a ser lido junto com a série longa.
- **Handles (cerca de +4 por abertura, vindos do WinUI):** pendência para a Fase 6. Lá vai ser medido se param de crescer, e, se não pararem, comparado com reaproveitar uma única janela escondida.
