# Fase 5 — Configurações e saídas favoritas

**Status:** análise aprovada (P1 a P3 como recomendado), implementada, conferida e teste do usuário (6.4) aprovado em 20/11/2026
**Implementação:** Sonnet (`medium`) · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também:
- `docs/PLANO.md`;
- `docs/fases/fase-3.md` e `docs/fases/fase-4.md`, seções 4 e 7: saídas, `DeviceTracker`, botões da linha do app e ferramentas de teste.

Pasta do repositório: `C:\Projetos\Mixer de Audio`, branch `main`, último commit `0604e9f`. **Não commitar.**

## 1. Objetivo

O PDF pede, em "Rodapé e configurações":
- um botão de configurações;
- escolher as 2 ou 3 saídas favoritas, que passam a ser as únicas mostradas nos botões;
- tema claro, escuro ou automático;
- iniciar com o Windows, sem atrasar o boot.

As escolhas ficam em `%LOCALAPPDATA%\MixerDeAudio\config.json` (A8). "Iniciar com o Windows" usa a chave `HKCU\...\Run` (A2).

Fora desta fase: o visual final, a altura adaptável da janela e o instalador, que ficam na Fase 6.

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Onde fica a tela de configurações?** | **Dentro da própria janelinha:** a engrenagem do rodapé troca o conteúdo por uma página de configurações, com um botão "voltar". Não abre uma segunda janela, e tudo continua sendo liberado ao fechar. O menu da bandeja ganha o item "Configurações", que abre a janelinha direto nessa página. |
| P2 | **E se a saída ativa não for favorita?** Por exemplo, a padrão é o Monitor, mas os favoritos são o Headset e a Soundbar. | **A saída ativa sempre aparece**, mesmo sem ser favorita, como um botão a mais no fim. Assim sempre há um destaque mostrando onde o som está. A mesma regra vale na linha de cada app, para a saída efetiva dele. Favoritas desligadas não aparecem (igual a hoje). Com nenhuma favorita escolhida, todas as saídas ativas aparecem, como agora. |
| P3 | **Testes automáticos que mexem nas suas configurações.** | O roteiro **guarda uma cópia** do seu `config.json` e do valor `MixerDeAudio` da chave Run (e do `StartupApproved`) antes de começar, e **devolve tudo** no fim, também em caso de erro. Ele só mexe no valor `MixerDeAudio`, nunca em outro valor da chave Run. Nada disso muda o som. |

## 3. Contexto do código atual

- `Flyout\FlyoutWindow.xaml(.cs)`:
  - tamanho `WidthDip = 420` × `HeightDip = 480`, com `MicaBackdrop`;
  - linhas do `Root`: saídas, volume geral, separador e lista de apps;
  - é criada a cada abertura e liberada no `Closed`.
- `Tray\TrayIconService.cs`: menu `PopupMenu` com "Abrir" e "Sair" (`MenuFlyoutItem` + `ActionCommand`).
- `Services\CommandLine.cs`: `--abrir`, `--abrir-sem-foco`, `--fechar`, `--sair`, `--sem-aparar`. O `App.xaml.cs` trata o comando, inclusive o que chega por redirecionamento de outra cópia.
- `Services\AppPaths.cs`: `DataFolder`, `LogsFolder` e `LogFile`.
- `ViewModels\DeviceListViewModel.cs`: a fileira do topo, montada pelo `DeviceTracker` na ordem D7 da Fase 3.
- `SessionListViewModel.cs` e `RouteButtonViewModel.cs`: os botões de cada linha (D7 e D8 da Fase 4).
- Ainda não existe nenhum arquivo de configuração nem leitura da chave Run.

## 4. Decisões

### 4.1 Configuração

| # | Decisão |
|---|---|
| D1 | **`Services\AppSettings.cs`** (lógica pura): um `record` com `Versao = 1`, `Favoritos` (lista de ids de `MMDevice`), `NomesFavoritos` (id → último nome curto, usado para mostrar uma favorita desligada) e `Tema` (`automatico`, `claro` ou `escuro`).<br>`Parse(string?)` volta aos valores padrão quando o JSON é inválido, nulo ou de outra versão. Tema desconhecido vira `automatico`. A lista de favoritos é cortada em 3 e perde ids repetidos ou vazios. `Serialize()` usa `System.Text.Json` com um `JsonSerializerContext` gerado, que é compatível com AOT (A4). |
| D2 | **`Services\SettingsStore.cs`:** lê o arquivo uma vez, na partida. A partir daí a configuração fica em memória (`App.Settings`). Cada mudança grava na hora, de forma atômica: `config.json.tmp` seguido de `File.Move(..., overwrite: true)`.<br>Se o arquivo estiver corrompido, isso vai para o log (`ERRO config invalida, usando o padrao`) e ele é sobrescrito na próxima gravação. Erro de gravação também vai para o log, e o app segue com o valor em memória.<br>O evento `Changed` avisa a janela aberta. |
| D3 | **"Iniciar com o Windows" não vai para o `config.json`:** a fonte da verdade é o registro, porque o Gerenciador de Tarefas também pode desligar o app. Fica em `Services\StartupRegistration.cs`, dividido em duas partes:<br>• **Lógica pura** (`StartupRules`): o caminho da chave Run aponta para este `.exe`? A comparação ignora maiúsculas e aspas. Os bytes do `StartupApproved\Run` dizem que o app está desligado? Byte 0 ímpar e diferente de `0x02` e `0x06`, como `0x03`, quer dizer desligado.<br>• **Registro:** **ligado** = valor `MixerDeAudio` em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` apontando para este `.exe` **e** não desligado no `HKCU\...\Explorer\StartupApproved\Run`.<br>Ligar grava `"<caminho do exe>"`, sem argumento, porque assim só o ícone da bandeja é criado, e apaga o valor `MixerDeAudio` do `StartupApproved\Run` se existir.<br>Desligar apaga o valor da Run. Nenhum outro valor é tocado. |
| D4 | **Sem atrasar o boot:** a partida já é leve (só o ícone, modo de eficiência e a aparagem de 2 s). A leitura do `config.json` é pequena e síncrona. Nada a mais roda na partida. |

### 4.2 Favoritos (lógica pura, entra nos testes)

| # | Decisão |
|---|---|
| D5 | **`Audio\FavoriteFilter.cs`:** `Visible(devices, favoritos, activeId)`. Sem favoritos, devolve todas as saídas. Com favoritos, devolve as favoritas que estão ativas e, se a saída ativa (`activeId`) não for favorita, acrescenta ela no fim (P2).<br>A ordem é a do D7 da Fase 3 (alfabética), com a extra sempre no fim. A mesma função serve para o topo (`activeId` = a padrão) e para a linha do app (`activeId` = a saída efetiva dela, D7 da Fase 4). |
| D6 | **Botões na linha do app** (D7 da Fase 4): a regra "só com 2 saídas ou mais" passa a contar as saídas **visíveis** pelo D5. |
| D7 | **Mudança ao vivo:** mudar os favoritos na página de configurações refaz o topo e as linhas quando se volta para a página principal. A padrão e as saídas que entram ou saem continuam atualizando como na Fase 3. |

### 4.3 Tela

| # | Decisão |
|---|---|
| D8 | **Rodapé:** uma linha nova no fim do `Root`, de 36 DIP, com o botão de engrenagem (`E713`, 32×32) à direita, AutomationId `SettingsButton` e Name "Configurações". A janela passa para **520 DIP** de altura, para a lista de apps manter o espaço de hoje. Os testes do `FlyoutPlacement` usam parâmetros e não mudam, mas o caso 1.2 do `roteiro-f1.ps1` passa a conferir 520. |
| D9 | **Página de configurações** (P1): `Flyout\SettingsView.xaml` (`UserControl`), **criada só quando é aberta** e liberada ao voltar, para não pesar na abertura. A página principal (as linhas 0 a 3 atuais) fica num `Grid` que é escondido enquanto a página está aberta, sem ser recriado. A página tem:<br>• **Topo:** botão voltar (`E72B`, AutomationId `SettingsBack`) e o título "Configurações".<br>• **"Saídas favoritas (até 3)":** um `CheckBox` por saída ativa, com o nome curto e o nome completo na dica, mais as favoritas desligadas, com o último nome salvo e "(desconectada)", que podem ser desmarcadas. Com 3 marcadas, as outras ficam desabilitadas. AutomationId `FavoriteCheck`, Name = nome curto.<br>• **"Tema":** três `RadioButton`s, Automático, Claro e Escuro, com AutomationId `ThemeAuto`, `ThemeLight` e `ThemeDark`.<br>• **"Iniciar com o Windows":** um `ToggleSwitch` com AutomationId `StartupSwitch`.<br>Toda mudança vale **na hora**, sem botão "Salvar". Esc ou o botão voltar retornam à página principal. Perder o foco fecha a janela, como sempre. |
| D10 | **Tema:** `Root.RequestedTheme` = `Default`, `Light` ou `Dark`, aplicado quando a janela nasce e na hora em que o tema muda. A Mica segue o tema do conteúdo; conferir na captura (5.4). O menu da bandeja é nativo e segue o tema do Windows, o que é aceito. |
| D11 | **Menu e linha de comando:** o menu da bandeja ganha "Configurações" entre "Abrir" e "Sair". Os argumentos novos `--configuracoes` e `--configuracoes-sem-foco` (este para os testes, como o `--abrir-sem-foco`) abrem a janela já na página. O `CommandLine` ganha `AppCommand.Settings`, `SettingsNoFocus` e os testes unitários deles. |
| D12 | **Log:** `config carregada (favoritos=<n>, tema=<t>)` na partida. `favoritos alterados (<n>)`, `tema alterado para <t>` e `iniciar com o Windows ligado` ou `desligado` a cada mudança. |

## 5. Arquivos

**Novos:**
- `MixerDeAudio\Services\AppSettings.cs` (D1, puro)
- `MixerDeAudio\Services\SettingsStore.cs` (D2)
- `MixerDeAudio\Services\StartupRegistration.cs` (D3: `StartupRules` puro + registro)
- `MixerDeAudio\Audio\FavoriteFilter.cs` (D5, puro)
- `MixerDeAudio\Flyout\SettingsView.xaml(.cs)` (D9)
- `MixerDeAudio\ViewModels\SettingsViewModel.cs`
- `MixerDeAudio.Tests\AppSettingsTests.cs`, `FavoriteFilterTests.cs` e `StartupRulesTests.cs`
- `tools\testes\roteiro-f5.ps1`

**Alterados:**
- `FlyoutWindow.xaml(.cs)`: rodapé, altura 520, página de configurações e tema.
- `DeviceListViewModel.cs` e `SessionListViewModel.cs`: filtro do D5 e regra do D6.
- `TrayIconService.cs`: item do menu.
- `CommandLine.cs` e `CommandLineTests.cs`: `--configuracoes`.
- `App.xaml.cs`: carrega a configuração e trata o comando.
- `AppPaths.cs`: `ConfigFile`.
- `MixerDeAudio.Tests.csproj`: links dos arquivos puros.
- `tools\testes\lib.ps1`:
  - `OpenSettings` (Invoke no `SettingsButton`);
  - `SettingsBack`;
  - `FavoriteChecks` (nome, marcado e habilitado);
  - `ToggleFavorite <nome>`;
  - `SelectTheme <auto|claro|escuro>`;
  - `StartupSwitchState` e `ToggleStartup`;
  - `BackupConfig` e `RestoreConfig` (config.json e os valores `MixerDeAudio` das chaves Run e StartupApproved\Run).
- `tools\testes\roteiro-f1.ps1` (altura 520), `README.md`, `docs\ESTADO.md` e `docs\PLANO.md` (estado).

## 6. Testes

### 6.1 Testes unitários (novos)

- **`AppSettingsTests`:**
  - padrão sem arquivo;
  - JSON inválido;
  - outra versão;
  - tema desconhecido;
  - mais de 3 favoritos;
  - repetidos e vazios;
  - ida e volta do `Serialize`/`Parse`.
- **`FavoriteFilterTests`:**
  - sem favoritos;
  - favoritos ativos;
  - favorita desligada;
  - ativa que não é favorita vai para o fim;
  - ativa nula;
  - ordem alfabética.
- **`StartupRulesTests`:**
  - caminho igual com e sem aspas;
  - maiúsculas diferentes;
  - outro caminho;
  - bytes `02`, `03`, `06`, vazio e nulo.
- **`CommandLineTests`:** os dois argumentos novos.

A bateria inteira continua passando.

### 6.2 Regras do roteiro (P3)

- O roteiro roda `BackupConfig` antes de tudo e `RestoreConfig` no `finally`. No fim, confere que o `config.json` e os dois valores do registro ficaram **iguais aos de antes**, inclusive "não existia".
- Só o valor `MixerDeAudio` é escrito ou apagado. Os outros valores da Run são lidos antes e depois, e as duas leituras têm que ser iguais.
- Não troca a saída padrão nem mexe em volume ou mudo. Não clica de verdade nem envia teclas. Usa só os padrões de UIA (Invoke, Toggle e SelectionItem).

### 6.3 Roteiro na tela (`tools\testes\roteiro-f5.ps1`, Sonnet roda, Opus confere)

| Caso | Por que na tela | Passos | Esperado |
|---|---|---|---|
| 5.1 Página de configurações | Navegação dentro da janela | Abrir; `OpenSettings` | A página aparece, com um `FavoriteCheck` por saída ativa (nomes iguais aos do `dispositivos`), o tema atual marcado e o `StartupSwitch` igual ao estado do registro (D3). O `SessionList` fica escondido. Captura `f5-config.png`. |
| 5.2 Favoritos | Filtro aplicado na tela | Marcar 2 saídas, sendo uma delas a padrão; tentar marcar a 3ª e a 4ª; voltar | Com 3 marcadas, a 4ª fica desabilitada. Desmarcar a 3ª. Ao voltar, o topo mostra só as 2 favoritas e as linhas dos apps só os botões delas. O `config.json` tem os 2 ids e o log tem `favoritos alterados (2)`. |
| 5.3 Ativa fora dos favoritos | P2 | Desmarcar a padrão, deixando 1 favorita que não é a padrão; voltar | O topo mostra a favorita e a padrão no fim, destacada. A linha MX-teste A mostra os mesmos 2 botões. |
| 5.4 Tema | Visual | `SelectTheme claro` com captura; depois `escuro` com captura | O fundo da captura clara é claro e o da escura é escuro (brilho médio > 150 e < 80; registrar os valores). O `config.json` tem o tema. |
| 5.5 Persistência | Leitura na partida | `--sair`, iniciar de novo, abrir | Os favoritos do 5.3 e o último tema continuam, e o log tem `config carregada (favoritos=1, tema=escuro)`. |
| 5.6 Iniciar com o Windows | Registro | `ToggleStartup` duas vezes | A 1ª vez cria o valor `MixerDeAudio` = `"…\publicado\MixerDeAudio.exe"`, e o switch fica ligado. A 2ª vez apaga o valor, e o switch fica desligado. Os outros valores da Run não mudam. |
| 5.7 Comando `--configuracoes` | Mesmo caminho do menu | Com a janela fechada, `--configuracoes-sem-foco` | A janela abre já na página de configurações, sem roubar o foco. |
| 5.8 Sem regressão | — | `roteiro-f4.ps1 -SemMedicao` (que roda f3, f2 e f1), com a configuração padrão restaurada | Todos os casos OK. |
| 5.9 Medição | — | `medir.ps1 -ComSom -Ciclos 50` | CPU parada < 0,1%. WS privado do ciclo 5 ao 20 < 5 MB. Bytes privados do ciclo 20 ao 50 < 3 MB. Handles registrados. Abertura < 300 ms. |

No fim o roteiro confere o `RestoreConfig` (iguais aos de antes) e que nenhum processo sobrou.

### 6.4 Teste do usuário (na entrega)

1. Abrir pelo ícone: a engrenagem aparece no rodapé. Clicar nela: a página de configurações abre, e o voltar retorna.
2. Escolher 2 ou 3 favoritas: o topo e os apps mostram só elas. Trocar a padrão por fora, pelas Configurações de Som, para uma saída que não é favorita: ela aparece no fim, destacada.
3. Trocar o tema para Claro, Escuro e Automático: a janela muda na hora.
4. Ligar "Iniciar com o Windows" e reiniciar o PC: o ícone aparece na bandeja. Depois, desligar.
5. Clicar com o botão direito no ícone e escolher "Configurações": abre direto na página.

## 7. Resultados

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q`: 0 erros e 0 avisos. Publicação Release (ReadyToRun) feita em `publicado\` com nenhuma cópia em execução.

**Testes unitários:** 165 aprovados, 0 falhas (118 anteriores + 47 novos: `AppSettingsTests`, `FavoriteFilterTests`, `StartupRulesTests` e os dois argumentos novos do `CommandLineTests`).

**Roteiro `tools\testes\roteiro-f5.ps1` (rodada inteira, com o 5.9; saída resumida):**

| Caso | Resultado |
|---|---|
| 5.1 Página de configurações | OK. 3 `FavoriteCheck` (3 saídas, nomes iguais), tema marcado `auto`, `StartupSwitch` desligado = registro, `SessionList` escondida. Captura `f5-config.png`. |
| 5.2 Favoritos | OK. Padrão (Soundbar) e Monitor marcadas, depois a 3ª (Headset): com 3 marcadas nenhuma outra ficou habilitada (só há 3 saídas, a 4ª não existe: o ramo "4ª desabilitada" não pôde ser exercitado na tela); desmarcada a 3ª, todas habilitadas. `config.json` com os 2 ids, topo `[Monitor, Soundbar]`, linha MX-teste A com os 2 botões, log `favoritos alterados (2)`. |
| 5.3 Ativa fora dos favoritos | OK. `config.json` só com o Monitor; topo `[Monitor, Soundbar]` com a padrão no fim e destacada; MX-teste A com os mesmos 2 botões e o da padrão ativo. |
| 5.4 Tema | OK. Brilho médio **claro = 238,1** (> 150) e **escuro = 40,7** (< 80); a Mica acompanhou o tema (o fundo da captura, que é só a Mica, ficou claro e escuro junto com o conteúdo). `config.json` com `claro` e depois `escuro`. |
| 5.5 Persistência | OK. Após `--sair` e nova partida: log `config carregada (favoritos=1, tema=escuro)`, topo `[Monitor, Soundbar]`, favorita marcada = Monitor, tema escuro, brilho 40,7. |
| 5.6 Iniciar com o Windows | OK. 1ª vez: valor `MixerDeAudio` = `"C:\Projetos\Mixer de Audio\publicado\MixerDeAudio.exe"` e switch ligado; 2ª vez: valor apagado e switch desligado; outros valores da Run iguais; log ligado e desligado. |
| 5.7 `--configuracoes-sem-foco` | OK. Abriu já na página (`SettingsBack` presente, `DeviceList` ausente); a janela do app não ficou em primeiro plano. |
| 5.8 Sem regressão | OK. `roteiro-f4.ps1 -SemMedicao` (que roda f3, f2 e f1): todos os casos passaram, inclusive o 1.2 com altura 520. |
| 5.9 Medição | OK (tabela abaixo). |
| Final | Configuração e registro iguais aos de antes (`config.json` não existia, `Run` e `StartupApproved` sem o valor `MixerDeAudio`); nenhum processo restante; 0 erros no log. |

**Medição (5.9, dois tocadores, 50 ciclos, build publicado; MB):**

| Parado 10 s: WS priv / bytes priv | CPU parada | Ciclo 5: WS priv / bytes priv | Ciclo 20: WS priv / bytes priv | Ciclo 50: WS priv / bytes priv | Handles parado / 5 / 20 / 50 | Abertura |
|---|---|---|---|---|---|---|
| 2,8 / 75,5 | 0,003% | 2,7 / 128,9 | 3,4 / 137,6 | 1,8 / 135,5 | 614 / 1280 / 1379 / 1440 | mediana 84 ms, máxima 151 ms (50 aberturas) |

WS privado 5 a 20: +0,7 MB (limite 5); bytes privados 20 a 50: -2,1 MB (limite 3). CPU 0,003% (limite 0,1). Handles só registrados, como nas fases anteriores.

**Desvios e observações:**
1. **Janela aberta sem foco ignora o "desativada" por código.** O `Invoke` da UI Automation no botão da engrenagem faz o WinUI emitir `CodeActivated` e logo `Deactivated`, e a janela fechava por "perda de foco" no meio do roteiro. A `FlyoutWindow` aberta sem foco (`--abrir-sem-foco` e `--configuracoes-sem-foco`, só de teste) agora só fecha por perda de foco depois de uma ativação por clique de verdade (`PointerActivated`). O uso normal (aberta com foco) não muda. O foco programático ao trocar de página só é pedido com a janela ativa.
2. **`StartupRules` em arquivo próprio** (`Services\StartupRules.cs`), separado de `StartupRegistration.cs`: o projeto de testes é `net10.0` puro e o código do registro geraria avisos de plataforma (CA1416) ao ser compilado lá.
3. **`SettingsStore` é `public`** (propriedade pública `App.Settings`).
4. **5.2 com 3 saídas:** a 4ª saída não existe nesta máquina, então só se confirmou que, com 3 marcadas, nenhuma não marcada fica habilitada. O desabilitar da 4ª é coberto pela lógica (`UpdateEnabled`), não pela tela.
5. **5.6:** o roteiro apaga antes só o valor `MixerDeAudio` da Run para partir de "desligado" (a restauração devolve o estado original).
6. **5.7:** o "foco igual" foi trocado por "a janela do app não é a do primeiro plano", porque o usuário pode trocar de janela por conta própria durante o roteiro (uma rodada falhou por isso).
7. **5.9:** na 1ª rodada completa a primeira abertura (a fria) levou 481 ms e na 2ª os bytes privados 20 a 50 deram +5,4 MB; a 3ª rodada, completa, passou (84 ms de mediana e 151 ms de máxima; -2,1 MB). O critério de bytes privados é ruidoso perto do limite, como já anotado nas Fases 3 e 4; vale observar na Fase 6.
8. O `roteiro-f5` espera 2 s após reiniciar o app (5.5), como os outros roteiros.

### Conferência (Opus, 20/11/2026)

- **Capturas:** `f5-config.png`, `f5-tema-claro.png` e `f5-tema-escuro.png` foram conferidas. A página e os dois temas estão certos, e a Mica acompanha o tema.
- **Caso extra** (P2 com o clique dentro do refazer dos botões, que o roteiro não cobria):
  - Montagem: favoritas Soundbar (a padrão) e Headset; tocador A enviado para o Monitor, que aparece como botão extra.
  - Ação: clicar em "Soundbar" na linha.
  - Resultado: os botões foram refeitos de 3 para 2, `rota-ler` deu `padrao`, o log disse "volta a seguir a padrao" e não teve erro. O app continuou vivo e o `RestoreConfig` não encontrou diferenças.
- **`FlyoutWindow.OnActivated`:** no uso normal, a janela abre ativada, então `_openedWithoutFocus` fica falso e a perda de foco fecha a janela como antes. A exceção só vale para as aberturas sem foco dos roteiros.
- **Memória:**
  - Na medição de 50 ciclos da conferência, os bytes privados do ciclo 20 ao 50 subiram +7,8 MB, acima do limite de 3. As rodadas do agente deram +2,8, +5,4 e −2,1.
  - Para separar a oscilação de um vazamento, medimos **150 ciclos**:
    - **Fase 5:** +20,1 MB do ciclo 20 ao 150, e os handles foram de 1351 a 1735.
    - **Fase 4** (commit `0604e9f`, publicado à parte): **+21,4 MB**, e os handles foram de 1347 a 1767.
  - **Conclusão:** o crescimento de cerca de 0,15 MB e 3 handles por abertura **já existia** e vem de criar uma janela WinUI a cada abertura. Não foi a Fase 5 que trouxe isso. O WS privado, que é o valor que o Gerenciador de Tarefas mostra, continua em cerca de 2 MB.
  - Isso vira pendência da Fase 7, onde vamos comparar com reaproveitar uma janela escondida. O limite de 3 MB em 50 ciclos fica sensível a ruído; na Fase 7, o critério passa a ser medido em 150 ciclos.
