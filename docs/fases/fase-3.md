# Fase 3 — Volume geral e troca da saída padrão

**Status:** análise aprovada (P1 a P3 como recomendado), implementada, conferida e teste do usuário (6.4) aprovado em 19/11/2026
**Implementação:** Sonnet (`medium`) · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também `docs/PLANO.md` e `docs/fases/fase-2.md`, seções 4 e 7, que explicam como o áudio é observado, as threads, o filtro de eco, o monitor de sessões e as ferramentas de teste. Pasta do repositório: `C:\Projetos\Mixer de Audio` (branch `main`, último commit `4f92460`). **Não commitar.**

## 1. Objetivo

O topo da janelinha ganha o controle geral:
- uma fileira de botões, um para cada saída de áudio, com ícone e nome; a saída padrão fica destacada, e um clique em outro botão a torna a saída padrão do Windows;
- abaixo dela, o botão de mudo, o slider do volume geral e o valor em %.

Tudo é feito por eventos (`IMMNotificationClient` para as saídas, notificação do volume da saída), e só com a janela aberta, como na Fase 2. Esta fase também fecha a pendência D2 da Fase 2: uma saída ligada ou desligada com a janela aberta entra ou sai da lista de apps.

Apps com saída escolhida individualmente continuam onde estão, porque é o próprio Windows quem guarda essa escolha (a Fase 4 a cria). Fora desta fase: escolher os favoritos (Fase 5), saída por app (Fase 4) e visual final (Fase 6).

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Quais saídas aparecem até a Fase 5** (que traz os 2 ou 3 favoritos)? | **Todas as saídas ativas.** Se forem mais de 4, a fileira rola na horizontal. A Fase 5 troca essa lista pelos favoritos. |
| P2 | **O que a troca muda.** O Windows tem a saída "padrão" e a "padrão de comunicação" (usada por Discord, Teams etc.). | **As duas:** o clique troca a saída de tudo, como pede o PDF. |
| P3 | **Testes automáticos que mexem no seu som.** Mudar o volume geral, silenciar ou trocar a saída afeta o que você estiver ouvindo, inclusive um jogo. | O roteiro só **lê** o estado, mais um **toque mínimo** no volume geral: sobe 1% e volta ao valor original em menos de 2 s. O mudo geral e a troca de saída ficam só para o seu teste (6.4). |

## 3. Contexto do código atual

- `Audio\AudioSessionMonitor.cs`: cria o próprio `MMDeviceEnumerator` e, ao abrir, lê todas as saídas ativas (`Render`, `Active`), com o `AudioSessionManager` e as sessões de cada uma. Os tratadores só enfileiram na `DispatcherQueue`. O `Dispose` segue uma ordem fixa. Sessão nova faz um `Rescan` do gerenciador (desvio do D4 da Fase 2, verificado na conferência).
- `Audio\VolumeEchoFilter.cs` (eco por chave), `Audio\VolumeGlyph.cs` (glifo do volume), `ViewModels\ObservableObject.cs`, `SessionListViewModel.cs` e `SessionRowViewModel.cs` (com o sinalizador "atualizando pelo sistema").
- `Flyout\FlyoutWindow.xaml(.cs)`: o título fica na linha 0 e a lista de apps (`SessionList`) num `ScrollViewer` na linha 1. A janela cria o monitor no construtor e dá `Dispose` no `Closed`. O `ItemsSource` é ligado pelo código, porque o `x:Bind` no nível da janela chega tarde.
- Pacotes: `NAudio.Wasapi` 3.3.0 e `System.Drawing.Common` 10.0.0.
- Ferramentas: `tools\testes\sessoes.cs` (`listar` e `definir`, com a trava `MX-teste`), `tocar.cs`, `lib.ps1` (`PrepararFerramentas` publica as ferramentas em `%TEMP%\mixer-check\ferr`), `roteiro-f2.ps1` (que roda o `roteiro-f1.ps1`) e `medir.ps1` (`-ComSom`).

## 4. Decisões

### 4.1 Estrutura

| # | Decisão |
|---|---|
| D1 | **Um enumerador só por abertura.** `Audio\DeviceMonitor.cs` passa a ser dono do `MMDeviceEnumerator`, do `IMMNotificationClient` e da lista de saídas ativas. O `AudioSessionMonitor` deixa de criar o próprio enumerador: recebe as saídas do `DeviceMonitor` e ganha `AddDevice(MMDevice)` e `RemoveDevice(id)`. A janela cria o `DeviceMonitor` primeiro e dá `Dispose` nele por último. |
| D2 | **Mesmas regras de thread da Fase 2.** Os tratadores do `IMMNotificationClient` e do volume geral só copiam os valores e enfileiram na thread da tela. Dentro deles não se chama nenhum método COM, não se cancela inscrição e não se dá `Dispose`, porque a Microsoft avisa que isso pode travar. Depois do `Dispose`, o que ainda chegar é ignorado. |
| D3 | **Eventos tratados:** `OnDefaultDeviceChanged`, só para `Render` e o papel `Multimedia`, para não tratar o mesmo evento três vezes; `OnDeviceAdded`, `OnDeviceRemoved` e `OnDeviceStateChanged`, que fazem a lista ser relida e comparada pelo id; `OnPropertyValueChanged` é **ignorado**, porque dispara muitas vezes. |
| D4 | **Saída que entra ou sai com a janela aberta** (pendência D2 da Fase 2): ao entrar, `AddDevice` lê as sessões dela; ao sair, `RemoveDevice` solta o gerenciador e as sessões dela pelo mesmo caminho do `RemoveEntry`. As sessões também podem chegar desconectadas antes, e isso tem que funcionar nos dois sentidos. |

### 4.2 Modelo (lógica pura, entra nos testes)

| # | Decisão |
|---|---|
| D5 | `Audio\DeviceInfo.cs` guarda id, nome curto, nome completo e formato. `Audio\DeviceTracker.cs` mantém a lista de saídas e o id da padrão, e avisa quando uma saída entra, muda ou sai, e quando a padrão muda. **Só uma** fica ativa por vez. Se a padrão apontar para um id que ainda não está na lista, ela fica ativa quando ele entrar. Sem saída padrão (nenhuma saída ligada), nada fica ativo. |
| D6 | **Nome curto** (o rótulo do botão): primeiro a descrição do dispositivo (`PKEY_Device_DeviceDesc`), que é o nome que a pessoa pode mudar nas Configurações de Som (ex.: "Headset", "Soundbar"); senão, o nome completo (`PKEY_Device_FriendlyName`); senão, "Saída desconhecida". O **nome completo** vai na dica e no nome de acessibilidade. |
| D7 | **Ordem dos botões:** alfabética pelo nome curto, sem diferenciar maiúsculas, com o id como desempate. Fica estável, porque a ordem do Windows não é garantida. A Fase 5 passa a usar a ordem dos favoritos. |
| D8 | **Ícone do botão** (`Audio\DeviceGlyph.cs`), pelo `PKEY_AudioEndpoint_FormFactor`: fone, headset ou handset (3, 5, 6) usam o fone `E7F6`; alto-falantes, saída de linha ou SPDIF (1, 2, 8) usam a caixa de som `E7F5`; monitor ou TV por HDMI/DisplayPort (9) usam o monitor `E7F4`; qualquer outro valor usa `E7F5`. |

### 4.3 Troca da saída padrão

| # | Decisão |
|---|---|
| D9 | `Audio\PolicyConfig.cs`: interface não documentada `IPolicyConfig` (IID `f8679f50-850a-41cf-9c72-430f290290c8`, classe `870af99c-171d-4f9e-af0d-e63df40c2bc9`). É estável desde o Windows 7 e usada pelo EarTrumpet e pelo SoundSwitch. Só o método `SetDefaultEndpoint(id, papel)` é usado, mas a interface precisa ter **todos os métodos antes dele, na ordem certa**: `GetMixFormat`, `GetDeviceFormat`, `ResetDeviceFormat`, `SetDeviceFormat`, `GetProcessingPeriod`, `SetProcessingPeriod`, `GetShareMode`, `SetShareMode`, `GetPropertyValue`, `SetPropertyValue`, `SetDefaultEndpoint`, `SetEndpointVisibility`. Escreva com `[GeneratedComInterface]` (gerado na compilação, o caminho do AOT da A4). Se não funcionar, use `[ComImport]` e registre na seção 7. |
| D10 | **Clique num botão** chama `SetDefaultEndpoint` para os papéis `Console`, `Multimedia` e `Communications` (P2). Clicar na saída que já é a padrão não faz nada. O destaque **só muda pelo evento** `OnDefaultDeviceChanged`, para que haja uma única fonte da verdade. Erro na troca é registrado no log e o destaque fica como estava. Log: `saida padrao trocada para <nome curto>`. |

### 4.4 Volume geral

| # | Decisão |
|---|---|
| D11 | O volume geral é o da **saída padrão**: `AudioEndpointVolume.MasterVolumeLevelScalar` e `Mute`, com a notificação `OnVolumeNotification`. Quando a padrão muda, a inscrição antiga é cancelada e a nova é feita, as duas na thread da tela. |
| D12 | **Eco:** o mesmo `VolumeEchoFilter`, com a chave fixa `"*geral*"`. O slider vai de 0 a 100, escala linear, como o do Windows. O glifo do mudo usa o `VolumeGlyph`. A atualização vinda do sistema usa o mesmo sinalizador da Fase 2 para não gravar de volta. |
| D13 | **Sem saída padrão:** o slider e o mudo ficam desabilitados, e o lugar dos botões mostra "Nenhuma saída de áudio". |

### 4.5 Tela

| # | Decisão |
|---|---|
| D14 | **Topo** (substitui o título "Mixer de Áudio"; o título sai):<br>1. Fileira de botões das saídas (`ToggleButton`, 76 × 56 DIP: glifo em cima e nome curto embaixo, com reticências), dentro de um `ScrollViewer` horizontal que só rola quando os botões não cabem. O botão ativo fica marcado (`IsChecked`) com o estilo de destaque. Clicar no ativo não o desmarca.<br>2. Linha do volume geral: mudo, slider e valor, com as mesmas medidas da linha de um app.<br>3. Um separador fino e, embaixo, a lista de apps da Fase 2.<br>A janela continua com 360 × 480 DIP: a lista de apps fica menor e rola. |
| D15 | **ViewModels:** `DeviceListViewModel` (`ObservableCollection` de `DeviceButtonViewModel`: nome curto, nome completo, glifo, ativo) e `MasterVolumeViewModel` (volume, mudo, glifo, texto, habilitado). As ligações da janela são feitas pelo código, como na Fase 2. |
| D16 | **Acessibilidade e testes:** `DeviceList`; cada botão com `AutomationId DeviceButton`, `Name` = nome completo e `TogglePattern` refletindo o ativo; `MasterVolume` (`RangeValuePattern`), `MasterMute` (`Name` "Silenciar tudo" ou "Ativar som"), `MasterValue` e `NoDeviceText`. |
| D17 | **Log:** a linha `sessoes carregadas` ganha `saidas=<n>`. O tempo continua sendo do início do `Open` até a lista pronta (meta < 300 ms). |

## 5. Arquivos

**Novos:**
- `MixerDeAudio\Audio\DeviceInfo.cs`, `DeviceTracker.cs`, `DeviceGlyph.cs`, `DeviceMonitor.cs`, `PolicyConfig.cs`
- `MixerDeAudio\ViewModels\DeviceListViewModel.cs`, `DeviceButtonViewModel.cs`, `MasterVolumeViewModel.cs`
- testes `DeviceTrackerTests` e `DeviceGlyphTests`
- `tools\testes\roteiro-f3.ps1`

**Alterados:**
- `Audio\AudioSessionMonitor.cs` (D1, D4)
- `Flyout\FlyoutWindow.xaml(.cs)` (D14, D15)
- `MixerDeAudio.Tests.csproj` (links novos: `DeviceInfo`, `DeviceTracker`, `DeviceGlyph`)
- `tools\testes\sessoes.cs` (comandos da 6.2)
- `tools\testes\lib.ps1`
- `tools\testes\medir.ps1` (`-Ciclos`)
- `tools\testes\README.md`
- `docs\ESTADO.md`, `docs\PLANO.md` e este documento (seção 7)

## 6. Testes

**Regra geral:** como nas fases anteriores. Build sem erros e sem avisos, a bateria inteira de testes unitários passando, e os roteiros rodando no build publicado.

**O usuário pode estar jogando:** nada de teclas, cliques ou foco. Os apps reais só são lidos. Além disso, pela P3:
- **o roteiro nunca troca a saída padrão e nunca muda o mudo geral;**
- o volume geral só recebe o toque mínimo de +1% (−1% se estiver em 100%) e volta ao valor original, inclusive em caso de erro (`finally`).

### 6.1 Testes unitários (novos)

| Arquivo | Casos |
|---|---|
| `DeviceTrackerTests` | Ordem alfabética, sem diferenciar maiúsculas, com desempate pelo id. Saída que entra fica na posição certa. Saída que sai some, e se era a padrão, nada fica ativo. Trocar a padrão deixa só uma ativa. Padrão apontando para um id ainda desconhecido: fica ativa quando ele entra. Sem padrão, nada ativo. Id repetido não duplica. Mudança de nome reposiciona. Nome curto: descrição, depois o nome completo, depois "Saída desconhecida". |
| `DeviceGlyphTests` | Os formatos 1, 2, 3, 5, 6, 8 e 9, um valor desconhecido e o 0. |

### 6.2 Ferramentas

`sessoes.cs` ganha dois comandos:
- `dispositivos` lista uma linha por saída ativa: `id|nome curto|nome completo|formato|padrao(sim/nao)|volume|mudo`.
- `geral <volume>` muda o volume geral da saída padrão, mas **recusa** um valor que fique a mais de 0,02 do atual. É a trava do toque mínimo.

`lib.ps1` ganha as funções:
- `Devices` (pelo `sessoes.cs`);
- `DeviceButtons` (nome e estado de cada botão pela UI Automation);
- `MasterValue`;
- `SetMasterValue`, que também só aceita até ±2 em relação ao valor atual.

### 6.3 Roteiro na tela (`tools\testes\roteiro-f3.ps1`, Sonnet roda, Opus confere)

| Caso | Por que na tela | Passos | Esperado |
|---|---|---|---|
| 3.1 Saídas | Leitura real das saídas e montagem dos botões. | Abrir com `--abrir-sem-foco`. | Os botões são as saídas de `dispositivos`, na ordem do D7, e o único marcado é a padrão. Registrar a captura `PrintWindow` em `f3-janela.png`. |
| 3.2 Volume geral lido | Ligação com a saída padrão real. | Ler `MasterVolume`. | Igual a `volume × 100` da saída padrão (±1), com o mudo igual. |
| 3.3 Volume geral gravado | Caminho tela → Windows (toque mínimo). | Guardar o valor original V; `SetMasterValue V+1` (ou V−1); conferir; voltar para V. | `dispositivos` mostra o valor novo (±0,01) e, no fim, V de novo. |
| 3.4 Mudança de fora | Caminho Windows → tela. | `sessoes geral` com V±0,01; esperar; depois voltar para V. | O slider acompanha em até 1 s, sem gravar de volta. No fim, V. |
| 3.5 Sem regressão | A lista de apps continua igual. | Rodar `roteiro-f2.ps1`, que também roda o da Fase 1. | 2.1 a 2.8 OK, e 1.1 a 1.5 OK. |
| 3.6 Medição | Vazamento com dois enumeradores a menos e as notificações novas. | `medir.ps1 -ComSom -Ciclos 50`, medindo nos ciclos 5, 20 e 50, e registrando também os handles. | CPU parada < 0,1%. WS privado do ciclo 5 ao 20 < 5 MB. Bytes privados do ciclo 20 ao 50 < 3 MB. Os handles só são registrados, para a Fase 6. |

Ao terminar, o roteiro encerra o app e os tocadores e confere que o volume geral está igual ao do início.

### 6.4 Teste do usuário (na entrega)

1. Abrir pelo ícone. Os botões das saídas aparecem com o ícone e o nome certos, e a saída padrão está destacada.
2. Arrastar o volume geral: o som muda, e o ícone de volume da barra de tarefas mostra o mesmo valor. Silenciar e tirar o silêncio pelo botão.
3. Com a janela aberta, mudar o volume pelas teclas do teclado: o slider acompanha.
4. Clicar em outra saída: o som passa para ela, o destaque muda, e as Configurações de Som mostram a nova saída padrão. Clicar de volta na anterior. Clicar na que já está destacada: ela continua destacada e nada muda.
5. Se der: ligar ou desligar um fone com a janela aberta. O botão aparece ou some, e os apps dele entram ou saem da lista.

## 7. Resultados

**Status:** implementado em 19/11/2026; aguarda conferência (Opus) e teste do usuário (6.4).

**Build e testes:** build Debug x64 com 0 erros e 0 avisos; publicação Release sem avisos. Testes unitários: 95 aprovados, 0 falhas (os novos: `DeviceTrackerTests` e `DeviceGlyphTests`). Tempo de abertura: 137 a 150 ms (meta < 300 ms).

**Roteiro `roteiro-f3.ps1 -SemMedicao`** (build publicado; saída padrão "Soundbar", 2 saídas, volume 0,520, mudo desligado; ao fim, o mesmo):

| Caso | Resultado |
|---|---|
| 3.1 Saídas | OK: 2 botões ("Monitor", "Soundbar") na ordem do D7, nomes completos iguais, só a padrão marcada, log `saidas=2`, 137 ms. Captura `%TEMP%\mixer-check3-janela.png`. |
| 3.2 Volume geral lido | OK: slider 52 (esperado 52), mudo igual. |
| 3.3 Volume geral gravado | OK: 0,52 -> 0,53 pela tela -> 0,52; final 0,520. |
| 3.4 Mudança de fora | OK: slider acompanhou em 0,09 s, sem gravar de volta; final 0,520. |
| 3.5 Sem regressão | OK: 2.1 a 2.8 e 1.1 a 1.5. |
| Final | OK: saída padrão, volume e mudo iguais aos do início; nenhum processo sobrando. |

**3.6 Medição** (`medir.ps1 -ComSom -Ciclos 50`, rodada direta, build publicado; MB):

| Parado 10 s: WS priv / bytes priv | CPU parada | Ciclo 5: WS priv / bytes priv | Ciclo 20: WS priv / bytes priv | Ciclo 50: WS priv / bytes priv | WS priv 5 a 20 | Bytes priv 20 a 50 |
|---|---|---|---|---|---|---|
| 2,9 / 75,8 | 0% | 2,6 / 120,3 | 1,7 / 128,4 | 1,4 / 131,3 | -0,9 (< 5) | +2,9 (< 3, folga pequena) |

Handles (só registro, para a Fase 6): parado 614; ciclo 5: 1273; ciclo 20: 1318; ciclo 50: 1406 (cerca de +3 por abertura).

**Desvios em relação à análise:**
1. **D9:** `IPolicyConfig` foi escrito com `[GeneratedComInterface]` (funcionou; criação por `CoCreateInstance` + `StrategyBasedComWrappers`). Não foi preciso `ComImport`. A troca real da saída **não foi exercitada** (regra P3); fica para o teste do usuário (6.4, passo 4).
2. **D1/D3:** no NAudio 3.3.0 o `IMMNotificationClient` é **interno**. O `DeviceMonitor` usa `MMDeviceEnumerator.CreateNotificationClient(false)` (eventos na thread de áudio, sem `SynchronizationContext`) e os eventos `DefaultDeviceChanged`, `DeviceAdded`, `DeviceRemoved` e `DeviceStateChanged`; `PropertyValueChanged` não é inscrito. Regras de thread do D2 mantidas. Várias notificações seguidas viram uma releitura só (`_resyncPending`).
3. **D1:** o `AudioSessionMonitor` continua dono dos `AudioSessionManager`; o `DeviceMonitor` é dono dos `MMDevice` e do enumerador. `DeviceRemoved` é disparado antes do Dispose do `MMDevice`.
4. **D14:** o volume geral e a lista de apps usam `ItemsControl` com um item só (`MasterHost`) para o `x:Bind` funcionar, como na lista de apps. O `IsEnabled` fica nos controles (a `Grid` não o tem).
5. **D4 e D3 sem teste automático:** ligar ou desligar uma saída (e a troca da padrão) só são verificados no teste do usuário (6.4, passos 4 e 5), porque o roteiro não pode mexer nelas.
6. **Ajustes nas ferramentas antigas:** `roteiro-f1.ps1` (1.2) deixou de exigir `FlyoutTitle` (o título saiu no D14) e passou a exigir `DeviceList`; `roteiro-f2.ps1` aceita `saidas=<n>` na linha de log. `roteiro-f3.ps1` espera 1,3 s antes do 3.4 por causa da janela de 1 s do filtro de eco (uma mudança de fora, logo após uma gravação da tela com o mesmo valor, seria tomada por eco; mesmo comportamento da Fase 2).
7. A medição 3.6 foi rodada direto pelo `medir.ps1`; o trecho do `roteiro-f3.ps1` que a chama e interpreta a saída não foi executado até o fim.

**Pontos de risco para a conferência:** `DeviceMonitor.Resync` e `RefreshMaster` (ordem de Dispose e `_masterDirty`), `AudioSessionMonitor.RemoveDevice`/`Rescan`, `DeviceButtonViewModel.OnClick` (refaz o destaque após o clique do `ToggleButton`), crescimento de bytes privados do ciclo 20 ao 50 (2,9 MB, perto do limite de 3).

### Conferência (Opus, 19/11/2026)

- Roteiro completo rodado de novo de ponta a ponta, 3.6 incluído: todos os casos OK. Medição: CPU 0%, WS privado 5→20 +0,5 MB, bytes privados 20→50 +1,7 MB. Som final igual ao inicial e nenhum processo restante.
- A captura f3-janela.png está conferida: a fileira de saídas, a linha do volume geral e o separador estão certos.
- `DeviceMonitor`:
  - A ordem na saída que some está certa: solta o volume geral, avisa o monitor de sessões e só então faz o Dispose.
  - O `Dispose` cancela o cliente de notificações antes de soltar o enumerador.
  - Nenhuma chamada COM acontece nos tratadores.
- `PolicyConfig`: a tabela de métodos está na ordem do D9 e o `Release` do ponteiro está correto, porque o wrapper faz AddRef.
- `AudioSessionMonitor`: o `Rescan` de uma saída que já saiu não faz nada.
- Detalhe sem efeito: a bandeira `_resyncPending` não junta releituras seguidas, porque ela é ligada e desligada dentro da mesma chamada. Só significa leituras a mais quando um fone é conectado. Fica para a Fase 6.
- Clicar na saída que já está destacada não foi testado na tela, porque o `OnClick` depende do x:Bind refazer o `IsChecked`. Esse caso foi incluído no passo 4 do teste do usuário (6.4).
