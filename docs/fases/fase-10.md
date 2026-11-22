# Fase 10 — Tela do PC fluida e em tela cheia

**Status:** implementada e conferida em 22/11/2026 (10a e 10b), aguardando teste do usuário (meta de CPU da Econômica não atingida por 0,02 ponto, ver seção 7; análise aprovada pelo usuário em 22/11/2026; P1 como recomendado; P2 respondida: iPhone; escopo D11–D14 acrescentado pelo usuário em 22/11/2026, parte 10b em implementação)
**Implementação:** Sonnet (`medium`), numa parte só · **Análise e conferência:** Opus · **Documentação:** Haiku

Documento autossuficiente. Leia também `docs/fases/fase-9.md`: D17 a D20 (ver a tela), D29 (leveza), seção 6.2 (regras do roteiro) e a subseção "Parte 9b" da seção 7.

Repositório: `C:\Projetos\Mixer de Audio`, branch `main`, último commit `dedb8a9`. **Não commitar. Não rodar git.**

## 1. Objetivo

Pedido do usuário depois do teste da Fase 9: a transmissão da tela "trava um pouco". Ele quer mais qualidade e a opção de ver a imagem em tela cheia no celular.

A transmissão continua opcional: nada é enviado antes de tocar em "Mostrar a tela" (Fase 9, D18).

Fica fora: som do PC no celular, controle por gestos além do toque, e captura pela GPU (DXGI Desktop Duplication, ver P1).

## 2. Pendências do usuário

| # | Pendência | Recomendação |
|---|---|---|
| P1 | **Fluidez × peso no PC.** Cada quadro custa cerca de 22 ms de CPU (captura GDI e JPEG em 1280 px, medido na 9b). | **Três qualidades na página:**<br>• **Econômica:** 2 qps, 960 px, para quando estiver jogando;<br>• **Normal (padrão):** até 8 qps, 1280 px;<br>• **Alta:** até 12 qps, 1920 px.<br>A página lembra a escolha. **Quadros iguais ao anterior não são enviados**, então uma tela parada quase não gasta CPU nem rede. A captura pela GPU seria mais leve em 15 qps ou mais, mas é bem mais código (Direct3D). Fica para depois, se a Normal não bastar. |
| P2 | **Seu celular é Android ou iPhone?** | **Android (Chrome):** tela cheia de verdade, girando para deitado sozinho.<br>**iPhone (Safari):** o Safari não deixa uma página entrar em tela cheia, só vídeos. Lá a imagem ocupa a página inteira, mas as barras do Safari continuam. Para ficar sem barras, use "Adicionar à Tela de Início" e abra pelo ícone: a página já vai preparada para isso.<br>Os dois caminhos são feitos; a resposta só muda o que você testa.<br>**Resposta (22/11/2026): iPhone.** A conferência prioriza o caminho sem `requestFullscreen`. |

## 3. O que foi levantado (22/11/2026)

Por que trava hoje (`index.html`, função `telaNext`, e `ScreenCapture`):
- A página espera **500 ms depois de mostrar cada quadro** antes de pedir o próximo, e o servidor impõe **250 ms** entre quadros. O resultado são **no máximo cerca de 2 qps**, de propósito (Fase 9, D17: "taxa baixa").
- Cada quadro é **uma conexão HTTP nova** (`Connection: close`). Pelo Wi-Fi, isso soma a abertura da conexão em todo quadro.
- A largura pedida é `min(largura da página, 560) × densidade`, cerca de 1000 px no celular em pé: deitado ou em tela cheia, a imagem fica esticada.
- JPEG com qualidade 0,6, limite de 1280 px (`ScreenRules.MaxWidth`).
- A troca de imagem usa `<img>` com URL de blob: o navegador decodifica na thread principal e pode piscar.

## 4. Decisões

| # | Decisão |
|---|---|
| D1 | **Canal próprio para a imagem:** WebSocket `GET /tela/ws`, com as mesmas regras do `/tela` (cookie da chave do celular conectado agora, senão 409; grupo `tela` ligado, senão 403; `Origin` igual ao `Host`) e no máximo um por vez. A página manda `{"mon":i,"w":largura,"q":"eco\|normal\|alta"}` para começar e para mudar. O servidor responde com quadros **binários** (JPEG). Separado do WebSocket de controle, para a imagem não atrasar o mouse. A rota HTTP `/tela` sai; o `celular.cs` passa a usar o canal novo. |
| D2 | **Controle de fluxo:** no máximo **um quadro em trânsito**. A página manda `{"ok":1}` depois de desenhar, e só então o servidor captura o próximo, respeitando o intervalo mínimo da qualidade. Assim, num Wi-Fi lento a taxa cai sozinha, sem acumular atraso. Laço numa tarefa do pool de threads, só enquanto o canal está aberto. |
| D3 | **Qualidades (P1)**, tabela pura em `ScreenRules`: `eco` 500 ms, 960 px, JPEG 0,5; `normal` 125 ms, 1280 px, 0,7; `alta` 83 ms, 1920 px, 0,8. `MaxWidth` sobe para 1920. A largura pedida pela página é o **lado maior** da área visível × densidade, para o deitado e a tela cheia ficarem nítidos. O servidor corta pela qualidade e pela tela. |
| D4 | **Quadro igual não vai:** depois da captura, o DIB é comparado com o anterior (`Span.SequenceEqual`, vetorizado: cerca de 1 ms para 1920×1080). Igual: nada é codificado nem enviado e o laço tenta de novo depois do intervalo. A cópia anterior só existe com o canal aberto e é solta ao fechar. O cursor entra no desenho, então mexer o mouse gera quadro novo. |
| D5 | **Buffers reaproveitados durante a transmissão:** DIB, cópia anterior e `SoftwareBitmap` ficam do tamanho atual enquanto o canal está aberto (hoje são criados e soltos a cada quadro). Mudou a tela ou a largura, eles são refeitos. Ao fechar, tudo é solto e o trim da 9b (10 s sem quadro) continua. |
| D6 | **Desenho na página:** `createImageBitmap(blob)` e `drawImage` num `<canvas>`, com `bitmap.close()` depois. A decodificação sai da thread principal e a imagem não pisca. Sem `createImageBitmap`, a página volta ao `<img>`. O `{"ok":1}` sai depois de desenhar. |
| D7 | **Tela cheia (P2):** botão "Tela cheia" na barra da imagem.<br>• Com `requestFullscreen` (Android): pede tela cheia do bloco da imagem e `screen.orientation.lock('landscape')` (falha ignorada).<br>• Sem ele (iPhone): o bloco vira `position:fixed` cobrindo a página toda (`100dvh`), sem abas nem títulos, com a dica "Gire o celular". A página ganha `<meta name="apple-mobile-web-app-capable" content="yes">` e `viewport-fit=cover`, para abrir sem barras pela Tela de Início.<br>• **Em tela cheia:** a imagem ocupa o máximo mantendo a proporção. Uma barra flutuante e translúcida (Sair, Clique, Direito, Teclado, qualidade) some em 3 s e volta com um toque. "Teclado" abre o campo de texto da aba Controle (grupo `entrada`). Sair da tela cheia (botão, voltar do Android ou Esc) volta ao layout normal, sem parar a transmissão. |
| D8 | **Zoom com dois dedos**, só em tela cheia: pinça aproxima até 4×, arrastar com dois dedos move a imagem e um toque duplo volta ao normal (útil no monitor ultralargo de 3440 px). O toque com um dedo continua levando o mouse ao ponto (Fase 9, D19), com a conta considerando zoom, deslocamento e as faixas pretas da proporção. A conta do ponto fica numa função JS só, conferida pelo Opus no navegador. |
| D9 | **Log e diagnóstico:** ao encerrar, `celular: tela encerrada (<n> quadros, <m> iguais, <ms> ms por quadro, <qualidade>)`, sem conteúdo. Fora isso, o log fica como na 9b. |
| D10 | **Leveza:** sem canal aberto, nada roda, como hoje. Com a tela parada, o custo é só a captura e a comparação a cada intervalo. Nenhum pacote NuGet novo. |
| D11 | **Parte 10b — celular lembrado.** Rota `GET /sessao`: 204 com cookie válido (chave conhecida), 401 sem cookie ou com chave desconhecida. **Não** exige o celular conectado agora (sem 409). A página só mostra o pareamento quando `/sessao` (ou `/parear`) responde 401. Qualquer outra falha (sem resposta, 5xx, WebSocket caído, "ocupado") fica em "Reconectando…" com novas tentativas (espera crescente até ~5 s). Regra pura em C# para a decisão "parear × reconectar" (testável), espelhada no JS. Motivo: hoje uma falha passageira (desbloquear o celular, Wi-Fi voltando) leva ao pareamento e o usuário cria chave nova (o `config.json` do usuário tem 3 chaves "iPhone"). |
| D12 | **Porta fixa.** `AppSettings` ganha `PortaCelular` (padrão 8750; fora de 8750–8759 vira 8750; arquivo antigo sem o campo vira 8750). Na partida, tenta a porta salva por até 10 s (uma tentativa por segundo) antes de passar à próxima da faixa. Se ligar em outra, salva a nova. `ExclusiveAddressUse = true` continua (segurança). Motivo: ao reiniciar logo depois de fechar, a 8750 fica em TIME_WAIT e o app pulava para a 8751, quebrando o endereço salvo no celular. |
| D13 | **Dicas nas Configurações**, abaixo do endereço do celular: "Para o endereço não mudar, reserve o IP deste PC no roteador (DHCP)." e "No iPhone, o atalho da Tela de Início precisa ser pareado uma vez." |
| D15 | **Parte 10c — menu flutuante** (pedido do usuário em 22/11/2026: "layout mais simples, sem mexer muito a mão"). A barra de abas do rodapé (`#tabs`) sai. No lugar entra **uma bolinha** de 56 px, `position:fixed`, presa a uma borda lateral, mostrando o ícone da aba atual. Só aparece com algum grupo de ferramentas ligado, como a barra de hoje, e fica escondida em tela cheia (Fase 10, D7). O conteúdo ganha a altura que a barra ocupava. |
| D16 | **Abrir e escolher:**<br>• **Apertar a bolinha** abre o menu na hora: as opções (Som, PC, Janelas, Controle, Tela, Enviar; só as liberadas) saem dela em leque, num arco virado para o centro da tela. Cada opção é um círculo de 52 px com ícone SVG embutido e o nome embaixo; a atual fica destacada.<br>• **Apertar e arrastar:** com o dedo ainda na tela, arrastar até uma opção a destaca (cresce um pouco); **soltar em cima dela escolhe**. Soltar fora de qualquer opção fecha o menu.<br>• **Toque rápido** (soltar sem mover): o menu fica aberto, e um toque numa opção escolhe. Tocar fora fecha.<br>• Conta de "qual opção está sob o dedo" e posições do arco numa função JS pura dentro da página. |
| D17 | **Mover a bolinha:** segurar parada por 600 ms fecha o menu e "levanta" a bolinha (aumenta e ganha sombra; `navigator.vibrate(15)` onde houver). Arrastando, ela segue o dedo para qualquer lugar; ao soltar, desliza com animação até a borda lateral mais próxima, mantendo a altura (sem passar das áreas seguras do iPhone, `env(safe-area-inset-*)`). Lado e altura (fração da tela) ficam no `localStorage`, com `try/catch`; sem ele, o padrão é a direita, a 70% da altura. A bolinha fica com opacidade 0,55 depois de 3 s sem uso e volta a 1 ao tocar. |
| D18 | **Animações suaves:** só `transform` e `opacity` (com aceleração da placa de vídeo). As opções abrem com escala de 0,3 para 1 e um leve atraso entre elas (25 ms), em 220 ms, com `cubic-bezier(.2,.9,.3,1.15)`. Fecham em 150 ms. A troca de aba faz o conteúdo novo entrar com fade e deslocamento de 12 px em 200 ms. A bolinha desliza até a borda em 250 ms. Com `prefers-reduced-motion`, tudo vira troca imediata. Fundo atrás do menu aberto: véu escuro com fade, que fecha ao tocar. |
| D19 | **Sem regressão:** os gestos da bolinha não podem disparar o touchpad (aba Controle) nem o toque na imagem (aba Tela): `pointerdown` na bolinha faz `preventDefault`, e a bolinha e o véu ficam acima do resto (`z-index`). `index.html` continua abaixo de 64 KB e sem URL externa. |
| D14 | **Cookie sem mudança:** já dura 1 ano e é a forma de guardar que o Safari não apaga em 7 dias. Não usar `localStorage` para a chave. |

## 5. Arquivos

- Alterados:
  - `Pc\ScreenRules.cs` (qualidades, largura, intervalo) e `ScreenRulesTests.cs`;
  - `Pc\ScreenCapture.cs` (buffers reaproveitados, comparação, qualidade por quadro);
  - `Remote\RemoteServer.cs` (`/tela/ws` no lugar do `/tela`);
  - `Remote\Web\index.html` (canal, canvas, qualidades, tela cheia, zoom, metas);
  - `tools\testes\celular.cs` (`tela` pelo canal novo: conta quadros, mede tamanho e dimensões, sem gravar imagem);
  - `tools\testes\roteiro-f10.ps1` (novo), `tools\testes\README.md`;
  - `docs\ESTADO.md` e `docs\PLANO.md` (linha da Fase 10).
- Novo, se a lógica de laço e controle de fluxo couber numa regra pura: `Pc\ScreenStreamRules.cs` e testes.

## 6. Testes

### 6.1 Unitários (novos)

- Tabela de qualidades: id desconhecido vira `normal`, intervalo, largura e JPEG de cada uma.
- Largura final: lado maior × densidade, corte pela qualidade e pela tela, mínimo de 160.
- Decisão do laço (pura): pode capturar só com o `ok` recebido e o intervalo vencido; quadro igual não envia; mudar a tela ou a largura refaz os buffers.
- Comparação de buffers do mesmo tamanho e de tamanhos diferentes.
- `PortaCelular`: padrão, faixa (fora de 8750–8759 vira 8750) e arquivo antigo sem o campo.
- Regra "parear × reconectar": só 401 de `/sessao`/`/parear` leva ao pareamento; sem resposta, 5xx, WS caído e ocupado reconectam.
- A bateria inteira continua passando.

### 6.2 Regras do roteiro

As mesmas da Fase 9, seção 6.2:
- `--teste-oculto --celular-local --teste-pc`;
- `BackupConfig` e `RestoreConfig`;
- **nunca matar um roteiro no meio**;
- nenhuma tecla, clique ou ação real no PC;
- a captura é real, só leitura, e **nenhuma imagem é gravada em disco**;
- o app do usuário é reaberto ao fim.

### 6.3 Roteiro (`roteiro-f10.ps1`)

| Caso | Por que no roteiro | Passos | Resultado esperado |
|---|---|---|---|
| 10.1 Canal e fluxo | Laço real com captura e WebSocket. | Tela ligada; canal `normal`, w 1280, por 10 s, com `ok` a cada quadro; depois 10 s **sem** mandar `ok`. | Quadros JPEG com 1280 px de largura e altura proporcional. Com `ok`: quadros iguais pulados e **nunca mais de 8 qps**. Sem `ok`: no máximo 1 quadro a mais. A linha `tela encerrada` traz quadros, iguais e ms por quadro. |
| 10.2 Troca de qualidade | Buffers refeitos no meio. | No mesmo canal: `alta` w 3000; `eco` w 3000; mon 1. | Larguras 1920 (ou a da tela, se menor), 960 e a da tela 2. Sem `ERRO` no log. |
| 10.3 Regras | Mesmas da 9b, na rota nova. | Sem cookie; chave de outro celular; `ToolScreen` desligado; segundo canal aberto junto. | 401, 409, 403; o segundo canal é recusado e o primeiro continua. |
| 10.4 Leveza | Custo real por qualidade. | 30 s em cada qualidade, mandando `ok`; depois 60 s parado. | CPU média registrada em cada qualidade (metas: eco < 3%, normal < 15%, alta < 30% de um núcleo). Bytes privados, 60 s depois de parar, no máximo 5 MB acima de antes; handles sem crescer mais de 10. |

| 10.5 Celular lembrado (10b) | Rota nova e porta real. | `/sessao` com chave boa, sem chave e com chave desconhecida; com o celular de teste conectado, parar o app de teste e religá-lo logo em seguida (sem restaurar o config entre os dois), com o `celular.cs` reconectando. | 204, 401, 401. A porta depois de religar é a mesma de antes (log `servidor ligado em`). O `celular.cs` volta a conectar com a mesma chave, sem parear. |

O Opus confere no navegador embutido, em 375×812 e deitado (812×375):
- tela cheia pelo caminho sem `requestFullscreen`;
- a barra flutuante;
- o zoom com o ponto do mouse certo nos cantos (`teste-pc: entrada mouseAbs`);
- sair da tela cheia sem parar a transmissão.
- (10b) derrubar o servidor com a página aberta: ela mostra "Reconectando…" e volta sozinha, sem pedir código.

### 6.4 Teste do usuário

1. Ver a tela em Normal com um vídeo tocando no PC: fluido, sem travar.
2. Trocar para Alta e Econômica e sentir a diferença.
3. Tela cheia, girar o celular, pinça para aproximar, tocar para levar o mouse e clicar pela barra.
4. iPhone: abrir pela Tela de Início e conferir que fica sem as barras do Safari.
5. Fechar o Safari, bloquear o celular, reiniciar o app no PC e abrir de novo: entra direto, sem código.

## 7. Resultados

**Arquivos:**
- `Pc\ScreenRules.cs`: `ScreenQuality`, tabela `Eco`/`Normal`/`Alta`, `QualityFor`, `MaxWidth` 1920, `FrameSize(..., maxWidth)`; saíram `ParseQuery` e `MinFrameInterval`.
- `Pc\ScreenStreamRules.cs` (novo): `Parse` das mensagens do canal, `CanCapture`, `RemainingDelay`, `SameFrame`, `Decide`, `NeedsRebuild`.
- `Pc\ScreenCapture.cs`: um objeto por canal; DIB, DC e cópia anterior (`_prev`) reaproveitados e refeitos só se mudar tela ou largura; quadro igual não é codificado; `Dispose` solta tudo, compacta e faz o trim.
- `Remote\RemoteServer.cs`: `/tela/ws` no lugar de `/tela` (`HandleScreenSocketAsync`, laço de recebimento e de envio, um canal por vez via `_screenBusy`); log `tela compartilhada` e `tela encerrada (n quadros, m iguais, x ms por quadro, qualidade)`.
- `Remote\Web\index.html` (53 KB): canal, `<canvas>` com `createImageBitmap` (volta ao `<img>`), três qualidades lembradas em `localStorage`, tela cheia (nativa ou `position:fixed`), barra flutuante que some em 3 s, campo de texto na tela cheia, pinça, arraste e toque duplo, metas `apple-mobile-web-app-capable`.
- `tools\testes`: `celular.cs` (`tela` pelo canal, `get` com upgrade em `/tela/ws`), `lib.ps1` (`CelularTela`, `CelularTelaBg`, `CelularTelaArquivo`), `roteiro-f10.ps1` (novo), `README.md`. `docs\ESTADO.md` e `docs\PLANO.md` atualizados.
- Testes: `ScreenRulesTests.cs` (ajustado) e `ScreenStreamRulesTests.cs` (novo); `MixerDeAudio.Tests.csproj` ganhou o link do `ScreenStreamRules.cs`.

**Build:** `dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q`: 0 avisos, 0 erros. O `dotnet publish` Release só mostra os mesmos dois `IL2104` do SDK (`Microsoft.Windows.SDK.NET`, `WinRT.Runtime`); nenhum aviso novo de trim. **Testes unitários:** 786 aprovados, 0 falhas (eram 757; +29).

**Roteiro** (`roteiro-f10.ps1`, 208 s, primeiro plano, `--teste-oculto --celular-local --teste-pc`, 0 linhas `ERRO`, nenhuma imagem em disco, `config.json` restaurado igual, app do usuário reaberto). Telas do PC: 1920x1080 e 3440x1440.
- **10.1 OK:** Normal w 1280, 10 s com `ok`: 77 quadros (7,7 qps; máximo 8), JPEG 1280x720, média 99.945 bytes; 10 s sem `ok`: 1 quadro. Log: `78 quadros, 0 iguais, 27 ms por quadro, normal` (a tela do PC mudava a todo quadro, por isso 0 iguais; o pulo de quadro igual só tem teste unitário).
- **10.2 OK:** Alta w 3000 deu 1920x1080; Eco w 3000 deu 960x540; mon 1 deu 1280x536 (tela 3440x1440).
- **10.3 OK:** sem cookie 401; outro celular 409; `ToolScreen` desligado 403; `Origin` errado 403; segundo canal recusado (503) e o primeiro seguiu (61 quadros, sem fechamento).
- **10.4 FALHA só na meta de CPU da Econômica** (30 s com `ok`, % de um núcleo): **Eco 3,02%** (meta < 3%; 60 quadros, 2 qps; na primeira rodada deu 2,90%), **Normal 14,53%** (meta < 15%; 7,7 qps), **Alta 20,93%** (meta < 30%; 11,6 qps). Cerca de 15 ms de CPU por quadro (captura GDI `HALFTONE` e JPEG) com a tela mudando a cada quadro; as metas de Normal e Alta passam com folga pequena na Normal. Bytes privados: antes 118,5 MB, depois de 60 s parado 111,4 MB (diferença -7,1 MB; máximo 5); handles 1468 para 1424 (máximo 10). Pico durante a transmissão: 136 MB (Eco), 188 MB (Normal), 185 MB (Alta).

**Desvios do documento:**
1. **Memória (achado na medição):** com `SetPixelData` o codificador do Windows guardava uma cópia nativa de 8 MB por quadro até o coletor rodar: pico de 340 a 600 MB privados em Alta e 1,1 GB num teste isolado. Agora cada quadro vai por um `SoftwareBitmap` criado e solto (`using`) a cada quadro (experimento: pico de 72 MB). O `SoftwareBitmap` **reaproveitado** do D5 falhou com "Acesso negado" no `CopyFromBuffer` e foi abandonado; o resto do D5 (DIB, DC e cópia anterior reaproveitados) está como no documento.
2. O trim (compactar e devolver memória) roda ao **fechar o canal**, não mais por timer de 10 s sem quadro (com tela parada o canal fica aberto sem quadros). `ScreenRules.IdleEnd` ficou sem uso.
3. A página não consegue ler o status HTTP de um WebSocket recusado; se o canal fecha sem quadros ela faz um `GET /tela/ws` comum, que repete os testes 401, 409 e 403 antes do 400, para mostrar a mensagem certa. Depois do upgrade, o servidor fecha com 4003 (grupo desligado ou outro celular) e 4004 (captura falhou ou tela inexistente).
4. A largura pedida é o lado maior da área visível × densidade em tela cheia, e a largura do palco × densidade no layout normal; o zoom não pede mais largura.
5. "Teclado" da tela cheia abre um campo de texto próprio dentro do bloco (a aba Controle fica escondida na tela cheia nativa); envia o mesmo `{"t":"texto"}`.
6. Os casos 9.8 e 9.9 do `roteiro-f9.ps1 -Parte b` ficaram obsoletos (a rota `/tela` saiu).

**Pendências:** meta de CPU da Econômica (3,02% contra 3%); conferência no navegador embutido (375x812 e 812x375): tela cheia sem `requestFullscreen`, barra flutuante, zoom (a conta é `tlPoint(cx,cy,sw,sh,iw,ih,z,tx,ty)`, exposta como `window.tlPoint`; conferida em Node nos cantos, com e sem zoom), sair sem parar a transmissão; teste do usuário (6.4).

### Parte 10b (celular lembrado)

**Arquivos:**
- `Remote\RemoteRules.cs`: `FirstPort`/`LastPort`, `NormalizePort`, `PortOrder` (porta salva primeiro), `PageAction` + `DecidePage(status)` (só 401 pareia) e `ReconnectDelayMs` (0,5 s dobrando até 5 s).
- `Services\AppSettings.cs`: `PortaCelular` (padrão 8750; fora da faixa ou ausente vira 8750).
- `Remote\RemoteServer.cs`: rota `GET /sessao` (204 com chave conhecida, 401 sem cookie ou chave desconhecida; sem 409); `Start` tenta a porta salva uma vez e, se ocupada, `StartRetryAsync` repete 1 vez por segundo até 10 tentativas (~10 s) e só então percorre o resto da faixa; ao ligar em outra porta salva a nova. `ExclusiveAddressUse = true` mantido. Evento `StateChanged` para a tela de Configurações atualizar o endereço.
- `Remote\Web\index.html`: sem abrir o WebSocket, consulta `/sessao`; só 401 mostra o pareamento. Qualquer outra resposta ou falha de rede fica em "Reconectando…" com espera crescente (`att`, zera ao abrir). Antes usava `GET /`, que sempre dava 200 e levava ao pareamento.
- `ViewModels\SettingsViewModel.cs` e `Flyout\SettingsView.xaml`: dicas D13 (uma linha) e refresh quando o servidor liga depois da espera.
- `tools\testes`: `celular.cs` ganhou `reconectar` (mesma porta e chave, espera crescente, consulta `/sessao`, nunca chama `/parear`); `roteiro-f10.ps1` ganhou o caso 10.5 e o parâmetro `-Caso 10.5` (roda só ele).
- Testes: `SessionPortRulesTests.cs` (novo).

**Build:** Debug x64, 0 avisos, 0 erros; publish Release sem avisos além dos `IL2104`. **Testes unitários:** 809 aprovados, 0 falhas (eram 786; +23).

**Roteiro `-Caso 10.5`** (12 s, `--teste-oculto --celular-local --teste-pc`, 0 `ERRO` no log, `config.json` restaurado igual, nenhuma imagem em disco):
- `/sessao`: chave boa 204; outro celular pareado com o A conectado 204; sem cookie 401; chave desconhecida 401.
- Parar e religar o app logo em seguida: porta antes 8750, depois 8750; servidor ligou em 202 ms (nessa rodada o TIME_WAIT não apareceu, 0 linhas "porta ocupada"). `celular.exe reconectar` voltou com a mesma chave (CONECTOU, CAIU, SESSAO, CONECTOU), sem `PAREAR`, sem novo `celular: pareado` e com as mesmas chaves no config.
- **10.5b (acréscimo):** como o TIME_WAIT não é garantido, o roteiro ocupa a 8750 por 3 s com um `TcpListener` exclusivo e liga o app: 1 linha "porta 8750 ocupada, tentando por ate 10 s", servidor ligado em 3258 ms na 8750, celular reconectou.

**Desvios:** (1) a espera pela porta não bloqueia a tela: a primeira tentativa é síncrona e as demais rodam em `async` (não atrasa a abertura do app nem o tráfego da bandeja; só o servidor do celular fica fora do ar, e as Configurações mostram "Servidor não iniciou…" até o `StateChanged`). (2) 10.5b não estava no documento. (3) A parte 10b exigiu publicar em `publicado\` (app do usuário fechado com `--sair` e reaberto depois).

### Parte 10c (menu flutuante)

**Arquivo:** só `Remote\Web\index.html` (CSS, HTML e JS puros, sem recurso externo).
- A barra `#tabs` deixou de ser barra: `<nav id="tabs">` virou o contêiner (tela cheia, sem eventos) dos 6 botões `data-tab`, agora círculos de 52 px com SVG embutido e nome embaixo. Entram `#fab` (bolinha de 56 px com o ícone da aba atual, copiado em `tabBtn()`), `#veil` (véu escuro) e `#fabp` (sonda das áreas seguras). `body.hastabs` saiu.
- Escolher uma opção chama `fabChoose(k)`: `tab=k; tabBtn(); view(); send({t:'atualizar'})` (igual ao clique antigo), mais a classe `.ent` no conteúdo novo (fade e 12 px, 200 ms).
- D16: apertar abre na hora; arrastar até uma opção a destaca (`.hv`, escala 1,2) e soltar escolhe; soltar fora fecha; toque rápido deixa aberto (toque numa opção escolhe; véu, bolinha ou Esc fecham).
- D17: segurar parado 600 ms fecha o menu e levanta a bolinha (`.lift`, `vibrate(15)`); arrastar move; ao soltar vai à borda mais próxima (250 ms), lado e altura em `localStorage['mx.fab']` = `{"s":"l|r","y":fração}` (padrão direita, 0,70); opacidade 0,55 (`.idle`) após 3 s.
- D18: só `transform` e `opacity`; abre em 220 ms com `cubic-bezier(.2,.9,.3,1.15)` e 25 ms de atraso entre opções; fecha em 150 ms; `prefers-reduced-motion` zera transições e animação.
- D19: `pointerdown` da bolinha faz `preventDefault`/`stopPropagation` com captura do ponteiro; z-index 60 (véu), 61 (opções), 62 (bolinha); `body.tlfs` esconde os três.
- Funções puras expostas: `window.fabArc(cx,cy,W,H,n,R,top,bot)` (posições em leque, tenta raios maiores e giros do arco até caber) e `window.fabPick(pts,x,y)` (raio de 40 px).

**Tamanho:** `index.html` com 60.524 bytes (limite 65.536). **Build:** Debug x64, 0 avisos, 0 erros. **Testes:** 809 aprovados, 0 falhas (inclui o do tamanho e das URLs externas). `node --check` do script: ok. `fabArc` conferida em Node em portrait e paisagem nos cantos: tudo dentro da tela e opções separadas por 55 px ou mais.

**Desvios:** o arco é um leque que gira e aumenta o raio (120, 150, 190 px) quando a bolinha está num canto, para caber; em paisagem muito baixa, no canto de baixo, o nome das opções pode ficar parcialmente cortado. Não conferido no navegador nem publicado (fica com o revisor).

### Conferência (Opus, 22/11/2026)

- **Build e testes:** 0 avisos, 0 erros; 809 testes aprovados. O publish Release tem só os dois `IL2104` antigos.
- **Riscos no código:**
  - O laço e o controle de fluxo estão corretos: um crédito, sem acúmulo, o intervalo é respeitado também no quadro igual e a ordem 401/409/403 está certa.
  - Os buffers são soltos no `finally` depois de as duas tarefas terminarem.
  - Ajustes do Opus:
    - `TryBind` solta o `TcpListener` que falhou;
    - `RemoteServer.IsStarting` foi criado;
    - durante a espera pela porta, as Configurações mostram "Ligando o servidor… (esperando a porta ficar livre)" no lugar de "Servidor não iniciou".
- **Navegador embutido** (`--teste-oculto --celular-local --teste-pc`, grupos `tela` e `entrada`), em 375×812 e 812×375, sem `requestFullscreen` (o caminho do iPhone): tudo OK.
  - Tela cheia: o bloco fica `fixed` e cobre a página, com a dica "Gire o celular".
  - Barra flutuante: some em cerca de 3 s e volta com um toque. Esse toque também leva o mouse ao ponto, como previsto no D8.
  - `mouseAbs` deitado, sem zoom: cantos de baixo em cerca de (4, 1075) e (1916, 1075) px da tela 1.
  - Com zoom de 3× no centro: (570, 360) e (1349, 719) px, iguais à conta esperada. O toque duplo volta a 1×.
  - Sair da tela cheia não para a transmissão: o canvas continuou atualizando.
- **10b na página:** religuei o servidor com a página aberta. A página mostrou "Reconectando…", voltou sozinha, sem pedir código, e a porta continuou 8750. A transmissão da tela para com o religamento ("Tela indisponível."), e basta tocar de novo em "Mostrar a tela".
- **Meta de CPU da Econômica:** 3,02% contra < 3%, medida com a tela mudando em todo quadro. Aceito, porque com a tela parada os quadros iguais nem são codificados.

### Conferência da Parte 10c (Opus, 22/11/2026)
- Build sem avisos e 809 testes aprovados; `index.html` abaixo de 64 KB.
- **Navegador embutido, 375×812, `--teste-pc`, 8 grupos:**
  - a bolinha começa à direita, a 70% da altura;
  - toque rápido abre o menu em leque (6 opções em arco em volta da bolinha) e um toque em "PC" escolhe e fecha;
  - **apertar na bolinha, arrastar até "Janelas" e soltar escolhe**;
  - os gestos não geraram nenhuma linha `teste-pc: entrada` (o touchpad não foi acionado);
  - `config.json` igual (`diferencas=[]`).
- **Corrigido:** na primeira exibição, a bolinha deslizava a partir do canto de cima à esquerda; agora é posta no lugar sem animação.
- **Não verificado aqui:** as animações (o painel do navegador estava em segundo plano, onde as transições não rodam) e o segurar 600 ms para mover. Ficam para o teste do usuário no iPhone.
- Publicado em `publicado\` e app do usuário reaberto.
