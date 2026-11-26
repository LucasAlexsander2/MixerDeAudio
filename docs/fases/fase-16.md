# Fase 16 — Segurança (Controladora PC)

Status: aprovada pelo usuário (26/11/2026), com as recomendações de P1 a P8.

## 1. Objetivo

Corrigir as brechas da análise `docs/seguranca/analise-2026-11.md` (15 brechas: 3 altas, 5 médias e 7 baixas), começando pelas que dão a chave a quem está na rede:
- **16a, pareamento e rede:** brechas 1, 2, 4, 5, 9 e 10;
- **16b, avisos e celulares:** brechas 6, 7 e 11 a 15;
- **16d, publicação:** brecha 8;
- **16c, HTTPS (brecha 3):** fica para depois de um estudo no iPhone (P5).

As duas páginas (`/` antiga e `/novo`) continuam funcionando. A página antiga continua congelada (hash da D8 da Fase 14).

## 2. Perguntas ao usuário

| # | Pergunta | Recomendação | Resposta |
|---|---|---|---|
| P1 | Confirmar no PC cada celular novo? | Sim. Com o código certo, aparece no PC uma janelinha por cima de tudo: "Permitir o iPhone a controlar este PC?", com **Permitir** e **Recusar**. Ela some em 60 s; sem resposta, conta como recusa. Enquanto isso, o celular mostra "Confirme no PC". É a proteção mais forte contra força bruta e contra quem viu o código. | |
| P2 | Janela de pareamento | O pareamento só funciona com a seção "Controle pelo celular" das Configurações aberta e até 2 min depois de fechá-la. Fora disso, o celular mostra "Abra as Configurações no PC para parear". O QR e o código ficam na mesma seção, então o uso normal não muda. | |
| P3 | Lista de celulares | As Configurações mostram os celulares pareados, cada um com o nome, o último uso e o botão **Esquecer** (que desconecta na hora). Uma chave sem uso há 30 dias expira. O limite é de 5 celulares: o 6º é recusado com o aviso "Esqueça um celular nas Configurações". O app nunca apaga um celular sozinho, a não ser pela validade. | |
| P4 | Arquivos executáveis recebidos (`.exe`, `.msi`, `.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.lnk`, `.url`, `.scr`, `.dll`, `.com`, `.pif`, `.hta`, `.jar`, `.reg`) | Recebe e acrescenta `.txt` ao nome (`setup.exe.txt`), com o aviso no balão: "renomeado por segurança". Assim nada se perde e nada abre com dois cliques. A alternativa é recusar. | |
| P5 | HTTPS (16c) | Fica **fora desta fase**. O primeiro passo é um estudo no seu iPhone (certificado próprio, WebSocket seguro, app na Tela de Início, perfil instalado), e a decisão vem depois dele. Como paliativo agora, a 16a já fecha o roubo pela internet (Host) e a 16b avisa no PC quando um celular conecta. O estudo vira uma fase curta antes ou depois da 2.0.0, como você preferir. | |
| P6 | Assinatura do executável (Azure Trusted Signing, cerca de US$ 10 por mês; tira o aviso do SmartScreen) | Não agora. Fica anotada em "Depois da 2.0". | |
| P7 | "Modo seguro" da brecha 14 (desligar entrada e tela com o PC bloqueado) | Não agora. Desta brecha, entra só o "agora" da energia com 10 s de aviso e Cancelar no PC. | |
| P8 | Versão do fim da fase | **1.5.0**, com Novidades curtas ("Mais segurança no controle pelo celular"). Ela só é publicada depois do seu teste e da sua autorização. | |

**Tarefas suas na 16d (no site do GitHub; eu explico passo a passo na hora):**
- ligar o 2FA na conta;
- criar um ruleset que protege as tags `v*`;
- pôr você como aprovador no ambiente `release`.

## 3. Levantamento (26/11/2026)

- `HandleAsync` só confere se o IP de origem é privado. Não confere o `Host` e escuta em `0.0.0.0`. `WriteAsync` não manda `X-Frame-Options` nem CSP.
- `PairingLimiter` (`RemoteRules.cs:269`) é global. O pareamento lê `IsBlocked` antes de ler o corpo e só chama `RegisterFailure` depois, o que abre a corrida da brecha 2. O código só muda no botão "Gerar novo código".
- `POST /parear` responde dentro do prazo de 10 s do cabeçalho; esperar a confirmação no PC exige um prazo próprio.
- Os roteiros pareiam pelo `127.0.0.1` com `--celular-local` e um código fixo, várias vezes por roteiro.
- Energia com `seg: 0` age na hora (`PcController`). O aviso de 60 s só existe para os agendamentos.
- `FileNameRules` não tira os caracteres de formatação Unicode, e `FileReceiver` abre o `.parcial` com `FileMode.Create`.
- O `release.yml` usa `actions/checkout@v4` e `actions/setup-dotnet@v4` por tag, sem ambiente com aprovação.

## 4. Decisões

| # | Decisão |
|---|---|
| D1 | **Host (brecha 4):** toda requisição, inclusive `/ws`, `/tela/ws`, `/arquivo` e `/parear`, só passa com `Host` igual a um IP de uma interface permitida do PC (D5), a `127.0.0.1`, `localhost` ou `[::1]`, sempre com a porta atual. O resto recebe 421 sem corpo. A regra fica em `RemoteRules.HostAllowed(host, ipsPermitidos, porta)`, pura. |
| D2 | **Limitador (brechas 2 e 10a):** `TryBegin(ip)` reserva a tentativa dentro do `lock` antes de ler o código. Os limites são por IP e globais. O bloqueio cresce (1 min, 5 min e 30 min, voltando ao começo depois de 1 h sem erro). Com o bloqueio global, o pareamento fica fechado até o usuário abrir as Configurações. Cada bloqueio gera um código novo. |
| D3 | **Janela de pareamento (P2):** `PairingWindow` com o relógio injetado: aberta enquanto as Configurações mostram a seção do celular e por 2 min depois. Fora dela, `/parear` responde 403 com `{"ok":false,"motivo":"fechado"}`. |
| D4 | **Confirmação no PC (P1):** com o código certo, o servidor pede a confirmação na thread da tela e espera até 60 s, com um prazo próprio e sem o limite de 10 s. Recusa ou falta de resposta dão 403 com `{"motivo":"recusado"}`. Um pedido por vez: outro `/parear` no meio recebe 409. Depois de cada pareamento aceito, o código muda (brecha 1c), e o QR e o código das Configurações se atualizam. |
| D5 | **Redes (brecha 5):** continua escutando em `0.0.0.0`, mas cada conexão é aceita só se o IP **local** dela for de uma interface permitida. Permitida é a interface ligada, Ethernet ou Wi-Fi, com categoria de rede **Privada** ou de domínio (`INetworkListManager` por COM) e que não seja virtual nem VPN. Virtual ou VPN: tipo `Tunnel`/`Ppp`, ou descrição com Hyper-V, vEthernet, VirtualBox, VMware, WSL, TAP, WireGuard, ZeroTier, Tailscale, Docker ou Npcap. A lista é relida em `NetworkChange`. Sem interface permitida, as Configurações mostram "Rede pública ou sem rede da casa: o celular fica desligado". O IP do QR é o de uma interface permitida. A classificação fica em lógica pura (`NetworkRules`), com teste. |
| D6 | **Cabeçalhos (brecha 9):** toda resposta leva `X-Frame-Options: DENY`. As páginas levam também uma CSP: `frame-ancestors 'none'; default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; font-src 'self'; connect-src 'self' ws://<Host>`. O `Host` já foi validado. O `'unsafe-inline'` existe só porque a página antiga, que está congelada, tem script e estilo dentro do HTML; na Fase 17 o `/` passa a ser o novo e a CSP fica mais rígida. O pareamento pelo `#c=` dentro de iframe deixa de funcionar com o `frame-ancestors`, sem mexer na página antiga. |
| D7 | **Conexões (brecha 10b e 10c):** no máximo 8 requisições abertas por IP, além das 32 de todas juntas. O `/arquivo` sem cookie válido fecha a conexão sem drenar o corpo. |
| D8 | **Modo de teste:** só com `--celular-local`, que escuta apenas em `127.0.0.1`, a confirmação é automática, a janela de pareamento fica sempre aberta e o código não muda depois do pareamento, porque os roteiros pareiam várias vezes. Os testes unitários cobrem essas regras sem o atalho. Sem `--celular-local`, nada disso vale, nem com `--teste-pc`. |
| D9 | **Janelinha do PC (`PcPromptWindow`, XAML):** pequena, por cima de tudo, no tema do app, com título, texto e dois botões. É usada pela D4 (Permitir/Recusar) e pela D13 (Cancelar). Não rouba o foco de um jogo em tela cheia: aparece sem ativar a janela e com som de aviso, e o balão também avisa. |
| D10 | **Avisos (brecha 6):** um balão para "Novo celular pareado: X", "Celular conectado: X" e "A tela do PC está sendo vista no celular". Um ícone da bandeja diferente (ponto colorido) enquanto há celular conectado, e outro enquanto a tela é transmitida. Os ícones são gerados a partir do ícone atual. |
| D11 | **Celulares (brecha 7, P3):** lista nas Configurações com nome, "último uso" e **Esquecer**, que tira a chave e fecha a sessão dela com 4000. A validade é de 30 dias sem uso (lida ao autenticar e ao abrir as Configurações) e o limite é de 5 celulares. As regras ficam em lógica pura (`PhoneListRules`), com teste. |
| D12 | **Arquivos (brechas 11 e 12, P4):** tirar os caracteres Unicode de categoria `Cf` do nome. Se a extensão estiver na lista da P4, acrescentar `.txt` e avisar no balão. O `.parcial` passa a ser aberto com `FileMode.CreateNew` e entra na conferência do nome livre. |
| D13 | **Energia "agora" (brecha 14):** `seg: 0` passa a ter 10 s de espera, com a `PcPromptWindow` mostrando "Desligando em 10 s" e **Cancelar**, e um balão. O cancelamento usa o caminho que já existe. As duas páginas mostram a contagem. |
| D14 | **`config.json` (brecha 13):** o código passa a ser guardado com DPAPI do usuário (`CryptProtectData` por P/Invoke, sem pacote novo), no campo `CodigoCelularProtegido`. O `CodigoCelular` em texto claro ainda é lido e migrado, porque é o que os roteiros gravam. |
| D15 | **Log (brecha 15):** o log não grava mais o nome do atalho aberto. |
| D16 | **Publicação (16d, brecha 8):** as actions ficam fixadas por SHA, com a versão em comentário, e o job de release passa a ter `environment: release`. Nada mais muda no fluxo. As tarefas do usuário estão na seção 2. |
| D17 | **Ordem:** a 16a vem primeiro e a 16b depois, porque as duas mexem em `RemoteServer.cs` e nas Configurações; ficar em sequência evita conflito. A 16d (só `release.yml`) corre junto com a 16a, na mesma pasta, porque não compila nada. No fim, o Opus confere tudo e roda os roteiros. Os commits são locais, e a publicação (P8) só acontece com a sua autorização. |

## 5. Partes

### 16a — Pareamento e rede (Sonnet, `medium`)

- **Arquivos:**
  - `Remote/RemoteRules.cs`: `HostAllowed`, o limitador novo, `PairingWindow` e a CSP;
  - `Remote/RemoteServer.cs`: D1 a D7, com o prazo próprio do `/parear`;
  - `Pc/NetworkRules.cs` e um leitor nativo das categorias de rede (D5);
  - `Flyout/PcPromptWindow.xaml` com o `.cs` (D9);
  - `ViewModels/SettingsViewModel.cs` e `Flyout/SettingsView.xaml`: abrir e fechar a janela de pareamento, atualizar o código e o QR, aviso de rede pública;
  - `app/js/telas/acesso.js`: os textos para "fechado", "recusado" e "Confirme no PC" (a página antiga mostra o código HTTP, como hoje).
- **Testes:**
  - `RemoteRulesTests`: Host (IP, porta errada, `localhost`, nome de domínio, IPv6, sem Host), limitador (atômico com 32 pedidos ao mesmo tempo, por IP, crescente) e janela (aberta, 2 min, fechada);
  - `NetworkRulesTests`: tipos e descrições virtuais, categoria pública e privada.

### 16b — Avisos e celulares (Sonnet, `medium`, depois da 16a)

- **Arquivos:**
  - `App.xaml.cs` e o ícone da bandeja (D10);
  - `Remote/RemoteServer.cs`: avisos, Esquecer, validade e limite;
  - `Remote/PhoneListRules.cs`;
  - `Services/AppSettings.cs` com a DPAPI (D14);
  - `ViewModels/SettingsViewModel.cs` e `Flyout/SettingsView.xaml`: a lista de celulares;
  - `Pc/PcController.cs` e `Pc/PowerTimer.cs` (D13);
  - `Pc/FileNameRules.cs` e `Pc/FileReceiver.cs` (D12);
  - o log do atalho (D15).
- **Testes:** `PhoneListRules` (validade, limite, esquecer), `FileNameRules` (`Cf`, lista de executáveis, `.txt`), energia "agora" com 10 s e migração do código (texto claro para protegido).

### 16d — Publicação (Sonnet, `low`, junto com a 16a)

- **Arquivos:** `.github/workflows/release.yml` (D16).
- **Teste:** conferir que o YAML é válido. O próximo release de verdade (P8) confere o resto.

## 6. Testes

### 6.1 Unitários

A bateria inteira (1082 hoje) passa em toda parte, mais os testes novos da seção 5.

### 6.2 Roteiros sem janela (`roteiro-f16.ps1 -Parte a|b`, rodados pelo Opus depois de cada parte)

| Caso | Por que precisa do app | Passos | Esperado |
|---|---|---|---|
| 16a.1 | Validação real do Host | `GET /`, `/novo`, `/sessao` e `/ws` com `Host: mal.exemplo:<porta>` | 421 em todos; com `Host: 127.0.0.1:<porta>`, como hoje |
| 16a.2 | Corrida do limitador no servidor real | 32 `/parear` errados ao mesmo tempo, segurando o corpo | No máximo 5 contam; o resto recebe 429 |
| 16a.3 | Cabeçalhos reais | `cabecalhos` em `/` e `/novo` | `X-Frame-Options: DENY` e a CSP da D6 |
| 16b.1 | Arquivo real | Enviar `foto‮gpj.exe` | Chega como `fotogpj.exe.txt`, sem o caractere `Cf` |
| 16b.2 | Energia com `--teste-pc` | `energia` `desligar` com `seg: 0` e depois o cancelar | Nada no log de "desligar"; o cancelamento aparece |
| 16.x | Sem regressão | `roteiro-f14`, `roteiro-f15a` e `roteiro-f15c` | Tudo OK |

Os roteiros usam `BackupConfig` e `RestoreConfig`, reabrem o app do usuário e nunca são interrompidos.

### 6.3 Conferência (Opus) e usuário

- **Opus:**
  - a `PcPromptWindow`, por UI Automation, com o app de teste sem `--celular-local`, só em `127.0.0.1` e com uma captura sem foco;
  - a lista de celulares nas Configurações;
  - o `/novo` no navegador embutido com o `servidor-teste.ps1`.
- **Usuário, no iPhone e no PC (1.5.0):**
  - U1: esquecer o iPhone nas Configurações e parear de novo pelo QR. O PC pergunta, você permite, e depois o código mostrado é outro.
  - U2: parear sem as Configurações abertas: o celular mostra "Abra as Configurações no PC".
  - U3: ao conectar, aparecem o balão e o ícone da bandeja com o ponto. Ao abrir a Tela do PC, aparece o outro ícone.
  - U4: energia "Desligar agora" mostra a janelinha de 10 s no PC. **Cancele.**
  - U5: enviar um arquivo `.exe`: ele chega como `.exe.txt`, com o aviso.
  - U6: sem regressão: `/` e `/novo` funcionam como antes, inclusive a Tela do PC.
- **Tarefas do usuário da 16d:** feitas no GitHub, com o passo a passo dado na hora.

## 7. Resultados

(preenchido na implementação e na conferência)

### Parte 16a — Sonnet, 26/11/2026

- **D1 Host:** `RemoteRules.HostAllowed` (pura) e a recusa 421 sem corpo em `HandleAsync`, logo depois de ler o cabeçalho (vale para `/ws`, `/tela/ws`, `/arquivo`, `/parear`); log de recusa no máximo a cada 10 s.
- **D2 limitador:** `PairingLimiter.TryBegin(ip)` (por IP: 5 tentativas, bloqueios de 1, 5 e 30 min, volta ao começo depois de 1 h; global: 12 tentativas em 10 min fecham até `ReopenGlobal`). A reserva vem antes de ler o corpo (`ReadRequestAsync` não lê mais o corpo do `/parear`). Cada bloqueio troca o código.
- **D3 janela:** `PairingWindow` (relógio injetado). `OpenPairing()` e `ClosePairing()` do servidor, chamados pelo `SettingsViewModel` ao criar e ao soltar. 403 com `motivo: fechado`.
- **D4 e D9 confirmação:** `PcPromptWindow.xaml(.cs)` (sem ativar, som `MessageBeep`, prazo de 60 s, resposta por `Task`), ligada em `App.AskPairing`. O `/parear` tem prazo de 65 s; 409 `ocupado`, 403 `recusado`, 429 `bloqueado`. Pareamento aceito troca o código. Recusas antes do corpo descartam até 1 KB (senão o RST apaga a resposta).
- **D5 redes:** `Pc/NetworkRules.cs` (pura), `Pc/NetworkCategories.Native.cs` (COM `netprofm` por `GeneratedComInterface`, sem pacote), `Remote/AllowedNetworks.cs` (relê no `NetworkAddressChanged` e a cada 60 s no máximo). Conexão recusada se o IP local não for permitido (loopback sempre passa). Leitura falha ou rede fora da lista do Windows vale como Privada, com uma linha no log.
- **D6 cabeçalhos:** `X-Frame-Options: DENY` em toda resposta (`WriteAsync`), CSP em `/` e `/novo` por `RemoteRules.BuildCsp(host)`. As respostas 101 de WebSocket não levam os cabeçalhos.
- **D7:** `IpRequestCounter` (8 por IP; o loopback não conta por IP, só nas 32 gerais); `/arquivo` sem cookie (401) fecha sem drenar.
- **D8:** os atalhos valem só com `localOnly` (`--celular-local`): confirmação automática, janela sempre aberta, código que não muda.
- **Configurações:** aviso de rede pública ou sem rede (`RemoteWarning`), aviso do pareamento fechado por bloqueio global, código e QR atualizados em `PairingChanged` e `NetworksChanged`; QR e endereço usam um IP permitido. Frente nova: `conexao.js` lê o `motivo`; `acesso.js` mostra "Confirme no PC" (após 0,8 s de espera); "Abra as Configurações no PC para parear", "O PC recusou" e "Muitas tentativas…". `antigo/` não foi tocado.
- **Testes:** `RemoteRulesTests` reescritos para a D1 a D3, D6 e D7 (Host, CSP, limitador por IP/global/atômico com `Parallel.For` de 32, janela, vagas por IP); `NetworkRulesTests` novo; `NetworkRules.cs` ligado ao `MixerDeAudio.Tests.csproj`. Bateria: 1153 aprovados (1082 + 71), 0 falhas.
- **Build:** `dotnet build -c Release -p:Platform=x64`: 0 avisos, 0 erros.
- **Roteiro:** `tools/testes/roteiro-f16.ps1 -Parte a` (casos 16a.1, 16a.3, 16a.2, nessa ordem; o 2 vai por último porque deixa o IP bloqueado por 1 min). `celular.cs` ganhou `hostget` e `rajada`, e o `cabecalhos` passou a trazer `X-Frame-Options` e `Content-Security-Policy` nos dois últimos campos. **Não executei roteiro nem o app.**
- **Desvios:** (1) o log de bloqueio mantém o texto "pareamento bloqueado por 60 s" (o `roteiro-f6` confere esse texto); (2) loopback fica fora do limite de 8 por IP, para o 16a.2 conseguir 32 conexões; (3) `PairingLimiter` perdeu `IsBlocked`/`RegisterFailure` (o `roteiro-f6` 6.2 continua válido: 5 erros dão 401 e o 6º, certo, dá 429).
- **Sem conferir:** a `PcPromptWindow` na tela (foco, posição, tema, som), a leitura real do `INetworkListManager` (vtables escritas pela documentação, sem rodar), a lista de interfaces com Wi-Fi/VPN reais, o ícone/balão de pareamento, a compatibilidade da CSP com a página antiga e a `/novo` no navegador (`unsafe-inline` cobre os scripts e estilos embutidos), e todos os roteiros.

## 8. Pendências

- 16c (HTTPS): o estudo no iPhone e a decisão (P5).
- Assinatura do executável (P6) e o modo seguro (P7): ficam em "Depois da 2.0".
