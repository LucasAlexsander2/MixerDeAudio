# Fase 6 — Controle pelo celular na rede de casa

**Status:** concluída (teste do usuário aprovado em 21/11/2026)
**Implementação:** Sonnet (`medium`), em duas partes: **6a** (núcleo e servidor) e depois **6b** (página do celular e configurações) · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também:
- `docs/PLANO.md`, decisão A10;
- `docs/fases/fase-5.md`, seções 4 e 7: `AppSettings`, `SettingsStore`, página de configurações, `BackupConfig` e `RestoreConfig`;
- `docs/fases/fase-4.md` e `docs/fases/fase-3.md`, seção 4.

Pasta do repositório: `C:\Projetos\Mixer de Audio`, branch `main`, último commit `2166ecc`. **Não commitar.**

## 1. Objetivo

O próprio app serve uma página para o celular, na rede de casa. A página espelha a janelinha:
- os botões das saídas, já com as favoritas;
- o volume geral e o mudo geral;
- cada app, com ícone, nome, volume, mudo e os botões de saída.

O que se mexe no celular muda o som do PC, e o que muda no PC aparece no celular, ao vivo. Para conectar, o celular lê um QR code ou digita o endereço e um código de 6 dígitos que aparecem nas Configurações. Depois disso, ele fica lembrado até o usuário gerar um código novo.

O recurso vem **desligado por padrão**. Desligado, o app fica exatamente como hoje. Ligado, ele só escuta a rede, o que custa quase nada. O áudio só é observado enquanto há um celular conectado ou a janela aberta.

Fica fora desta fase:
- acesso pela internet, que fica para depois (A10);
- mudar favoritas, tema ou "iniciar com o Windows" pelo celular, que continuam só no PC;
- o instalador e a regra de firewall feita por ele (Fase 7).

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Como o celular se conecta.** | **QR code ou código de 6 dígitos.** O QR já leva o endereço e o código. Sem o QR, abre-se o endereço mostrado (ex.: `http://192.168.0.10:8750`) e digita-se o código. Depois de parear, o celular fica lembrado por 1 ano, por um cookie. "Gerar novo código" desconecta e esquece todos os celulares. Depois de 5 códigos errados, o pareamento fica bloqueado por 60 s. |
| P2 | **Criptografia.** Com HTTPS, o celular mostraria um aviso de "site não seguro" toda vez, porque o certificado seria feito pelo próprio app. Com HTTP, alguém **no mesmo Wi-Fi** com ferramenta de captura poderia ver o código e o cookie. | **HTTP simples**, só na rede de casa. O servidor recusa endereços que não sejam da rede local e confere a origem da página. O risco se limita a alguém que já está no seu Wi-Fi, e ele só conseguiria mexer no volume. |
| P3 | **Testes automáticos.** Ligar o servidor na rede faz o Firewall do Windows abrir um aviso, o que atrapalharia um jogo. | Os roteiros ligam o servidor **só em `127.0.0.1`**, com um argumento de teste. Assim não aparece aviso nem nada fica exposto na rede. O roteiro também guarda e devolve o `config.json`, como na Fase 5, e pelo celular de teste só mexe nos tocadores `MX-teste`. O aviso do firewall e o celular de verdade ficam para o seu teste. |

## 3. Contexto do código atual

- `Flyout\FlyoutWindow.xaml.cs` é **dono do áudio**:
  - no construtor, cria `SessionTracker`, `DeviceMonitor`, `AudioSessionMonitor`, `AppRoutingService`, os ViewModels e as ligações entre eles (`MasterChanged`, `DeviceAdded` e `DeviceRemoved`);
  - no `Closed`, solta tudo em ordem.
- `AudioSessionMonitor.SetVolume` e `SetMute` gravam no Windows, registram o eco **e atualizam o `SessionTracker`**, que avisa quem está ouvindo.
- `DeviceMonitor.SetMasterVolume` e `SetMasterMute` gravam e registram o eco, mas **não** disparam o `MasterChanged`. A tela de origem já mostra o valor, mas uma segunda tela não ficaria sabendo.
- A escolha de saída de cada app (`PreferredId`) fica guardada **no `SessionRowViewModel`** (Fase 4, D6), e não num lugar comum.
- `App.xaml.cs`:
  - liga o modo de eficiência com a janela fechada e o desliga ao abrir;
  - chama o `MemoryTrimmer.Trim()` 2 s depois de fechar;
  - guarda a configuração em `App.Settings` (`SettingsStore` com `Changed`).
- `AppIconCache.GetPngAsync(exePath)` já entrega os ícones em PNG, com cache.
- `Services\AppSettings.cs`: `Versao = 1`, `Favoritos`, `NomesFavoritos` e `Tema`. O `Parse` é tolerante.
- `Flyout\SettingsView.xaml`: seções de favoritas, tema e "Iniciar com o Windows".

## 4. Decisões

### 4.1 Núcleo de áudio compartilhado (6a)

| # | Decisão |
|---|---|
| D1 | **`Audio\AudioHub.cs`**, um só por app e dono do App, assume o que hoje o `FlyoutWindow` cria: `SessionTracker`, `DeviceMonitor`, `AudioSessionMonitor` e `AppRoutingService`, com as mesmas ligações e a mesma ordem de criação e de `Dispose`.<br>**Contagem de uso:** `Acquire(string dono)` e `Release(string dono)`. O primeiro `Acquire` cria e inicia tudo. O último `Release` solta tudo e pede o trim de memória. A janela é um dono, e cada celular conectado é outro.<br>Modo de eficiência: **desligado enquanto houver algum dono**, ligado quando não sobrar nenhum.<br>Tudo isso roda na thread da tela, com as regras de thread de sempre. |
| D2 | **Comandos passam pelo hub:** `SetVolume(key, v)`, `SetMute(key, m)`, `SetMasterVolume(v)`, `SetMasterMute(m)`, `SetDefaultDevice(id)` e `SetRoute(key, id ou nulo)`.<br>Cada um grava e depois **avisa todos os consumidores**. Nos apps isso já acontece pelo `SessionTracker`. No volume geral, o hub dispara o próprio `MasterChanged` depois de gravar. Na saída de cada app, dispara `RouteChanged(key)`. Na saída padrão, o aviso continua vindo do evento do Windows (Fase 3, D10). |
| D3 | **Escolha de saída por app no hub:** a escolha é lida quando o grupo aparece no `SessionTracker` (Fase 4, D6) e fica num dicionário `key → id ou nulo`, exposto por `PreferredOf(key)`. O `SessionRowViewModel` passa a ler daí. O clique na janela chama `hub.SetRoute`. |
| D4 | **A janela vira consumidora:** o `FlyoutWindow` faz `Acquire("janela")` no construtor e `Release("janela")` no `Closed`. Ele assina os eventos do hub e cancela as assinaturas no `Closed`. Os ViewModels continuam iguais, só com as ações apontando para o hub.<br>**O comportamento da janela não muda:** o roteiro-f5 inteiro, que roda f4, f3, f2 e f1, tem que passar igual. |

### 4.2 Servidor (6a)

| # | Decisão |
|---|---|
| D5 | **Servidor próprio e pequeno, sem ASP.NET e sem `HttpListener`.** O `HttpListener` exige permissão de administrador para escutar na rede (urlacl).<br>`Remote\RemoteServer.cs` usa um `TcpListener` em `0.0.0.0:8750`; se a porta estiver ocupada, tenta até a 8759. Ele lê uma requisição HTTP/1.1 por conexão, com cabeçalhos de no máximo 8 KB e corpo de no máximo 1 KB, e responde com `Connection: close`. A exceção é o `/ws`, que faz o handshake do WebSocket (o `Sec-WebSocket-Accept` é calculado em lógica pura) e passa a conexão para `WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = 20 s })`.<br>Ligado e parado, o servidor fica só num `AcceptTcpClientAsync`, sem timer. |
| D6 | **Rotas, e qualquer outra dá 404:**<br>• `GET /`: a página, embutida como recurso no `.exe`.<br>• `POST /parear`: recebe `{"codigo":"123456"}` e, se estiver certo, responde com `Set-Cookie: mx=<chave>; Max-Age=31536000; SameSite=Strict; Path=/`.<br>• `GET /icone/<id>`: o PNG do app, onde `<id>` são 16 caracteres hexadecimais do SHA-256 da chave do grupo, para não expor caminhos. Exige cookie válido.<br>• `GET /ws`: WebSocket. Exige cookie válido e `Origin` igual ao `Host`. |
| D7 | **Segurança** (P1 e P2):<br>• **Só rede local:** a conexão é recusada se o endereço do outro lado não for privado (`10/8`, `172.16/12`, `192.168/16`, `169.254/16`, loopback, IPv6 de enlace local ou ULA).<br>• **Código:** 6 dígitos de `RandomNumberGenerator`.<br>• **Chave da sessão:** 32 bytes aleatórios em base64url. No `config.json` só fica o **SHA-256** da chave, nunca a chave.<br>• **Limite de tentativas:** 5 erros seguidos bloqueiam **todo** o pareamento por 60 s, com o tempo injetável para os testes.<br>• **Conexões:** no máximo 4 celulares ao mesmo tempo.<br>• **"Gerar novo código"** troca o código, apaga todas as chaves e fecha os WebSockets abertos.<br>• **Logs:** o código e as chaves nunca são gravados. |
| D8 | **Protocolo** (JSON pelo WebSocket, lógica pura em `Remote\RemoteProtocol.cs`):<br>• **Do PC para o celular:** `{"t":"estado", ...}` com tudo:<br>  – `saidas`: id, nome curto, glifo e se está ativa, já filtradas pela Fase 5 D5;<br>  – `geral`: se está disponível, volume de 0 a 100 e mudo;<br>  – `apps`: id do ícone, chave, nome, volume, mudo, se é do sistema, e `rotas` com id, glifo, nome e se está ativa.<br>  O estado é mandado ao conectar e depois a cada mudança, **juntando as mudanças em no máximo uma mensagem a cada 50 ms**.<br>• **Do celular para o PC:**<br>  – `{"t":"volume","app":<key>,"v":0..100}` e `{"t":"mudo","app":<key>,"m":bool}`;<br>  – `{"t":"geral","v":..}` e `{"t":"geralMudo","m":..}`;<br>  – `{"t":"padrao","id":..}`;<br>  – `{"t":"rota","app":<key>,"id":..}`. Clicar na padrão apaga a escolha, como na Fase 4, P1.<br>• **Validação:** valores fora da faixa são cortados, e chave ou id desconhecidos são ignorados. Mensagens acima de 4 KB fecham a conexão. |
| D9 | **Threads:** a rede roda no pool de threads. Comandos recebidos são **enfileirados na thread da tela** e executados pelo hub; o estado é montado na thread da tela e enviado de forma assíncrona. Cada conexão tem uma fila de saída que guarda só o estado mais recente. O hub nunca é chamado fora da thread da tela. |
| D10 | **Ciclo de vida:** cada WebSocket autenticado faz `hub.Acquire("celular:<n>")` e, ao fechar, `Release`. O servidor liga na partida se `CelularLigado` for verdadeiro, e liga ou desliga na hora quando o interruptor muda. Desligar fecha todas as conexões. |
| D11 | **Configuração:** o `AppSettings` ganha `CelularLigado` (falso), `CodigoCelular` (gerado na primeira vez que o recurso é ligado) e `ChavesCelular` (lista de `{Hash, Nome, UltimoUso}`, com no máximo 8, removendo as mais antigas). O `Nome` sai do User-Agent, como "Android" ou "iPhone". `Versao` continua 1, e o `Parse` aceita arquivos sem esses campos. |
| D12 | **Argumento de teste** (P3): `--celular-local` faz o servidor escutar só em `127.0.0.1`. Ele é usado pelos roteiros. O `CommandLine` ganha o argumento e o teste dele. |
| D13 | **Log:**<br>• `celular: servidor ligado em <ip>:<porta>` e `celular: servidor desligado`;<br>• `celular conectado (<n> no total)` e `celular desconectado (<n>)`;<br>• `celular: codigo errado de <ip>` e `celular: pareamento bloqueado por 60 s`;<br>• `celular: novo codigo gerado`;<br>• erros de rede como `ERRO celular: <msg>`. |

### 4.3 Página do celular e configurações (6b)

| # | Decisão |
|---|---|
| D14 | **`Remote\Web\index.html`:** um arquivo só, com CSS e JavaScript puros dentro, embutido no `.exe` como recurso. Sem framework e sem nada carregado de fora. O arquivo fica abaixo de 40 KB. |
| D15 | **Telas da página:**<br>• **Pareamento** (sem cookie válido): campo de 6 dígitos e botão "Conectar". Se a página foi aberta pelo QR (`/#c=123456`), o código já vem preenchido e o pareamento é automático.<br>• **Mixer:** o mesmo layout da janelinha, para celular em pé: os botões das saídas no topo, o volume geral, depois os apps com ícone, nome, botões de saída, mudo, slider e valor. Os alvos de toque têm pelo menos 44 px.<br>• **Tema:** segue o do celular (`prefers-color-scheme`).<br>• **Sem conexão:** uma faixa "Reconectando…" e uma nova tentativa a cada 2 s, e logo ao voltar para a aba (`visibilitychange`). Se o cookie for recusado, a página volta para o pareamento. |
| D16 | **Sliders no celular:** enquanto o dedo estiver no slider, envia no máximo 1 mensagem a cada 50 ms e, ao soltar, o valor final. Durante o arraste e até 500 ms depois, ignora o estado que chegar **para aquele controle**, para o slider não pular. |
| D17 | **Seção nova nas Configurações** do PC: "Controle pelo celular".<br>• Um `ToggleSwitch` com AutomationId `RemoteSwitch`.<br>• Ligado, mostra:<br>  – o endereço `http://<ip>:<porta>` (`RemoteAddress`), usando o IPv4 da placa que tem gateway padrão;<br>  – o código (`RemoteCode`);<br>  – o QR code (`RemoteQr`, 160×160) de `http://<ip>:<porta>/#c=<código>`;<br>  – o texto "N celulares conectados" (`RemoteCount`);<br>  – o botão "Gerar novo código" (`RemoteNewCode`), que desconecta todos.<br>• A página de configurações ganha um `ScrollViewer`, porque passa da altura da janela. |
| D18 | **QR code:** pacote **QRCoder 1.6.0** (MIT, só código gerenciado), com `PngByteQRCode`, que não depende de System.Drawing. A imagem é gerada só quando a seção é mostrada. Se o pacote gerar avisos de trimming ou AOT, registre na seção 7; isso será revisto na Fase 7. |

## 5. Arquivos

**6a, novos:**
- `Audio\AudioHub.cs`
- `Remote\RemoteServer.cs` (D5, D6, D9 e D10)
- `Remote\RemoteSession.cs` (uma conexão WebSocket)
- `Remote\RemoteProtocol.cs` (D8, puro)
- `Remote\RemoteRules.cs` (puro): rede privada, Origin e Host, `Sec-WebSocket-Accept`, leitura da linha e dos cabeçalhos HTTP com limites, limitador de tentativas e hash da chave
- `MixerDeAudio.Tests\RemoteProtocolTests.cs` e `RemoteRulesTests.cs`
- `tools\testes\celular.cs`: celular de teste, cliente HTTP e `ClientWebSocket`. Comandos:
  - `parear <porta> <codigo>`: imprime a chave;
  - `estado <porta> <chave>`: imprime o estado em uma linha;
  - `enviar <porta> <chave> <json>`: **recusa** comandos de `volume`, `mudo` e `rota` para apps que não sejam `MX-teste`, e recusa sempre `geral`, `geralMudo` e `padrao`;
  - `ciclos <porta> <chave> <n>`: conecta e desconecta n vezes.

**6a, alterados:**
- `FlyoutWindow.xaml.cs` (D4), `SessionListViewModel.cs` e `SessionRowViewModel.cs` (D3), `App.xaml.cs` (hub, servidor e modo de eficiência).
- `AppSettings.cs` e `AppSettingsTests.cs` (D11), `CommandLine.cs` e `CommandLineTests.cs` (D12), `MixerDeAudio.Tests.csproj`.
- `tools\testes\lib.ps1`: `Celular*` (wrappers do `celular.cs`), `LigarCelularTeste`, que grava `CelularLigado` e um código conhecido no `config.json` de teste e inicia com `--celular-local`, e a porta lida do log.
- `tools\testes\medir.ps1`: novo `-ComCelular`.

**6b, novos:**
- `Remote\Web\index.html` (D14 a D16)
- `tools\testes\roteiro-f6.ps1`

**6b, alterados:**
- `SettingsView.xaml(.cs)` e `SettingsViewModel.cs` (D17)
- `MixerDeAudio.csproj` (recurso embutido e QRCoder)
- `README.md`, `docs\ESTADO.md` e `docs\PLANO.md` (estado)

## 6. Testes

### 6.1 Testes unitários (novos)

- **`RemoteRulesTests`:**
  - endereços privados e públicos, IPv4 e IPv6;
  - Origin igual ou diferente do Host, ou ausente;
  - `Sec-WebSocket-Accept` com o exemplo da RFC 6455;
  - cabeçalhos grandes demais ou malformados;
  - limitador: 5 erros bloqueiam, liberam depois de 60 s, e um acerto zera a contagem;
  - hash da chave.
- **`RemoteProtocolTests`:**
  - montar o estado a partir de dados fixos;
  - ler cada comando;
  - valores fora da faixa;
  - tipo desconhecido;
  - JSON inválido;
  - mensagem grande.
- **`AppSettingsTests`:** os campos novos, um arquivo antigo sem eles e o limite de 8 chaves.
- **`CommandLineTests`:** `--celular-local`.
- A bateria inteira continua passando.

### 6.2 Regras do roteiro (P3)

- O `BackupConfig` e o `RestoreConfig` da Fase 5, com a comparação no fim.
- O servidor fica **só em `127.0.0.1`** (`--celular-local`), então nunca aparece o aviso do firewall.
- O celular de teste só mexe nos tocadores `MX-teste`. O roteiro confere que a padrão, o volume e mudo geral e a escolha dos apps reais ficaram iguais.
- Nada de teclas, cliques reais ou foco.

### 6.3 Roteiro da 6a (`roteiro-f6.ps1 -Parte a`)

| Caso | Por que | Passos | Esperado |
|---|---|---|---|
| 6.1 Desligado não escuta | Leveza | Iniciar com a configuração padrão | Nenhuma porta de 8750 a 8759 é aberta pelo app (`Get-NetTCPConnection`), e o log não fala do celular. |
| 6.2 Pareamento | D6, D7 | `LigarCelularTeste`; código errado 5 vezes; código certo; esperar 60 s; código certo | Os 5 erros dão 401 e o 6º dá 429 (bloqueado). Depois de 60 s, o código certo devolve o cookie. O `config.json` tem 1 hash e nenhuma chave em texto. |
| 6.3 Sem chave não entra | D6 | `/ws` e `/icone` sem cookie e com Origin errado | 401 e 403. |
| 6.4 Estado e ao vivo | D8, D9 | Tocador A; `estado` com a janela fechada; `DefinirSessao` A 0,6; janela aberta e slider de A em 40 | O estado tem A, as saídas e o geral. A mudança de fora chega ao celular em até 1 s, e a mudança feita pela janela também. O log tem `celular conectado (1)`. |
| 6.5 Comandos do celular | D2 | `enviar` volume A 30, mudo A, rota A para a outra saída e de volta para a padrão, com a janela aberta | A sessão de A fica em 0,30 e muda, e a linha da janela mostra os mesmos valores em até 1 s. A rota pelo celular aparece no destaque da janela e no `rota-ler`. Os apps reais, a padrão e o geral não mudam. |
| 6.6 Hub e janela | D1, D4 | Celular conectado; abrir e fechar a janela 5 vezes; desconectar o celular | Os monitores continuam vivos enquanto o celular está conectado, e o log mostra uma só criação dos monitores. Depois de desconectar, eles são soltos, há trim e o modo de eficiência volta. |
| 6.7 Sem regressão | D4 | `roteiro-f5.ps1 -SemMedicao`, que roda f4 a f1 | Todos os casos OK. |

### 6.4 Roteiro da 6b (`roteiro-f6.ps1 -Parte b`)

| Caso | Por que | Passos | Esperado |
|---|---|---|---|
| 6.8 Página servida | D14 | `GET /` | 200, `text/html; charset=utf-8`, < 40 KB e sem URL externa (sem `http` fora do próprio host). |
| 6.9 Seção nas Configurações | D17, tela | `LigarCelularTeste`; `--configuracoes-sem-foco`; ler a seção | O `RemoteAddress` mostra a porta certa, o `RemoteCode` é igual ao do `config.json`, o QR existe e o `RemoteCount` mostra 0. Captura `f6-config.png`. |
| 6.10 Novo código | D7 | Celular conectado; `RemoteNewCode` (Invoke) | O WebSocket do celular de teste é fechado, a chave antiga leva 401, o código mudou e o `RemoteCount` mostra 0. |
| 6.11 Medição | Leveza | `medir.ps1 -ComSom -Ciclos 50`, com o servidor ligado em 127.0.0.1 e sem celular; depois `medir.ps1 -ComCelular`, com 50 ciclos de conectar e desconectar | Parado: CPU < 0,1%, e o WS privado parado no máximo 2 MB acima do valor com o servidor desligado. Janela: os mesmos critérios da Fase 5. Celular: bytes privados do ciclo 20 ao 50 < 3 MB e handles registrados. |

O Opus confere a página no navegador embutido, em tamanho de celular, na conferência: layout, slider, mudo e uma rota de um tocador de teste.

### 6.5 Teste do usuário (na entrega)

1. Ligar "Controle pelo celular" nas Configurações. Aceitar o aviso do Firewall do Windows **só para "Redes privadas"**.
2. Com o celular no mesmo Wi-Fi, ler o QR code: a página abre e conecta sozinha. Fechar o navegador e abrir de novo pelo endereço: continua conectado, sem pedir o código.
3. Mexer no volume de um app, no mudo, no volume geral, na saída padrão e na saída de um app pelo celular: o PC muda na hora, e a janelinha aberta acompanha. O caminho contrário também: mexer no PC aparece no celular.
4. Bloquear a tela do celular, esperar 1 minuto e voltar: a página reconecta sozinha.
5. Clicar em "Gerar novo código": o celular volta para a tela de pareamento. Desligar o recurso: o endereço deixa de abrir.

## 7. Resultados

(preenchido pela implementação, uma subseção por parte)

### Parte 6a

- **Build:** 0 erros e 0 avisos (Debug e publicação Release x64 em `publicado\`).
- **Testes unitários:** 296 aprovados, 0 falhas (eram 165; novos: RemoteRulesTests, RemoteProtocolTests, campos do celular em AppSettingsTests, `--celular-local` em CommandLineTests).
- **6.1 Desligado não escuta:** OK. Nenhuma porta de 8750 a 8759 aberta, nenhuma linha `celular` no log, prioridade em repouso Idle.
- **6.2 Pareamento:** OK. Servidor só em 127.0.0.1:8750; 5 erros = 401, o 6º (certo) = 429; após 61 s o código certo devolve o cookie (43 caracteres); `config.json` com 1 hash de 64 caracteres e sem a chave; log sem código nem chave.
- **6.3 Sem chave não entra:** OK. `/ws` e `/icone` sem cookie ou com chave errada = 401; `/ws` com chave certa e Origin errado = 403; id de ícone inválido = 404; `GET /` = 200; rota desconhecida = 404.
- **6.4 Estado e ao vivo:** OK. Estado com a janela fechada traz MX-teste A, as 3 saídas e o geral; mudança de fora chegou ao celular em 148 ms e a feita pela janela em 102 ms (limite 1000); log `celular conectado (1 no total)`.
- **6.5 Comandos do celular:** OK. Volume 30, mudo e rota para outra saída e de volta à padrão refletidos na sessão, na linha e no destaque da janela e no `rota-ler`; o celular de teste recusa geral, geralMudo, padrao e app real (código 3); padrão, geral e apps reais iguais.
- **6.6 Hub e janela:** OK. 5 aberturas e fechamentos da janela com celular conectado: 1 só criação dos monitores, 0 liberações; mudança de fora continuou chegando (126 ms); ao desconectar, monitores liberados, prioridade volta a Idle e o conjunto de trabalho cai (trim).
- **6.7 Sem regressão:** OK. `roteiro-f5.ps1 -SemMedicao` (roda f4, f3, f2 e f1): todos os casos passaram (9 OK no f5, 8 no f4). Erros no log de 6.1 a 6.6: 0.
- **Memória (`medir.ps1 -ComSom -Ciclos 50`):** servidor desligado: CPU 0,007%, WS privado parado 2,8 MB, crescimento 5 a 20 de -0,8 MB, bytes privados 20 a 50 de -0,7 MB. Servidor ligado sem celular (`-ComServidor`): CPU 0,003%, WS privado parado 2,9 MB (+0,1 MB), -1,1 MB e -0,4 MB. Com celular (`-ComCelular`, 50 conexões e desconexões): CPU 0,007%, bytes privados 20 a 50 de +0,4 MB, handles 746/757/750 (estáveis). Todos dentro dos critérios.
- **Desvios:**
  - `EmbeddedResource` do `index.html` provisório (mínimo, só mostra o JSON) foi para o csproj já na 6a; a 6b o substitui.
  - `medir.ps1` ganhou também `-ComServidor` (servidor ligado sem celular), para o critério "parado" do 6.11; `-ComCelular` e `-ComServidor` guardam e devolvem o `config.json`.
  - Cookie com `HttpOnly` a mais que o texto do D6; `POST /parear` também confere o Origin quando ele vem.
  - `celular.cs` ganhou `get` e `segurar` (checar 401/403 e manter conexão para medir a latência); `parear` imprime `status|chave`.
  - Logs novos: `audio: monitores criados`, `audio: monitores liberados` e `celular: pareado (<nome>)`.
  - Modo de eficiência e trim seguem o evento de "hub sem dono", não só o fechamento da janela.
  - Janela aberta com o hub já ativo usa a escolha de saída já lida pelo hub (não relê do Windows).

**Conferência 6a (Opus, 21/11/2026):** 296 testes aprovados de novo. Conferidos: rede privada (inclui IPv4 mapeado), Origin no `/ws` obrigatório, código com `FixedTimeEquals`, limites de cabeçalho (8 KB), corpo, mensagem, 32 requisições e 4 celulares; chaves limitadas a 8 em `NormalizePhones`; sessão liga/desliga do hub na thread da tela e solta o dono no `finally`; refcount do hub com donos únicos. Sem pendências.

### Parte 6b

- **Build:** 0 erros e 0 avisos (Debug e publicação Release x64 em `publicado\`, com `publicado\MixerDeAudio.exe --sair` antes). O QRCoder 1.6.0 não gerou aviso nenhum (nem de trimming nem de AOT); a Fase 7 ainda deve rever o pacote ao ligar o trimming.
- **Testes unitários:** 298 aprovados, 0 falhas (eram 296; novos: `EnderecoEUrlDoQr` e `PaginaDoCelular_TemMenosDe40KbESemUrlExterna` em `RemoteRulesTests`).
- **6.8 Página servida:** OK. `GET /` = 200, `text/html; charset=utf-8`, 11670 bytes, 0 URLs externas, e é a página final.
- **6.9 Seção nas Configurações:** OK. Interruptor ligado, `RemoteAddress` = `http://192.168.1.14:8750` (porta certa), `RemoteCode` igual ao do `config.json`, `RemoteQr` existe (160x160) e `RemoteCount` = "0 celulares conectados". Captura em `%TEMP%\mixer-check\f6-config.png`.
- **6.10 Novo código:** OK. Com 1 celular conectado a tela mostrou "1 celular conectado"; `RemoteNewCode` (Invoke) fechou o WebSocket do celular de teste, a chave antiga deu 401, o código mudou (6 dígitos), o `config.json` ficou sem chaves, o `RemoteCode` da tela acompanhou e o `RemoteCount` voltou a 0.
- **6.11 Medição (`medir.ps1 -ComSom -Ciclos 50`):** OK. Servidor desligado: CPU 0,007%, WS privado parado 2,9 MB. Servidor ligado sem celular: CPU 0,003%, WS privado parado 2,9 MB (diferença 0,0 MB), WS privado 5 a 20 de -0,7 MB, bytes privados 20 a 50 de -1,0 MB, abertura máxima da janela 154 ms. Com celular (50 conexões): CPU 0%, bytes privados 20 a 50 de +0,3 MB, handles 687/749/752/742 (estáveis). Observação: na rodada-base (servidor desligado) os bytes privados 20 a 50 deram +4,7 MB nesta execução (ruído do WinUI; o limite do 6.11 vale para as rodadas com servidor e celular, e a 5.9 já registrou valores próximos de 3 MB).
- **Final do roteiro:** `config.json` e registro (Run e StartupApproved) iguais aos de antes; erros no log de 6.8 a 6.10: 0; nenhum processo sobrando. `roteiro-f6.ps1 -Parte b` completo: "Todos os casos passaram".
- **Tamanhos:** `index.html` 11670 bytes (limite 40 KB). `publicado\`: 295.107.705 bytes antes e 295.510.199 depois (+0,4 MB, o `QRCoder.dll`).
- **Desvios:**
  - Dois testes unitários e dois métodos puros novos em `RemoteRules` (`BuildAddress`, `BuildPairingUrl`) para o endereço e a URL do QR; o segundo teste confere o tamanho e a ausência de URL externa do `index.html` real.
  - `SettingsView` e `SettingsViewModel` recebem o `RemoteServer` por parâmetro (`App.Remote`, novo acessor estático interno); a seção inteira fica oculta se ele for nulo.
  - O `ScrollViewer` da página de configurações já existia desde a Fase 5; nada a acrescentar.
  - O endereço mostrado usa o IPv4 da placa com gateway mesmo com `--celular-local` (o servidor só escuta em 127.0.0.1 nesse modo), como diz o documento.
  - Como o cookie é `HttpOnly`, a página não lê o cookie: se o WebSocket nunca abre e `GET /` responde, ela volta ao pareamento; se `GET /` também falha, mostra "Reconectando…".
  - Não houve conferência da página no navegador nem toque (fica com o Opus).

**Conferência 6b (Opus, 21/11/2026):** página aberta no navegador embutido em 375×812 com `--celular-local`. Pareamento pelo link `#c=` funcionou e foi direto ao mixer; volume 25 e mudo enviados pela página chegaram ao tocador MX-teste A; volume 70 e "som" mudados pelo PC apareceram na página; tema claro e escuro seguem o celular. 0 erros no log; app, tocador e `config.json` (não existia antes) deixados como estavam. Observações sem conserto agora: os botões de saída por app quebram em 2 linhas com 3 saídas (a fileira do topo cabe); trocar só o `#c=` numa página já aberta não pareia (o QR abre página nova, então não afeta o uso).
