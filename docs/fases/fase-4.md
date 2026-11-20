# Fase 4 — Saída por app

**Status:** análise aprovada (P1 a P3 como recomendado), implementada, conferida e teste do usuário (6.4) aprovado em 19/11/2026, com a largura ajustada para 420 px
**Implementação:** Sonnet (`medium`) · **Análise e conferência:** Opus · **Documentação:** Haiku

Este documento é autossuficiente. Leia também:
- `docs/PLANO.md`;
- `docs/fases/fase-3.md`, seções 4 e 7: saídas, `DeviceMonitor`, `DeviceTracker`, `PolicyConfig` e as ferramentas de teste;
- `docs/fases/fase-2.md`, seção 4: as regras de thread.

Pasta do repositório: `C:\Projetos\Mixer de Audio`, branch `main`, último commit `1a56d19`. **Não commitar.**

## 1. Objetivo

Cada linha de app ganha uma fileira pequena de botões, um para cada saída, que mostra para onde aquele app manda o som. Um clique envia o app para aquela saída, e o Windows guarda essa escolha mesmo depois que o app fecha. Apps sem escolha própria seguem a saída padrão. Apps com escolha própria continuam onde estão quando a padrão é trocada pela fileira do topo da Fase 3.

A troca usa a interface interna do Windows `IAudioPolicyConfigFactory`. É o mesmo mecanismo da tela "Volume do app e preferências de dispositivo" das Configurações e o mesmo que o EarTrumpet usa.

Fica fora desta fase:
- os favoritos (Fase 5);
- o roteamento da entrada, ou seja, o microfone. Só a saída é tratada aqui;
- o visual final (Fase 6).

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Como um app volta a seguir a saída padrão?** | **Clicar no botão da saída que é a padrão do Windows** apaga a escolha do app, e ele volta a seguir a padrão. Não precisa de um botão a mais. O app só fica preso a uma saída quando a escolhida é diferente da padrão. |
| P2 | **Como são os botões na linha do app?** A janela tem 360 px. | **Só o ícone** (28×24), à direita do nome do app. A dica e o nome de acessibilidade dizem "Enviar <app> para <saída>". A linha continua com 56 px de altura. Se duas saídas tiverem o mesmo ícone (dois fones, por exemplo), a dica as diferencia. Na Fase 5 viram só os 2 ou 3 favoritos. |
| P3 | **Testes automáticos.** | O roteiro **só troca a saída dos tocadores de teste `MX-teste`**, que tocam silêncio. Os apps reais são só lidos. No fim, a escolha dos tocadores é apagada, também em caso de erro. |

## 3. Contexto do código atual

- `Audio\DeviceMonitor.cs` (Fase 3):
  - lista de saídas ativas e saída padrão no `Tracker` (`DeviceTracker`: `Devices`, `ActiveId` e eventos de mudança);
  - `SetDefaultDevice(id)` pela `PolicyConfig`.
- `Audio\PolicyConfig.cs` é o modelo de interface COM feita com `[GeneratedComInterface]` e `CoCreateInstance` por `LibraryImport`.
- `Audio\AudioSessionMonitor.cs` junta as sessões de todas as saídas por .exe, na chave do `SessionTracker`. Um app que muda de saída perde a sessão na saída antiga e ganha outra na nova, mas a linha continua a mesma, porque a chave é o .exe.
- `Audio\SessionTracker.cs`:
  - O `GroupSnapshot` não traz os pids do grupo.
  - O `SessionInfo` tem `ProcessId`. A linha "Sons do sistema" tem pid 0.
- `ViewModels\SessionRowViewModel.cs` (volume, mudo, ícone e a bandeira `_updatingFromSystem`) e `SessionListViewModel.cs`.
- `ViewModels\DeviceListViewModel.cs` e `DeviceButtonViewModel.cs` formam a fileira do topo.
- `Flyout\FlyoutWindow.xaml`: o modelo da linha do app tem o nome na linha 0 e, na linha 1, o mudo, o slider e o valor.
- `tools\testes\tocar.cs` toca silêncio com `WasapiOut` na saída padrão. `sessoes.cs` tem `listar`, `definir`, `dispositivos` e `geral`, com travas.

## 4. Decisões

### 4.1 Interface do Windows

| # | Decisão |
|---|---|
| D1 | **`Audio\AppRoutingService.cs`:** fábrica `IAudioPolicyConfigFactory`, obtida com `RoGetActivationFactory("Windows.Media.Internal.AudioPolicyConfig")` (combase.dll) e com o IID do Windows 10 21H2 ou mais novo e do Windows 11: `ab3d4648-e242-459f-b02f-541c70306324`.<br>A interface herda de `IInspectable`. Na tabela de métodos vêm primeiro os 3 do `IInspectable` (`GetIids`, `GetRuntimeClassName`, `GetTrustLevel`) e depois **19 métodos não usados, na ordem**: `add_CtxVolumeChange`, `remove_CtxVolumeChanged`, `add_RingerVibrateStateChanged`, `remove_RingerVibrateStateChange`, `SetVolumeGroupGainForId`, `GetVolumeGroupGainForId`, `GetActiveVolumeGroupForEndpointId`, `GetVolumeGroupsForEndpoint`, `GetCurrentVolumeContext`, `SetVolumeGroupMuteForId`, `GetVolumeGroupMuteForId`, `SetRingerVibrateState`, `GetRingerVibrateState`, `SetPreferredChatApplication`, `ResetPreferredChatApplication`, `GetPreferredChatApplication`, `GetCurrentChatApplications`, `add_ChatContextChanged`, `remove_ChatContextChanged`. Esses podem ser declarados como `int MetodoN()` só para ocupar a posição.<br>Em seguida vêm `SetPersistedDefaultAudioEndpoint(uint pid, int flow, int role, IntPtr hstringId)` e `GetPersistedDefaultAudioEndpoint(uint pid, int flow, int role, out IntPtr hstringId)`.<br>**Não declarar `ClearAllPersistedApplicationDefaultEndpoints`**, que vem logo depois: ele apagaria as escolhas de todos os apps do usuário.<br>Escreva com `[GeneratedComInterface]`. Os HSTRINGs usam `WindowsCreateString`, `WindowsGetStringRawBuffer` e `WindowsDeleteString` (combase.dll, `LibraryImport`), sempre liberados num `finally`. |
| D2 | **Formato do id.** A interface usa o caminho do dispositivo, não o id do `MMDevice`: `\\?\SWD#MMDEVAPI#<id do MMDevice>#{e6327cad-dcec-4949-ae8a-991e976a79d2}`, que é a classe de saída de áudio. A conversão nos dois sentidos fica em lógica pura (`AppRouting.ToPolicyId` e `FromPolicyId`). A volta tolera maiúsculas e minúsculas e devolve nulo para um texto vazio ou fora do formato. |
| D3 | **Gravar:** `Set` com `flow = Render (0)` para os papéis `Console (0)` e `Multimedia (1)`, como o EarTrumpet. A comunicação fica de fora, porque é o Windows que decide a saída das chamadas. **Apagar a escolha:** `Set` com HSTRING nulo (`IntPtr.Zero`) nos mesmos papéis. **Ler:** `Get(pid, Render, Multimedia)`. Vazio quer dizer "segue a padrão". |
| D4 | **Para quem:** o Windows guarda a escolha por app (pelo .exe), mas a chamada recebe um pid. A gravação é feita para **cada pid do grupo**, sem repetir pids, e a leitura usa o primeiro pid. O `SessionTracker` ganha `ProcessIds(key)`, que devolve os pids distintos maiores que 0. A linha "Sons do sistema" e grupos sem pid não têm botões. |
| D5 | **Fábrica por abertura:** é criada uma vez quando a janela abre, na thread da tela, e liberada no `Closed`. Se a ativação falhar (um Windows que mudou a interface), o erro é registrado uma vez (`ERRO saida por app indisponivel: <msg>`) e as linhas ficam **sem botões**. O resto da janela funciona normalmente. |
| D6 | **Sem evento:** o Windows não avisa quando a escolha de um app muda. A escolha é lida quando a linha nasce e relida depois do próprio clique, para confirmar. Uma mudança feita pelas Configurações com a janela aberta só aparece na próxima abertura. Como a janela vive pouco, isso é aceito. |

### 4.2 Modelo (lógica pura, entra nos testes)

| # | Decisão |
|---|---|
| D7 | **`Audio\AppRouting.cs`:**<br>`Effective(preferidaId, padraoId, idsAtivos)`: devolve a escolhida, se ela estiver entre as ativas; senão, a padrão. Se a escolhida estiver desligada, o Windows manda o som para a padrão, e o destaque mostra isso.<br>`OnClick(clicadaId, preferidaId, padraoId)`: devolve `Nenhuma` se a clicada já for a efetiva; `Apagar` se a clicada for a padrão (P1); `Gravar(clicadaId)` nos outros casos.<br>`ShowButtons(isSystem, temPid, quantasSaidas, servicoOk)`: só mostra os botões com **2 saídas ou mais**, porque com uma não há o que escolher. |
| D8 | **Destaque:** exatamente um botão por linha, o efetivo. Ele muda depois de um clique (pela releitura do D6) e quando a padrão muda, mas só nas linhas que seguem a padrão. Saídas que entram ou saem refazem os botões de todas as linhas, na ordem do D7 da Fase 3. |

### 4.3 Tela

| # | Decisão |
|---|---|
| D9 | **Linha do app** (P2): a linha 0 passa a ter o nome (`*`) e, à direita, um `ItemsControl` horizontal com `ToggleButton`s de 28×24, só com o ícone (`DeviceGlyph`) e espaçamento 4. A altura da linha continua 56. O nome do app é cortado com reticências antes dos botões. |
| D10 | **ViewModel:** `ViewModels\RouteButtonViewModel.cs` tem `DeviceId`, `Glyph`, `IsActive`, `Name` ("Enviar <app> para <nome curto>") e `OnClick`. O `SessionRowViewModel` ganha `Routes` (`ObservableCollection`), `RouteVisibility` e `PreferredId`.<br>O clique segue o mesmo padrão do `DeviceButtonViewModel`: pede a troca e reenvia `IsActive` para desfazer o toggle automático do botão. Clicar no destaque não faz nada. |
| D11 | **Ligação:** o `SessionListViewModel` recebe o `DeviceTracker` e o `AppRoutingService`. Quando uma linha nasce, ele lê a escolha (D6) e monta os botões. Ele também se inscreve nas mudanças de saídas e da padrão do tracker para atualizar as linhas (D8). Tudo acontece na thread da tela e a inscrição é cancelada no `Dispose`. |
| D12 | **Log:** `saida de <app> trocada para <nome curto>` ou `saida de <app> volta a seguir a padrao`. Erros como `ERRO ao trocar a saida de <app>: <msg>`, e o destaque fica como estava. |
| D13 | **AutomationIds:** `RouteButton` (Name = "Enviar <app> para <nome curto>", com o padrão Toggle) dentro de cada `SessionRow`. |

### 4.4 O que esperar do Windows

| # | Decisão |
|---|---|
| D14 | Alguns apps passam para a nova saída na hora; outros só depois de reiniciar o som, ou seja, pausar e tocar de novo ou reabrir o app. Isso é do Windows (o mesmo acontece pelas Configurações) e não é tratado aqui. O destaque mostra a **escolha**, não onde a sessão está tocando naquele instante. O roteiro registra qual dos dois casos aconteceu com o tocador de teste. |

## 5. Arquivos

**Novos:**
- `MixerDeAudio\Audio\AppRoutingService.cs` (D1 a D5)
- `MixerDeAudio\Audio\AppRouting.cs` (D2 e D7, lógica pura)
- `MixerDeAudio\ViewModels\RouteButtonViewModel.cs`
- `MixerDeAudio.Tests\AppRoutingTests.cs`
- `tools\testes\roteiro-f4.ps1`

**Alterados:**
- `Audio\SessionTracker.cs`: `ProcessIds(key)`. É lógica pura, então ganha teste.
- `ViewModels\SessionRowViewModel.cs` e `SessionListViewModel.cs` (D10 e D11).
- `Flyout\FlyoutWindow.xaml(.cs)`: o modelo da linha (D9) e a criação e o Dispose do serviço (D5).
- `MixerDeAudio.Tests\MixerDeAudio.Tests.csproj`: link para o `AppRouting.cs`.
- `tools\testes\sessoes.cs`: `listar` passa a mostrar também o id da saída de cada sessão, numa coluna no fim. Comandos novos:
  - `rota-ler <pid>`: imprime o id do `MMDevice` escolhido ou `padrao`;
  - `rota <pid> <id|padrao>`: **recusa** pid cujo processo não seja `tocar` nem `tocar-b`, com código 3.
- `tools\testes\lib.ps1`:
  - `RouteButtons $h <app>` (nome e estado);
  - `ClickRoute $h <app> <nome curto>`, só pelo padrão Toggle e **só para linhas `MX-teste`** (recusa as outras);
  - `RotaLer`, `Rota` e `SessionDevice <pid>`.
- `tools\testes\README.md`, `docs\ESTADO.md` e `docs\PLANO.md` (estado da fase).

## 6. Testes

### 6.1 Testes unitários (novos)

- `AppRoutingTests`:
  - id: a ida e a volta; maiúsculas e minúsculas; texto vazio, nulo ou fora do formato;
  - `Effective`: sem escolha; escolha ativa; escolha desligada; sem padrão;
  - `OnClick`: os 3 resultados;
  - `ShowButtons`: os 4 casos.
- `SessionTrackerTests`: `ProcessIds` sem repetir, sem pid 0 e para um grupo inexistente.
- A bateria inteira continua passando.

### 6.2 Regras do roteiro (P3)

- O roteiro só clica nas linhas `MX-teste`, e só pelo padrão Toggle: sem teclas, sem cliques reais e sem foco.
- Ele lê a escolha de todos os apps reais antes e depois, e as duas leituras têm que ser iguais.
- A saída padrão, o volume geral e o mudo geral não são tocados.
- O `finally` faz, nesta ordem: `Rota <pid> padrao` para cada tocador **ainda vivo**, depois encerra os tocadores e o app.
- Se o Monitor (ou a saída que não é a padrão) não existir, os casos 4.2 a 4.4 são marcados como "pulado" com o motivo, e não como falha.

### 6.3 Roteiro na tela (`tools\testes\roteiro-f4.ps1`, Sonnet roda, Opus confere)

| Caso | Por que na tela | Passos | Esperado |
|---|---|---|---|
| 4.1 Botões nas linhas | Layout e ligação da linha | Tocador A; abrir; ler `RouteButtons` de todas as linhas; captura `f4-janela.png` | Cada linha de app tem 1 botão por saída ativa, e "Sons do sistema" não tem nenhum. Na linha MX-teste A, o destaque está na padrão, porque A não tem escolha. Os nomes seguem "Enviar MX-teste A para <saída>". |
| 4.2 Enviar para outra saída | Clique → Windows → destaque | `ClickRoute` A → a saída que não é a padrão | `rota-ler` do pid A dá o id dela, e o destaque muda para ela em até 1 s. O log tem `saida de MX-teste A trocada para <nome>`. A sessão de A aparece na nova saída em até 3 s; se não aparecer, o tocador é reiniciado e a nova sessão tem que aparecer lá (registrar qual dos dois casos aconteceu). A padrão e a escolha dos apps reais continuam iguais. |
| 4.3 Reabrir mantém | A leitura quando a linha nasce | `--fechar`, depois `--abrir-sem-foco` | O destaque de A continua na saída escolhida. |
| 4.4 Voltar a seguir a padrão | P1 | `ClickRoute` A → a padrão | `rota-ler` dá `padrao`, o destaque vai para a padrão, e o log tem `volta a seguir a padrao`. |
| 4.5 Sem regressão | — | `roteiro-f3.ps1 -SemMedicao` (que roda o f2, que roda o f1) | Todos os casos OK. |
| 4.6 Medição | — | `medir.ps1 -ComSom -Ciclos 50` | CPU parada < 0,1%. WS privado do ciclo 5 ao 20 < 5 MB. Bytes privados do ciclo 20 ao 50 < 3 MB. Handles registrados. |

No fim, o roteiro confere que nenhum processo sobrou e que `rota-ler` dos tocadores (pela última leitura feita) era `padrao`.

### 6.4 Teste do usuário (na entrega)

1. Abrir: cada app tem os botões das saídas, com o destaque onde ele toca.
2. Enviar um app real (navegador ou Spotify, por exemplo) para a outra saída: o som passa para lá, na hora ou depois de pausar e tocar de novo.
3. Trocar a saída padrão pela fileira do topo: o app enviado continua onde estava, e os outros mudam junto.
4. Fechar e reabrir o app enviado: ele continua na saída escolhida. As Configurações (Sistema > Som > Volume do app e preferências de dispositivo) mostram a mesma escolha.
5. Clicar no botão da padrão nesse app: ele volta a seguir a padrão.

## 7. Resultados

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q`: 0 erros, 0 avisos.
**Testes unitários:** 118 aprovados, 0 falhas (95 da Fase 3 + 23 novos: `AppRoutingTests` e `ProcessIds` no `SessionTrackerTests`).
**Roteiro:** `roteiro-f4.ps1` rodado inteiro, com o 4.6, e passou (`Todos os casos passaram.`). Saídas ativas: 2 (padrão `Soundbar`, outra `Monitor`).

| Caso | Resultado |
|---|---|
| 4.1 Botões nas linhas | OK. 7 linhas; cada app com 2 botões (1 por saída); "Sons do sistema" sem botões; na linha MX-teste A o destaque está na padrão (`Enviar MX-teste A para Soundbar`); nomes corretos. Captura `%TEMP%/mixer-check/f4-janela.png`. |
| 4.2 Enviar para outra saída | OK. `rota-ler` deu o id do Monitor em 0,12 s; destaque mudou em 0,14 s; log `saida de MX-teste A trocada para Monitor`; padrão e escolha dos 6 apps reais iguais. **A sessão NÃO mudou de saída na hora (3 s): só apareceu no Monitor depois de reiniciar o tocador** (a escolha vale para o novo processo do mesmo .exe). |
| 4.3 Reabrir mantém | OK. Depois de `--fechar` e `--abrir-sem-foco`, o destaque de A continua em Monitor. |
| 4.4 Voltar a seguir a padrão | OK. `rota-ler` = `padrao`, destaque na Soundbar, log `volta a seguir a padrao`. A sessão do tocador continuou no Monitor até reiniciar (informativo, como no 4.2). |
| 4.5 Sem regressão | OK. `roteiro-f3 -SemMedicao` (casos 3.1 a 3.5, que rodam f2 e f1): todos OK. |
| 4.6 Medição | OK (tabela abaixo). |
| Finais | Escolha dos tocadores vivos apagada (última leitura `padrao`); escolha de 6 apps reais igual antes e depois; saída padrão igual; nenhum processo sobrou (app=0, tocadores=0). |

**Medição (50 ciclos, dois tocadores tocando; MB; última rodada completa do roteiro):**

| Parado 10 s: WS priv / bytes priv | CPU parada | Ciclo 5: WS priv / bytes priv | Ciclo 20: WS priv / bytes priv | Ciclo 50: WS priv / bytes priv | Handles parado / 5 / 20 / 50 |
|---|---|---|---|---|---|
| 2,8 / 76,9 | 0% | 2,9 / 125,7 | 1,9 / 132,9 | 1,8 / 132,0 | 614 / 1249 / 1319 / 1425 |

WS privado 5 a 20: -1,0 MB (limite 5); bytes privados 20 a 50: -0,9 MB (limite 3). Handles só registrados (Fase 6). Abertura: cerca de 146 ms (log `sessoes carregadas`).

**Desvios e observações:**
1. **Medição, primeira rodada completa:** bytes privados 20 a 50 deram +4,6 MB (limite 3), com 126,9 / 132,5 / 137,1 nos ciclos 5, 20 e 50. Havia um `StrategyBasedComWrappers` novo a cada abertura da janela; passou a ser um só, estático (`AppRoutingService.Wrappers`). Depois disso, duas rodadas deram -0,1 e -0,9 MB. Não deu para separar o efeito da troca do ruído da medição (a Fase 3 já ficava em +2,9, perto do limite).
2. **`tocar.cs`:** passou a procurar a própria sessão em todas as saídas ativas e a reaplicar o `DisplayName` a cada 250 ms. Com a saída por app a sessão do tocador nasce em outra saída (e sem nome), e o roteiro precisa que a linha continue `MX-teste A`.
3. **`StartPlayer` (lib.ps1):** tira o tocador do mudo ao nascer. O Windows guarda volume e mudo por app e o tocador estava mudo por herança de rodadas anteriores; isso quebrava o caso 2.3 do roteiro da Fase 2 (não era regressão do app).
4. **`sessoes.cs`:** a interface `IAudioPolicyConfigFactory` foi escrita com `[ComImport]` clássico (a ferramenta é de arquivo único), com as mesmas posições de método da D1; só IUnknown na base, com os 3 métodos do `IInspectable` declarados como espaços. `ClearAllPersistedApplicationDefaultEndpoints` não é declarado.
5. **D1:** os métodos não usados do app (`GetIids` etc. mais os 19) têm nomes próprios no `[GeneratedComInterface]` e a assinatura vazia `int M()`, porque nunca são chamados.
6. `ClickRoute` usa `TogglePattern.Toggle` e isso dispara o `Click` do `ToggleButton` (D10 funcionou sem mudança). O `PLANO.md` não foi alterado: nenhum desvio afeta as decisões A1 a A9.
7. **Teste do usuário:** além do 6.4, vale conferir um app real que a janela não tenha como reiniciar (navegador, Spotify) para ver se muda na hora ou só ao reiniciar o som (D14); o tocador de teste não muda na hora.

### Conferência (Opus, 19/11/2026)

- **Medição repetida na conferência** (`medir.ps1 -ComSom -Ciclos 50`):
  - WS privado do ciclo 5 ao 20: −1,2 MB.
  - Bytes privados do ciclo 20 ao 50: −0,8 MB.
  - Handles: 614 parado, 1252 no ciclo 5, 1346 no 20 e 1432 no 50.
  - Nenhum processo restante.
  - Esta é a terceira rodada seguida dentro do limite, depois de passar a usar um único `StrategyBasedComWrappers` estático.
- A captura `f4-janela.png` está conferida: os botões de saída aparecem à direita do nome, com o destaque na padrão, e a altura da linha não mudou.
- `AppRoutingService`:
  - A vtable segue a D1: 3 métodos do `IInspectable`, os 19 não usados, depois Set e Get. `ClearAll…` não é declarado em lugar nenhum (só aparece nos comentários).
  - Os HSTRINGs são liberados em `finally`. A ativação libera o próprio HSTRING. O ponteiro da fábrica é liberado no `Dispose`, porque o wrapper faz o próprio AddRef.
- `SessionListViewModel`:
  - Leitura e gravação ficam dentro de `try/catch` com log.
  - O clique na padrão apaga a escolha (P1), inclusive quando a escolhida está desligada.
  - A inscrição nos trackers é cancelada no `Dispose`.

### Ajuste pedido pelo usuário (19/11/2026)

- A janela passou de 360 para **420 DIP** de largura (`FlyoutWindow.WidthDip`), porque os campos estavam apertados. O caso 1.2 do `roteiro-f1.ps1` foi atualizado para conferir 420.
- A lista de apps, o separador e a linha do volume geral ganharam 18 px de margem à direita. Assim o valor em % não encosta na barra de rolagem.
- Build sem avisos, 118 testes OK e roteiro-f1 OK. A captura foi conferida.
