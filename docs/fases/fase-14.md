# Fase 14 — Base do novo front (Controladora PC)

Status: concluída em 26/11/2026. A 1.3.0 foi publicada; os ajustes A1 a A5 vão na versão do fim da Fase 15 (decisão do usuário: publicar só no fim), e o reteste no iPhone foi aprovado junto com o da Fase 15 (1.4.0, 26/11/2026).

## 1. Objetivo

Preparar o terreno das partes da Fase 15 sem mexer no que o usuário usa hoje:

1. Dividir o `index.html` (63 KB) em partes por área, **com a página `/` idêntica byte a byte**.
2. Criar a rota `/novo` com a base do layout novo: tokens do guia (Fase 13), pilha de telas com voltar arrastando para a esquerda, seta do cabeçalho e voltar do navegador/Android pelo histórico (A4), já conectando e pareando com o PC.
3. PWA: manifesto, ícone e abrir em tela cheia, sem service worker (Q6).
4. Fontes Manrope (400–700) e JetBrains Mono (500) embutidas, em WOFF2 só latino, com a licença OFL, servidas com cache longo e sem internet (A5).

As telas de cada área ficam para as partes da Fase 15; aqui elas são provisórias ("Chega na Fase 15").

## 2. Perguntas ao usuário

| # | Pergunta | Recomendação | Resposta |
|---|---|---|---|
| P1 | Download das fontes e licenças (só na implementação, uma vez; o app nunca baixa nada) | Aprovar estes 4 arquivos: **Manrope** variável 400–700, subconjunto `latin` (cerca de 25 KB) e **JetBrains Mono** 500 `latin` (cerca de 20 KB), ambas do Google Fonts (`fonts.googleapis.com/css2` → `fonts.gstatic.com`); e os textos **OFL.txt** de cada uma (cerca de 4 KB cada) do repositório oficial `github.com/google/fonts` (`ofl/manrope` e `ofl/jetbrainsmono`). | |
| P2 | Ícone do app na tela inicial (o guia não definiu) | Fundo `#0f1218` e uma grade 2 × 2 de blocos arredondados com traço `#8fb4ff`, o de cima à esquerda preenchido (o "painel de blocos"), com margem de 20% para o Android poder recortar. Gerado por script, sem arquivo de fora. | |
| P3 | Versão para o teste no iPhone | **1.3.0 publicada** pelo Velopack (tag `v1.3.0`, depois da autorização do commit), com uma linha nas Novidades: "Prévia da Controladora PC em `http://<PC>:8750/novo`". Testa também o empacotamento dos recursos novos. Alternativa: só um build local em `publicado\`. | |

## 3. Levantamento (25/11/2026)

- O servidor (`Remote/RemoteServer.cs`) é próprio: uma requisição por conexão, `Connection: close`, até 32 abertas. `GET /` lê o recurso `MixerDeAudio.Web.index.html`; `WriteAsync` sempre manda `Cache-Control: no-store`.
- A página é um único IIFE com cerca de 40 variáveis compartilhadas (`S`, `ws`, `screen`, `tab`, `hold`…) e funções que se chamam entre áreas. Já tem comentários-marco `/* ---------- … ---------- */` separando as áreas.
- Converter isso em ES modules exigiria trocar todas as variáveis compartilhadas por um objeto e reordenar o que roda na carga; partir em `<script>` clássicos jogaria tudo no escopo global, onde `screen` colide com `window.screen`. Não há testes de JS, e a página antiga sai na Fase 17.
- O teste unitário roda em `dotnet test` (local e no `release.yml`), com a lógica pura ligada por `<Compile Include … Link>`. O repositório usa `core.autocrlf=true` (a página está em CRLF).
- O ícone `/icone/` hoje manda dois `Cache-Control` (o `no-store` fixo e o `max-age=3600`), e o navegador fica com o `no-store`. Fica como está (ver Pendências).

## 4. Decisões

| # | Decisão |
|---|---|
| D1 | Rotas: `GET /` (antiga), `GET /novo` e `GET /novo/` (nova), `GET /app/*` (arquivos do novo front) e `GET /fontes/*`. `/ws`, `/tela/ws`, `/sessao`, `/parear`, `/icone/` e `/arquivo` não mudam. |
| D2 | `Remote/WebAssets.cs`, lógica pura ligada aos testes: `Resolve(path)` devolve `(recurso, tipo, cache)` ou nulo. Aceita só `/app/` e `/fontes/` seguidos de `[a-z0-9._/-]`, sem `..`, sem `//`, sem `\`, sem `%`, terminando numa extensão da D4. O recurso é `MixerDeAudio.Web/` + o caminho sem a barra inicial. Pasta `antigo/` nunca é rota. Arquivo inexistente: 404. |
| D3 | `MixerDeAudio.csproj`: um curinga `Remote\Web\**\*` com `LogicalName="MixerDeAudio.Web/$([System.String]::Copy('%(RecursiveDir)').Replace('\','/'))%(Filename)%(Extension)"`. O recurso `MixerDeAudio.Web.index.html` sai. Se a função de propriedade não funcionar, listar cada arquivo com o mesmo padrão de nome (o caso 14.2 do roteiro pega o erro). |
| D4 | Tipos: `.html` `text/html; charset=utf-8`; `.js` `text/javascript; charset=utf-8` (obrigatório para módulo com `nosniff`); `.css` `text/css; charset=utf-8`; `.webmanifest` `application/manifest+json`; `.png` `image/png`; `.svg` `image/svg+xml`; `.woff2` `font/woff2`; `.txt` `text/plain; charset=utf-8`. Outras: não servidas. |
| D5 | Cache: páginas (`/` e `/novo`) `no-store`, como hoje. `/app/*` `no-cache` com `ETag` (16 hex do SHA-256 do arquivo, calculado uma vez por arquivo e guardado num dicionário) e resposta 304 a `If-None-Match` igual: muda a cada fase sem ficar velho no celular. `/fontes/*` `public, max-age=31536000, immutable`: o arquivo nunca muda; se mudar, muda o nome (`-1`, `-2`). |
| D6 | `WriteAsync` ganha o parâmetro do `Cache-Control` (padrão `no-store`, então as respostas de hoje saem iguais). 304 sai sem corpo, sem `Content-Type` e sem `Content-Length`; `Reason(304)` = `Not Modified`. `WebAssets.IfNoneMatchMatches(header, etag)` trata lista, `W/` e `*`. |
| D7 | **Página antiga em partes:** `Remote/Web/antigo/` com 18 arquivos, cortados exatamente nas linhas-marco (lista na seção 5), sem mudar nenhum byte, em CRLF. `GET /` concatena as partes na ordem de `WebAssets.OldPageParts` e serve com o mesmo tipo e cabeçalhos de hoje. O `index.html` sai. |
| D8 | Garantia de que `/` não regride: o teste unitário lê as partes do disco na ordem de `OldPageParts`, tira o `\r` e exige SHA-256 `ffff7d3dbdef971270c97731b09a759fb2b389b63149a032cc56ced9784a0d8b` (a página do commit dc58f67, conferida nesta análise); outro teste exige que a pasta `antigo/` e a lista sejam iguais. O roteiro 14.1 confere o mesmo hash no que o app publicado serve. A página antiga fica congelada até a Fase 17; qualquer mudança nela troca o hash de propósito. |
| D9 | Por que não ES modules na antiga: ver o levantamento. As partes servem para ler e portar uma área por vez nas partes da Fase 15 (o agente lê 5 KB em vez de 63 KB). |
| D10 | O novo front usa **ES modules nativos** (`<script type="module">`, Safari 11+ e Chrome), sem build, sem biblioteca. No HTML os caminhos são absolutos (`/app/js/app.js`); entre módulos, relativos (`./pilha.js`). Assim a Fase 17 só troca a rota da página. |
| D11 | Estado e conexão do novo front (`conexao.js`, `estado.js`) portados das partes `05-conexao` e `06-pareamento`, com as mesmas regras: `/sessao` 401 → pareamento, resto → "Reconectando…" (espelha `DecidePage`); espera 0,5–5 s (espelha `ReconnectDelayMs`); 4000/4001 → desconectado; 4009/`ocupado` → ocupado; reconectar ao voltar a ficar visível. O cookie (`Path=/`) é o mesmo da página antiga; o QR continua apontando para `/`. |
| D12 | Pilha (`pilha.js`): telas registradas por nome. `abrir(n)` empilha e faz `history.pushState({n: profundidade, tela})`; seta e gesto chamam `history.back()`; `popstate` concilia a pilha com `state.n` (desempilha ou, no "avançar", reabre `state.tela`). Ao carregar com `history.state.n > 0`, `history.go(-n)` e começa no Início. O Início não tem voltar. Transição de 0,28 s como no protótipo; sem transição com "reduzir movimento" (D7 da Fase 13). |
| D13 | Gesto (A4): na pilha, começa depois de 8 px, só para a esquerda com `|dx| > 1,2·|dy|`; volta com 30% da largura ou `v < −0,5 px/ms` e 30 px; senão a tela volta ao lugar. Ignora toques em `input`, `textarea`, `select` e em qualquer elemento dentro de `[data-sem-gesto]` (as partes da Fase 15 marcam faixas, barras, touchpad e imagem); aviso ou folha aberta bloqueia. Telas com `touch-action: pan-y`. |
| D14 | Dica "Arraste para a esquerda para voltar" na primeira tela interna, uma vez só (guardado no `localStorage`, com `try/catch`; sem ele aparece uma vez por carregamento). |
| D15 | PWA: `/app/manifest.webmanifest` com `name` "Controladora PC", `short_name` "Controladora", `start_url` `/novo`, `scope` `/`, `display` `standalone`, `background_color` e `theme_color` `#0f1218`, ícones 192 e 512 (`any maskable`). No HTML: `apple-touch-icon` 180, `apple-mobile-web-app-capable`, `apple-mobile-web-app-status-bar-style` `black-translucent`, `apple-mobile-web-app-title`, `theme-color`, `color-scheme: dark`, `viewport-fit=cover`. Sem service worker. Limite conhecido: no Android sem HTTPS o atalho abre como aba do Chrome. |
| D16 | Ícone (P2): `tools/icone/icone.cs` (`dotnet run`, `System.Drawing.Common`) gera `app/icone-180.png`, `-192` e `-512`. Os PNGs entram no repositório; o script só roda de novo se o ícone mudar. |
| D17 | Fontes (P1): `fontes/manrope-latin-1.woff2` (variável, `font-weight: 400 700`), `fontes/jetbrainsmono-500-latin-1.woff2`, `fontes/OFL-Manrope.txt`, `fontes/OFL-JetBrainsMono.txt`. `@font-face` com `font-display: swap` e o `unicode-range` do subconjunto latino; `<link rel="preload">` da Manrope. Reservas do guia (4.2). `.gitattributes` marca `*.woff2` e `*.png` como binários. |
| D18 | Nada da internet: nenhum arquivo de `app/` cita `http://` ou `https://` (só o `xmlns` do SVG). Teste unitário garante. |
| D19 | Telas provisórias do `/novo`: acesso (parear com 6 dígitos, ocupado, desconectado, faixa "Reconectando…"); Início com "Todas as funções" em blocos (grade de 3) e Desconectar (com aviso de confirmação, D8 da Fase 13); uma tela interna provisória por área. Áreas e grupo (`estado.ferr`): Som (sempre), Tela do PC (`tela`), Controle (`entrada`), Energia (`energia`), Monitores (`monitores`), Janelas (`janelas`), Atalhos (`atalhos`), Enviar (`transferencia`), Timer de sono e Estado do PC (`estado`), a mesma regra da página antiga. |
| D20 | Componentes em `app/js/componentes/`, um arquivo por componente, conforme o guia 4.5. A 14b cria os que usa (cabeçalho, bloco, botão, aviso de confirmação, toast e dica); a 14c cria os demais e fecha todos. Ícones SVG copiados do objeto `IC` do protótipo para `app/js/icones.js` (a 14c completa o conjunto da 4.4). Cores só por tokens em `tokens.css` (D2 da Fase 13); teste garante que nenhum outro arquivo de `app/` tem cor fixa. |
| D22 | **Componentes fechados (pedido do usuário em 25/11/2026):** as partes 15a a 15d usam os componentes em paralelo e não os alteram; mudança num componente vai para a conferência. Cada componente tem uma interface fixa (`export function criarX(opções)` devolvendo o elemento e os métodos), descrita em `app/js/componentes/LEIAME.md`; o CSS de cada um fica em `css/componentes.css`. |
| D23 | Lista fechada na 14c: bloco (e faixa com rolagem para o lado e grade), botão grande, botão, cartão, cartão de app (avatar com mudo, nome, música, botãozinho de saída, barra, Anterior/Pausa/Próxima), barra (slider), chip e grupo de chips, interruptor, medidor, aviso de confirmação, folha de opções, toast, dica e touchpad (em pé e o painel de vidro deitado, que arrasta e esconde). Áreas marcam o que não aceita o gesto de voltar com `data-sem-gesto` (a barra, a faixa e o touchpad já vêm marcados). |
| D24 | Barra: mesma regra da página antiga (`07-sliders.js`): no máximo 1 envio a cada 50 ms (configurável), valor final ao soltar e o estado vindo do PC ignorado por 500 ms depois de soltar. Interface: `aoMudar(valor, final)` e `definir(valor)`, que respeita a espera. |
| D25 | Touchpad: só os gestos, sem protocolo (ligar ao PC fica na 15c). Mesmos limites da página antiga (`16-controle.js`): um dedo move (somado e entregue uma vez por quadro), toque curto = clique esquerdo, toque com dois dedos = direito, dois dedos na vertical ou a faixa lateral = rolar, botões Esquerdo/Direito na base (segurar = arrastar). Interface: `aoMover(dx, dy)`, `aoClicar(botão)`, `aoRolar(dy)`, `aoSegurar(botão, ligado)`. |
| D26 | Galeria: tela escondida em `/novo#galeria`, que abre sem precisar do PC e mostra cada componente com as variantes e uma linha com o último evento (valor da barra, gesto do touchpad). Serve à conferência e ao teste no iPhone; sai na Fase 17. |
| D21 | Leveza no PC: nada de timer novo; os arquivos são lidos do recurso a cada pedido (como hoje) e só os ETags ficam na memória. |

## 5. Partes e arquivos

A fase tem três partes, uma depois da outra (a 14b usa as rotas da 14a; a 14c, a base da 14b). Cada parte é de um agente Sonnet com esforço `medium` (servidor e histórico são os pontos delicados).

### 14a — Servidor, página antiga em partes e fontes

- **Novos:**
  - `Remote/WebAssets.cs` (D2, D4 a D8): `Resolve`, `ContentTypeFor`, `ETagFor(bytes)`, `IfNoneMatchMatches`, `OldPageParts`;
  - `Remote/Web/antigo/`, cortes no `index.html` atual, cada parte começando na sua linha-marco: `01-inicio.html` (linhas 1–12, até `<style>`), `02-estilo.css` (13–117), `03-corpo.html` (`</style>` até `<script>`, 118–291), `04-base.js` (292–325), `05-conexao.js` (326), `06-pareamento.js` (374), `07-sliders.js` (393), `08-som.js` (414, "desenho"), `09-pc.js` (495), `10-menu.js` (525), `11-brilho.js` (655), `12-estado-pc.js` (678), `13-enviar.js` (699), `14-sono.js` (734), `15-janelas.js` (745), `16-controle.js` (792), `17-tela.js` (864, com tela cheia e toque), `18-partida.js` (1117 até o fim). Os números de linha são o guia; o critério é o hash da D8;
  - `Remote/Web/fontes/` (D17), baixados como em P1;
  - `.gitattributes` (D17);
  - `MixerDeAudio.Tests/WebAssetsTests.cs`;
  - `tools/testes/roteiro-f14.ps1`.
- **Alterados:**
  - `Remote/RemoteServer.cs`: `ServePageAsync` monta as partes; nova `ServeAssetAsync` (D1, D5, D6); `WriteAsync` com o parâmetro de cache;
  - `MixerDeAudio.csproj` (D3);
  - `MixerDeAudio.Tests/MixerDeAudio.Tests.csproj`: liga `WebAssets.cs`;
  - `tools/testes/celular.cs`: comando `cabecalhos <porta> <caminho> [etag|-]` que imprime `status|content-type|cache-control|etag|sha256-do-corpo|bytes`;
  - `tools/testes/README.md`: o roteiro e o comando novos.
- **Removido:** `Remote/Web/index.html`.

### 14b — O front `/novo`

- **Novos** em `Remote/Web/app/`:
  - `index.html` (D15, D10);
  - `manifest.webmanifest` e `icone-180.png`, `icone-192.png`, `icone-512.png` (D15, D16);
  - `css/tokens.css` (cores da 4.1, `@font-face`, fontes e espaços da 4.2 e 4.3) e `css/base.css` (componentes da D20, telas da pilha, reduzir movimento, bordas seguras);
  - `js/app.js` (entrada), `js/conexao.js`, `js/estado.js` (último estado e `aoMudar(fn)`), `js/pilha.js`, `js/icones.js`;
  - `js/componentes/` com cabeçalho, bloco, botão, aviso, toast e dica (D20, D22) e `css/componentes.css`;
  - `js/telas/acesso.js`, `js/telas/inicio.js`, `js/telas/provisoria.js` (D19).
- **Novos fora:** `tools/icone/icone.cs` (D16); `MixerDeAudio.Tests/WebFrontTests.cs`.

### 14c — Componentes compartilhados (pedido do usuário em 25/11/2026)

- **Novos:** os demais componentes da D23 em `app/js/componentes/`, `app/js/componentes/LEIAME.md` (interface de cada um, D22) e `app/js/telas/galeria.js` (D26).
- **Alterados:** `css/componentes.css`, `js/icones.js` (conjunto completo da 4.4), `js/app.js` (abre a Galeria com `#galeria`) e os componentes da 14b, só para seguir a mesma interface.
- **Referências:** visual e medidas do protótipo e do guia 4.5; regras de envio de `antigo/07-sliders.js` e dos gestos de `antigo/16-controle.js`.

## 6. Testes

### 6.1 Unitários (toda a bateria, 904 hoje)

`WebAssetsTests` (14a):
- `Resolve`: `/app/js/pilha.js` → `text/javascript`, `no-cache`; `/fontes/x-1.woff2` → `font/woff2`, `immutable`; `/app/manifest.webmanifest` → `application/manifest+json`; recusa `/app/../x.js`, `/app//x.js`, `/app/%2e%2e/x.js`, `/app/x\y.js`, `/app/X.JS`, `/app/x.exe`, `/app/`, `/antigo/04-base.js`, `/fontes`, caminho vazio.
- `ETagFor`: o mesmo conteúdo dá o mesmo ETag, conteúdos diferentes dão ETags diferentes, formato `"` + 16 hex + `"`. `IfNoneMatchMatches`: igual, lista com espaços, `W/"…"`, `*`, ausente e diferente.
- Página antiga (D8): hash das partes concatenadas e lista igual à pasta.
- Disco: todo arquivo em `Web/app` e `Web/fontes` passa no `Resolve`; as fontes começam com `wOF2`; cada licença existe.

`WebFrontTests` (14b), lendo os arquivos do repositório (sobe pastas até achar `MixerDeAudio.slnx`):
- todo `import … from './x.js'` dos módulos e todo `src`/`href` absoluto do `index.html` e do manifesto aponta para arquivo que existe;
- o manifesto é JSON válido com os campos da D15, e cada ícone tem no PNG (cabeçalho IHDR) o tamanho declarado;
- `tokens.css` tem os 13 tokens da 4.1 com os valores do guia; nenhum outro arquivo de `app/` tem `#rgb`, `#rrggbb` ou `rgb(` (D20);
- nenhum `http://` ou `https://` em `app/` fora do `xmlns` do SVG (D18);
- (14c) cada componente da D23 tem o seu arquivo e o `export function criar…` citado no `LEIAME.md`, e todo `export` de `componentes/` aparece no `LEIAME.md`.

### 6.2 Roteiro sem janela (`roteiro-f14.ps1`, `--teste-oculto --celular-local`, depois da 14a e de novo depois da 14b)

| Caso | Por que precisa do app | Passos | Esperado |
|---|---|---|---|
| 14.1 | Só o executável publicado mostra o que foi embutido de verdade | `cabecalhos /` | 200, `text/html`, `no-store`, SHA-256 (sem `\r`) igual ao da D8 |
| 14.2 | Idem: nomes dos recursos (D3) e cabeçalhos reais | `cabecalhos` em `/novo` e em cada arquivo de `Web/app` e `Web/fontes`; e em `/app/../x.js`, `/antigo/04-base.js`, `/app/nao-existe.js` | Tipos e cache da D4/D5; os três últimos 404 |
| 14.3 | O 304 só existe no servidor de verdade | `cabecalhos /app/js/app.js <etag>` com o ETag do caso anterior | 304 sem corpo |
| 14.4 | Sem regressão | `CelularParear` e `CelularEstado` | Pareia e recebe o estado |

Usa `BackupConfig` e `RestoreConfig`, reabre o app do usuário no fim e nunca deve ser interrompido.

### 6.3 Conferência (Opus) e usuário

- **Opus, no navegador embutido, com o app de teste em 127.0.0.1:** pareia no `/novo`; o Início mostra só as áreas dos grupos ligados; abrir uma área e voltar pela seta, por `history.back()` (o voltar do Android) e pelo "avançar"; `document.fonts.check` confirma Manrope e JetBrains Mono; a lista de rede não tem nenhum pedido fora do PC; `/` continua abrindo como antes.
- **Usuário, no iPhone** (4 casos curtos, só o que depende do aparelho):
  - U1, sem regressão: abrir `/` como sempre, mexer num volume e trocar de aba.
  - U2: abrir `/novo`: a letra é a Manrope e aparecem os blocos das áreas ligadas.
  - U3: abrir uma área e voltar arrastando para a esquerda, pela seta e pelo gesto de voltar do Safari (borda esquerda).
  - U4: "Adicionar à Tela de Início": o ícone aparece, o app abre em tela cheia sem a barra do Safari, a barra de status fica escura sem cobrir o conteúdo, e o voltar arrastando funciona.
  - U5 (14c): na Galeria, a barra arrasta sem pular, o touchpad move e o toque vira clique na linha de eventos, a rolagem da página não briga com a barra nem com o touchpad, e deitado o painel de vidro arrasta e esconde.
- **Opus, na Galeria (14c):** arrastar a barra por 1 s gera no máximo cerca de 20 eventos e o último tem `final`; `definir` logo depois de soltar é ignorado por 500 ms; os componentes conferem com o guia em tamanho e cor (uma captura).

## 7. Resultados

### Parte 14a — Sonnet, 25/11/2026

- Feito: `Remote/WebAssets.cs` (Resolve, ContentTypeFor, ETagFor, IfNoneMatchMatches, OldPageParts); `RemoteServer.cs` (`ServePageAsync` concatena as partes, `ServeNewPageAsync` em `/novo` e `/novo/`, `ServeAssetAsync` com ETag em memória e 304, `WriteAsync` com parâmetro de cache, `Reason(304)`); `index.html` cortado em 18 partes em `Remote/Web/antigo/` e removido; fontes, licenças e `LEIAME.txt` em `Remote/Web/fontes/`; `.gitattributes`; `WebAssetsTests`; `roteiro-f14.ps1`; comando `cabecalhos` no `celular.cs`; README dos testes.
- Página antiga: concatenação das 18 partes igual byte a byte ao original (63.070 bytes) e SHA-256 sem `\r` = `ffff7d3d...0d8b`, conferidos antes de apagar o `index.html`. Cortes nas linhas da seção 5, sem ajuste.
- Recursos (D3): a função de propriedade funcionou; nomes reais `MixerDeAudio.Web/antigo/01-inicio.html` ... `18-partida.js` e `MixerDeAudio.Web/fontes/manrope-latin-1.woff2` etc. (comprovado pelo roteiro: 200 em `/` e nas fontes).
- Fontes: `manrope-latin-1.woff2` 24.532 bytes; `jetbrainsmono-500-latin-1.woff2` 21.832 bytes; ambas começam com `wOF2`; OFL-Manrope.txt 4.387 e OFL-JetBrainsMono.txt 4.399 bytes. `unicode-range` e origem em `fontes/LEIAME.txt`.
- Build Release x64: 0 erros, 0 avisos. Testes: 923/923 (904 + 19 novos).
- Roteiro `roteiro-f14.ps1` (`--teste-oculto --celular-local`, 8 s, 0 falhas, config restaurado sem diferenças): 14.1 OK (200, text/html, no-store, hash igual); 14.2 OK (2 fontes 200 com tipo, `immutable` e ETag; 3 nomes com maiúscula 404; `/app/../x.js`, `/antigo/04-base.js`, `/app/nao-existe.js`, `/app/`, `/app/%2e%2e/x.js` 404; `/novo` 404 por falta do `app/index.html`, essa parte PULADA); 14.3 PULADO (sem `/app/js/app.js` ainda); 14.4 OK (pareia e recebe o estado).
- Desvios: (1) `LEIAME.txt` e `OFL-*.txt` têm maiúscula e a D2 só aceita `[a-z0-9._/-]`: ficam embutidos mas não são rota (404 de propósito); o teste "todo arquivo passa no Resolve" só exige isso dos `.woff2` e de `app/`. (2) O teste existente `RemoteRulesTests.PaginaDoCelular_TemMenosDe64KbESemUrlExterna` lia `index.html`; passou a ler as partes concatenadas na ordem de `OldPageParts`. (3) `cabecalhos` imprime um 7º campo (SHA-256 sem `\r`), necessário ao 14.1. (4) 304 só sai para `/app/*` (`no-cache`); `/fontes/*` é `immutable` e sempre 200.
- Pendências para a 14b: o 304 (14.3) e o `/novo` (14.2) só rodam de verdade quando `app/index.html` e `app/js/app.js` existirem; rodar o roteiro de novo então.

### Parte 14b — Sonnet, 25/11/2026

- Criados em `Remote/Web/app/` (22 arquivos, 55.324 bytes no total): `index.html`, `manifest.webmanifest`, `icone-180/192/512.png`; `css/tokens.css`, `base.css`, `componentes.css`; `js/app.js`, `conexao.js`, `estado.js`, `pilha.js`, `icones.js` (todo o objeto `IC` do protótipo, 43 ícones); `js/componentes/` cabecalho, bloco, botao, aviso, toast, dica; `js/telas/` acesso, inicio, provisoria.
- Criados fora: `tools/icone/icone.cs` (D16; PNGs gerados e conferidos a olho) e `MixerDeAudio.Tests/WebFrontTests.cs` (8 testes).
- Build Release x64: 0 erros, 0 avisos. Testes: 931/931 (923 + 8 novos). O `publish` mostra só os avisos IL2104 de trim do SDK, já existentes.
- Roteiro `roteiro-f14.ps1` (8 s, 0 falhas, config restaurado sem diferenças, app do usuário reaberto): 14.1 OK (200, `no-store`, hash igual); 14.2 OK (27 arquivos de `app/` e `fontes/` com tipo, cache, ETag e tamanho certos; `/novo` e `/novo/` 200; 5 negados 404); 14.3 OK (304 sem corpo com o ETag certo; 200 com o errado); 14.4 OK (pareia e recebe o estado). Nenhuma execução de JS no navegador: fica para a conferência do Opus (6.3); só a sintaxe dos 17 módulos foi checada com `node --check`.
- Desvios: (1) o teste de cor fixa (D20) varre só `.css` e `.js`; o `index.html` (`theme-color`) e o manifesto (`theme_color`, `background_color`) precisam do valor literal `#0f1218` por definição. (2) `tools/icone/icone.cs` fica ao lado do `gerar-icone.cs` do ícone do app (não mexido). (3) A faixa "Reconectando…" tem a tela "Conectando…" (título e texto) como primeira tela antes do primeiro estado, em vez de faixa sobre um Início vazio; depois do primeiro estado só a faixa. (4) `botao.js` ganhou a opção `largo` para o Desconectar do Início (o botão grande da D23 é da 14c). (5) Extras no `WebFrontTests`: os `url()` dos CSS e o `Resolve` implícito do HTML apontam para arquivos que existem; cada componente da D20 tem o `export function criar…`. (6) Início sem Favoritos nem estado de CPU/GPU/RAM (D19 só pede Todas as funções e Desconectar).
- Para a 14c: demais componentes da D23, `LEIAME.md`, `galeria.js` e `#galeria` no `app.js`, completar `icones.js`; o teste "todo `export` de `componentes/` aparece no `LEIAME.md`".

### Parte 14c — Sonnet, 26/11/2026

- Componentes novos em `app/js/componentes/` (interface resumida; todos documentados em `LEIAME.md`):
  - `faixa.js`: `criarFaixa({linhas, blocos})` (`data-sem-gesto`) e `criarGrade({colunas, alta, blocos})`; `definir`, `adicionar`, `limpar`.
  - `botaogrande.js`: `criarBotaoGrande({texto, icone, variante, aoClicar})`; `definirTexto`, `definirIcone`, `habilitar`.
  - `cartao.js`: `criarCartao({filhos, classe})`; `definir`, `adicionar`, `limpar`.
  - `cartaoapp.js`: `criarCartaoApp({nome, musica, saida, volume, mudo, tocando, aoVolume, aoMudo, aoSaida, aoAnterior, aoPausa, aoProxima})`; `definirNome/Musica/Saida/Volume/Mudo/Tocando`, `barra`.
  - `barra.js`: `criarBarra({min, max, valor, rotulo, formatar, intervaloMs, esperaMs, aoMudar(valor, final)})` (`data-sem-gesto`); `definir`, `valor`, `retida`, `habilitar`. Regra da D24.
  - `chip.js`: `criarChip({texto, icone, marcado, grande, aoClicar})` e `criarGrupoChips({opcoes, valor, aoEscolher})`; `marcar`, `valor`, `habilitar`.
  - `interruptor.js`: `criarInterruptor({rotulo, ligado, aoMudar})`; `definir`, `valor`, `habilitar`.
  - `medidor.js`: `criarMedidor({valor, max, rotulo})`; `definir`, `valor`.
  - `folha.js`: `criarFolha({container, titulo, opcoes, valor, aoEscolher, aoFechar})`; `abrir`, `fechar`, `aberto`, `marcar`, `definirOpcoes`, `destruir`.
  - `touchpad.js`: `criarTouchpad({sensibilidade, botoes, faixa, dica, aoMover, aoClicar, aoRolar, aoSegurar, aoVisibilidade})` (`data-sem-gesto`); `definirSensibilidade`, `definirDeitado`, `esconder`, `mostrar`, `destruir`. Painel de vidro deitado com alça e bolha.
- Alterados: `bloco.js` e `botao.js` (vibram 8 ms; `habilitar`; o botão ganhou `definirIcone`); `aviso.js`, `toast.js` e `dica.js` ganharam `destruir()`; `icones.js` ganhou `left` e `right` (45 ícones); `componentes.css`, `app.js` (`#galeria`); novo `js/util.js` (`vibrar`).
- Galeria (`telas/galeria.js`): abre por `/novo#galeria` sem conexão (no modo galeria `iniciarConexao` nem roda), entra na pilha, tem a linha do último evento (`#n origem: texto`, também em `window.__galeriaLog`) e todos os componentes e variantes, mais uma camada para simular o painel deitado.
- Conferência no navegador embutido (servidor estático, sem PC): Galeria abre e recarrega sem erro de JS; barra arrastada por 1 s por eventos sintéticos gerou 21 eventos (20 + 1 `final`), `definir(80)` logo depois de soltar foi ignorado e aplicado após a espera; touchpad: toque = clique esq, dois dedos = dir, arrasto soma e entrega por quadro, dois dedos na vertical rolam, 600 ms parado segura e solta; folha, aviso (foco em Cancelar), mudo, pausa, esconder e mostrar do painel deitado funcionam. Os erros de console são só o `/ws` (404), esperado no servidor estático.
- `app/`: 35 arquivos, 122.295 bytes (os 22 da 14b eram 55.324 bytes); 16 módulos em `componentes/`. Sintaxe dos módulos conferida com `node --check`.
- Build Release x64: 0 erros, 0 avisos. Testes: 937/937 (931 + 6 novos em `WebFrontTests`: arquivo, `criar…` e entrada no LEIAME de cada componente da D23; todo `export` no LEIAME e nenhum arquivo fora da lista; componentes sem import de conexão, estado ou pilha, sem `getElementById`; `data-sem-gesto` na barra, faixa e touchpad; ícones citados existem; a Galeria usa todos os componentes e `app.js` abre `#galeria` sem iniciar a conexão).
- Desvios: (1) `LEIAME.md` mora em `app/js/componentes/` como pedido, mas `.md` não é tipo servido (D4): `MixerDeAudio.csproj` ganhou `Exclude` para `.md` (não vai no .exe), o teste de disco da 14a e o `roteiro-f14.ps1` ignoram `.md`. (2) A folha usa as classes `aviso folha`, assim o bloqueio do gesto de voltar da D13 vale para ela sem mudar a pilha. (3) `aoRolar` entrega só `dy` (D25), em passos de 24 px, mesmo sinal da página antiga. (4) O botão Esquerdo/Direito também segura por 450 ms (além de mexer o dedo no painel) para o arrasto. (5) Extras fora da lista: `util.js`, `destruir()` nos componentes de camada, `left`/`right` nos ícones. (6) Não conferido: toque e vibração reais, rolagem da página contra a barra e o touchpad, e o painel deitado em orientação real no iPhone (U5); só eventos sintéticos no navegador do PC.

### Conferência (Opus, 26/11/2026)

- Diff do servidor: `/` concatena as partes com os cabeçalhos de antes; `Resolve` recusa `..`, `//`, `%`, maiúsculas e extensões fora da D4; 304 sem corpo. Sem mudança nas outras rotas.
- Navegador embutido, com servidor estático e PC simulado: o Início mostra só as áreas ligadas; seta, `history.back()`, avançar e arrasto para a esquerda certos; Manrope e JetBrains Mono carregam de `/fontes/`; nenhum erro de JS.
- Galeria (captura): blocos, botões, cartões de app, barras, medidor e chips conferem com o guia. Recarregar dentro da Galeria volta ao Início (D12); basta abrir `/novo#galeria` de novo.
- Publicado de novo depois da 14c: roteiro 14.1 a 14.4 OK (39 arquivos conferidos, 304 certo, hash de `/` igual), config restaurado, app do usuário reaberto. Testes 937/937.
- Não conferido no PC: o pareamento pela tela do `/novo` contra o `/parear` real (o 14.4 cobre o servidor; o cookie é o mesmo de `/`) e os casos U1 a U5, que dependem do iPhone.
- Entrega preparada: `Version` 1.3.0 no csproj e a entrada 1.3.0 nas Novidades (P3).

### Teste do usuário (iPhone, 26/11/2026) e ajustes

- O voltar arrastando para a esquerda funcionou. O ícone foi aprovado.
- **A1, pedido:** voltar também arrastando para a **direita**. Nos dois sentidos valem as regras da D13 (8 px, `|dx| > 1,2·|dy|`, 30% ou rápido, `data-sem-gesto`). A tela acompanha o dedo e sai para o lado do arrasto. Toque que começa a menos de 24 px da borda esquerda é ignorado, para não somar com o voltar do próprio Safari. A dica passa a ser "Arraste para o lado para voltar".
- **A2, defeito:** no app aberto pela Tela de Início, a metade de cima do título "Controladora PC" fica borrada. Causa provável: o iOS desfoca a faixa logo abaixo da barra de status, e o título começava exatamente na borda segura (`max(12px, env(safe-area-inset-top))`). Correção: o espaço do topo passa a ser `calc(env(safe-area-inset-top) + 12px)`, nas telas da pilha, na faixa de conexão e no topo da Galeria. A confirmação fica para o reteste no iPhone.
- **A3, pedido:** no aviso, o botão da ação (`principal`, como o "Aplicar") ficava escuro, porque `.aviso .linha .botao` tirava o fundo de destaque. Agora o fundo escuro só vale para os botões que não são `principal`, e a ação fica na cor da borda (`--accent`).
- **A4, pedido:** os favoritos do Início (parte 15a) usam blocos do tamanho dos da grade de 2, numa faixa que arrasta para o lado. A faixa ganhou a opção `larga` (`criarFaixa({ larga: true })`), com a Galeria e o `LEIAME.md` atualizados. Conferido: o bloco tem 146,5 × 76 px, igual ao da grade de 2, e a faixa rola para o lado.
- **A5, pedido (mão direita e esquerda):** o app deve funcionar bem com qualquer uma das mãos.
  - Cartão de app: o botãozinho de saída sai de baixo do nome e vai para a ponta da linha do nome, do lado da mão usada. Tocar nele continua abrindo a folha de saídas.
  - Preferência "Mão usada: direita | esquerda" (padrão direita) em `app/js/preferencias.js`: `lerMao()`, `definirMao(m)` e `aoMudarMao(fn)`. A escolha fica no `localStorage`, com `try/catch`, e aplica a classe `mao-esquerda` no `<html>`.
  - Os componentes trocam de lado só pelo CSS, sem mudar a interface. Com a mão esquerda: no cartão de app, o avatar vai para a direita e a saída para a esquerda; a faixa de rolagem do touchpad passa para a esquerda; o painel de vidro deitado e a bolha dele vão para baixo à esquerda.
  - Tela "Opções" (`telas/opcoes.js`), aberta por um chip com engrenagem no topo do Início, com o grupo de chips "Mão usada". A Galeria ganha o mesmo grupo para conferir.
- Conferência do A5 (Opus, navegador embutido): com a mão esquerda, o cartão de app espelha (a saída à esquerda, o avatar à direita e a música alinhada sob o nome, corrigido na conferência), a faixa do touchpad vai para a esquerda e a escolha fica salva; a tela Opções abre pelo chip do Início. Testes 938/938.
- Conferência (Opus, navegador embutido): o arrasto para a direita acompanha o dedo e sai para a direita; o toque a menos de 24 px da borda esquerda é ignorado; um aviso aberto bloqueia o gesto; o "Aplicar" tem fundo `--accent` e texto `--accent-ink`. Testes 937/937. Entrega: na versão do fim da Fase 15 (decisão do usuário em 26/11/2026).

## 8. Pendências

- Novidades da versão do fim da Fase 15: incluir os ajustes da Fase 14 (arrastar para os dois lados, título sem borrão no topo, botão de confirmar em destaque, escolha da mão usada).

- `/icone/` manda dois `Cache-Control` e o navegador nunca guarda o ícone (desde a Fase 9). Corrigir à parte, se o usuário quiser.
- Fase 17: `/` passa a servir `app/index.html`, `/novo` vira redirecionamento para `/` (atalhos criados na prévia continuam valendo), o manifesto troca `start_url` para `/` e a pasta `antigo/` sai com o hash da D8.
