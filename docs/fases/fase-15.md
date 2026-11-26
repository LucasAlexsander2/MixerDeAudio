# Fase 15 — Telas no layout novo (Controladora PC)

Status: aprovada pelo usuário (26/11/2026), com as recomendações de P1 a P4.

## 1. Objetivo

Trazer todas as áreas para o `/novo`, no visual do protótipo aprovado e com as mesmas funções da página antiga (regra de ouro: nada se perde), em quatro partes paralelas que só usam os componentes fechados da Fase 14:

- **15a, Início e Som:** favoritos, estado rápido (CPU/GPU/RAM), Todas as funções, Desconectar; Som com saídas, volume geral e apps com música. A GPU % passa a vir do PC (A2 da Fase 13).
- **15b, PC e monitores:** Energia com agendamento, Monitores (projeção, apagar/acordar, brilho, manter a tela ligada), Timer de sono e Estado do PC.
- **15c, Controle e Tela:** touchpad, teclado e modos (Touchpad, Teclado, Apresentação, Vídeo, Mídia); Tela do PC com o touchpad dentro dela, em pé e deitado, com tamanho do ponteiro e zoom que segue o ponteiro (pedidos do usuário de 26/11/2026).
- **15d, Janelas e Enviar:** Janelas, Atalhos, área de transferência e envio de arquivos.

A página antiga em `/` não muda (hash da D8 da Fase 14).

## 2. Perguntas ao usuário

| # | Pergunta | Recomendação | Resposta |
|---|---|---|---|
| P1 | Favoritos do Início: o que pode virar favorito e como escolher? | Uma lista fixa de ações: Mudo geral, cada saída de áudio, Play/Pausa (do app que está tocando), Bloquear (com confirmação), Tela do PC, Brilho e qualquer área. Na tela Opções, uma seção "Favoritos" com um interruptor por ação. A escolha fica no celular. Padrão: os do protótipo (Mudo, as saídas, Brilho, Bloquear, Tela do PC e Play/Pausa). | |
| P2 | Teclas Ctrl, Alt, Shift e Win "presas" no teclado do Controle (do protótipo) | Sim. Elas pedem uma mudança pequena no PC: aceitar a tecla com modificadores, só para as teclas da lista fixa que já existe (nada de tecla livre). O modificador solta sozinho depois da próxima tecla. | |
| P3 | Tamanho do ponteiro na Tela do PC | As opções 1×, 1,5×, 2× e 3×, com padrão 1,5× e a escolha guardada no celular. O PC desenha o ponteiro maior só na imagem enviada; o ponteiro do Windows não muda. | |
| P4 | Versão do fim da fase | **1.4.0**, com as Novidades da prévia completa em `/novo` e os ajustes da Fase 14. É publicada só depois do seu teste e da sua autorização. | |

## 3. Levantamento (26/11/2026)

- `app.js` registra hoje uma tela provisória por área; as áreas não têm módulo próprio. `conexao.js` só entrega o `estado`, `ocupado`, `desconectado` e `aviso`; não entrega `janelas`, `clip` nem `info`. A pilha não avisa a tela quando ela fica no topo ou sai.
- As assinaturas (`assinar` `info` e `janelas`) da página antiga ligam e desligam com a aba aberta. No layout novo, o Início (estado rápido) e o Estado do PC usam o `info` ao mesmo tempo.
- O PC já desenha o ponteiro na imagem da tela (`ScreenCapture.DrawCursor`, tamanho do sistema × escala). A posição do ponteiro não vai para o celular.
- As teclas aceitas são uma lista fixa (`InputRules`): sem modificadores livres.
- Rodar quatro agentes na mesma pasta faz os builds brigarem (arquivos presos em `obj/`). Por isso cada parte roda num worktree próprio, que parte de um commit.

## 4. Decisões

| # | Decisão |
|---|---|
| D1 | **Parte 15.0 (preparação), antes das outras e sozinha:** cria tudo o que é comum, para as quatro partes só escreverem nos próprios arquivos (D2 a D7). Depois dela, commit local. |
| D2 | Um módulo por área em `app/js/telas/`: `som.js`, `energia.js`, `monitores.js`, `sono.js`, `estado-pc.js`, `controle.js`, `tela-pc.js`, `janelas.js`, `atalhos.js`, `enviar.js`. Cada um tem `export function criarTela(ctx)` e devolve `{ el, aoTopo?(sim), destruir?() }`. A 15.0 cria todos como provisórios e o `app.js` registra todos. As partes só trocam o conteúdo dos seus. |
| D3 | `ctx` (fixo, descrito em `app/js/telas/LEIAME.md`): `enviar`, `aoMudar` e `obterEstado` (estado), `aoMensagem(tipo, fn)` (`janelas`, `clip`, `info`…; devolve a função que cancela), `assinar(grupo)` (D4), `toast`, `camadas` (onde abrir aviso e folha), `abrir(nome)`, `voltar()`, `preferencias` (mão e as próprias de cada área, D6). |
| D4 | `app/js/assinaturas.js`: contagem por grupo. `assinar('info')` devolve `soltar()`; manda `assinar on` no primeiro e `off` no último, manda de novo depois de reconectar e desliga com a página escondida. Assim o Início e o Estado do PC dividem o `info` sem brigar. |
| D5 | Pilha: aceita a fábrica devolvendo `{ el, aoTopo, destruir }`. Chama `aoTopo(true/false)` quando a tela entra ou sai do topo e `destruir()` ao sair da pilha. Uma tela assina só enquanto está no topo. |
| D6 | `preferencias.js` ganha `ler(chave, padrão)` e `gravar(chave, valor)` genéricos (`localStorage` com `try/catch`). Cada área usa chaves com o próprio prefixo (`inicio.`, `tela.`…). |
| D7 | CSS por área em `app/css/telas/<área>.css` (vazios na 15.0), todos ligados no `index.html` pela 15.0. Os ícones de `icones.js` são fechados: se faltar um, a parte usa o mais próximo e anota (a conferência acrescenta). |
| D8 | **Componentes fechados (D22 da Fase 14):** nenhuma parte muda `componentes/`, `pilha.js`, `conexao.js`, `estado.js`, `assinaturas.js`, `app.js` nem `index.html`. Se precisar, anota como pendência e segue. |
| D9 | Visual e textos de cada tela: os do protótipo (`docs/prototipo/controladora.html`, telas `s-home`, `s-sound`, `s-pow`, `s-mon`, `s-sleep`, `s-stat`, `s-ctl`, `s-pc`, `s-win`, `s-sc`, `s-snd`), com os componentes no lugar do HTML do protótipo. Funções e mensagens: as da parte antiga correspondente (`antigo/*.js`, lista na seção 5), para nada se perder. Áreas aparecem conforme os grupos ligados (regra de hoje). |
| D10 | Ações perigosas pedem confirmação (D8 da Fase 13); mão usada respeitada (A5 da Fase 14); `data-sem-gesto` nas áreas com arrasto próprio. |
| D11 | 15a, favoritos (P1): catálogo fixo em `inicio.js`, interruptores numa seção "Favoritos" da tela Opções (`opcoes.js`, que passa a ser da 15a), guardados em `inicio.favoritos`. Faixa `larga` (A4 da Fase 14). |
| D12 | 15a, estado rápido: três medidores (CPU, GPU, RAM) no topo do Início. Assina `info` só com o Início no topo. Tocar abre o Estado do PC. Sem GPU: "GPU —". |
| D13 | 15a, GPU % no PC: contador do Windows "GPU Engine / Utilization Percentage", somando os motores `engtype_3D` do adaptador mais usado, por PDH (`P/Invoke`, sem pacote novo). Lido junto com a CPU, só com `info` assinado. Vai no `info` como `gpu` (0–100) e não vai se a leitura falhar. A regra de soma fica em lógica pura com teste. Arquivos: `Pc/PcInfo.cs` e o leitor em `Pc/`; no protocolo, só o trecho do `info`. |
| D14 | 15b: Energia (desligar, reiniciar, suspender, bloquear, com agendamento e cancelar), Monitores (projeção, apagar/acordar, brilho por tela, manter ligada), Timer de sono e Estado do PC (CPU, GPU, RAM, ligado há, bateria e disco), como no protótipo. Só JS. |
| D15 | 15c, teclado: modificadores presos (P2) com `{t:'tecla', k, mods:['ctrl','alt','shift','win']}`. O PC aceita `mods` só com uma tecla da lista fixa e recusa o resto. Fica em `InputRules` (lógica pura, com testes) e no trecho do `tecla` do protocolo. A página antiga não manda `mods` e continua igual. |
| D16 | 15c, Tela do PC: imagem com o touchpad dentro (nota "Tela do PC com touchpad"), modos Touchpad e Toque direto, barra com teclado, modo, zoom, qualidade e tela cheia; deitado com o painel de vidro. O canal `/tela/ws` ganha dois campos opcionais na configuração: `cur` (1, 1,5, 2 ou 3; tamanho do ponteiro, P3) e `p:1`. Com `p:1`, o PC manda `{"p":[x,y]}` (0–1 no quadro) antes de cada quadro, para o zoom seguir o ponteiro. Sem os campos, o PC faz como hoje, e a página antiga continua igual. Arquivos: `Pc/ScreenStreamRules.cs` (leitura, com testes), `Pc/ScreenCapture.cs` e o laço da tela em `Remote/RemoteServer.cs`. |
| D17 | 15d: Janelas (lista com ícone, focar, minimizar, fechar com confirmação; assina `janelas` só no topo), Atalhos (blocos do `pc.atalhos`), Enviar (área de transferência nos dois sentidos e arquivo por `POST /arquivo` com progresso, como na página antiga). Só JS. |
| D18 | Testes de cada parte em arquivos próprios: `WebFront15aTests.cs` a `WebFront15dTests.cs` e, para o PC, os arquivos de teste que já existem da lógica mexida (`PcInfoTests`, `InputRulesTests`, `ScreenStreamRulesTests`). Ninguém mexe no `.csproj` de testes, a não ser a 15a se o leitor da GPU tiver lógica pura num arquivo novo (só ela liga esse arquivo). |
| D19 | **Ordem e junção:** a 15.0 roda e é commitada localmente. Depois, 15a a 15d rodam juntas, cada uma num worktree com branch própria, cada uma com commit local na sua branch. O Opus junta as quatro branches no `main` local, resolve conflitos pequenos (`RemoteProtocol.cs` pode ter dois trechos) e confere. Nada vai para o GitHub até a P4. |
| D20 | A Fase 16 (Segurança) mexe no servidor: começa só depois da junção da 15, para não conflitar com a 15a e a 15c. |
| D21 | Leveza no PC: GPU lida só com `info` assinado; posição do ponteiro só com `p:1` e junto do quadro, sem timer novo. |

## 5. Partes

Todas usam o Sonnet com esforço `medium`. A 15.0 roda sozinha; depois, a 15a, a 15b, a 15c e a 15d rodam em paralelo, cada uma no seu worktree.

### 15.0 — Preparação comum

- **Novos:**
  - `app/js/assinaturas.js` (D4) e `app/js/telas/LEIAME.md` (contrato do `ctx` e regras da D8);
  - os dez módulos de área provisórios (D2);
  - os onze CSS vazios: as dez áreas e `inicio.css` (D7);
  - `tools/testes/servidor-teste.ps1`: liga o app de teste com `--teste-oculto --celular-local --teste-pc` e um código conhecido, com `BackupConfig` antes. Fica esperando um arquivo de parar e então restaura a configuração e reabre o app do usuário. Serve para a conferência no navegador. Nunca é interrompido à força.
- **Alterados:**
  - `conexao.js` (`aoMensagem` e o aviso de reconexão);
  - `pilha.js` (D5);
  - `preferencias.js` (D6);
  - `app.js`: registra as áreas com o `ctx`;
  - `index.html`: os CSS;
  - `WebFrontTests.cs`: cada área tem o seu módulo com `criarTela` e o seu CSS ligado; nenhum componente foi mudado.

### 15a — Início e Som

- **Do celular:** `telas/inicio.js`, `telas/opcoes.js`, `telas/som.js`, `css/telas/inicio.css` e `css/telas/som.css`.
- **Do PC:** a GPU (D13).
- **Referências:** `antigo/08-som.js` e `05-conexao.js` (ícones em `/icone/<id>`), e `12-estado-pc.js` (formato do `info`).
- **Som:**
  - saídas em blocos (tocar = virar a padrão);
  - volume geral com mudo;
  - cartões de app: mudo, volume, saída pela folha (`rota`), Anterior/Pausa/Próxima e nome da música.

### 15b — PC e monitores

- **Do celular:** `energia.js`, `monitores.js`, `sono.js`, `estado-pc.js` e os CSS deles.
- **Referências:** `antigo/09-pc.js`, `11-brilho.js`, `12-estado-pc.js` e `14-sono.js`.

### 15c — Controle e Tela

- **Do celular:** `controle.js`, `tela-pc.js` e os CSS deles.
- **Do PC:** D15 e D16.
- **Referências:** `antigo/16-controle.js` e `17-tela.js`, e a nota "Tela do PC com touchpad" (Obsidian).
- **Componentes:** o touchpad é o da Fase 14, ligado ao protocolo aqui.

### 15d — Janelas e Enviar

- **Do celular:** `janelas.js`, `atalhos.js`, `enviar.js` e os CSS deles.
- **Referências:** `antigo/13-enviar.js` e `15-janelas.js`.

## 6. Testes

### 6.1 Unitários (toda a bateria, 938 hoje, em toda parte)

- **15.0:** módulos e CSS das áreas (D2 e D7); a conferência também confere que nenhum arquivo da D8 mudou fora da 15.0.
- **15a:**
  - soma da GPU: motores 3D somados por adaptador; vale o maior; corte em 100; lista vazia dá "sem leitura";
  - `gpu` no `info` quando há leitura e ausente quando não há;
  - catálogo de favoritos: cada item tem ação e ícone que existem.
- **15b:** os módulos só mandam tipos de mensagem que o PC aceita: os `t:` do JS conferidos com a lista do `RemoteSession`. A mesma conferência vale para 15a, 15c e 15d, cada uma no seu arquivo.
- **15c:**
  - `tecla` com `mods`: válido, modificador desconhecido, tecla fora da lista e lista vazia;
  - configuração da tela com `cur` (cada valor, valor inválido = 1) e `p`;
  - posição do ponteiro em 0–1 também nas bordas e fora do quadro.
- **15d:** a conferência de tipos de mensagem e os `data-sem-gesto` da lista de janelas.

### 6.2 Roteiros sem janela (`roteiro-f15.ps1 -Parte a|b|c|d`, cada parte escreve o seu trecho)

| Caso | Por que precisa do app | Passos | Esperado |
|---|---|---|---|
| 15a.1 | Contador real da GPU | assinar `info` e ler 3 mensagens | `gpu` entre 0 e 100 (ou ausente, com o motivo no log) |
| 15c.1 | Ponteiro no canal real | abrir `/tela/ws` com `cur:2,p:1` | antes de cada quadro, um `{"p":[x,y]}` com x e y entre 0 e 1 |
| 15c.2 | Sem regressão da página antiga | `/tela/ws` sem `cur` e `p` | só quadros binários, como hoje |
| 15c.3 | Teclas com `--teste-pc` | `tecla` `c` com `mods:['ctrl']` e `k:'q'` com `mods` | o log tem `ctrl+c`; o segundo é recusado |
| 15.x | Sem regressão | `roteiro-f14.ps1` | 14.1 a 14.4 OK (página antiga idêntica) |

Os roteiros usam `BackupConfig` e `RestoreConfig`, reabrem o app do usuário e nunca são interrompidos.

### 6.3 Conferência (Opus) e usuário

- **Opus, no navegador embutido, com `servidor-teste.ps1` (PC de teste de verdade):** abre cada área, confere cada ação no log do `--teste-pc` (uma por área), as assinaturas ligando e desligando, a mão esquerda e capturas só onde o visual importa (Início, Som, Tela deitada).
- **Usuário, no iPhone** (curto, só o que depende do aparelho):
  - U1: o Início com favoritos e o estado rápido; trocar um favorito em Opções.
  - U2: Som: volume de um app, trocar a saída pela folha, Próxima música.
  - U3: Controle: touchpad, teclado com Ctrl preso e Apresentação.
  - U4: Tela do PC: em pé e deitado, ponteiro em 2×, zoom com pinça seguindo o ponteiro.
  - U5: Enviar uma foto e Janelas (focar uma janela).
  - U6: Energia: agendar e cancelar um desligamento (sem deixar chegar a zero).
  - U7: reteste da Fase 14: o título sem borrão no app da Tela de Início (A2) e `/` como sempre.

## 7. Resultados

(preenchido na implementação e na conferência)

### Parte 15.0 — Sonnet, 26/11/2026

- `app/js/assinaturas.js` (D4), novo; `conexao.js` ganhou `aoMensagem(tipo, fn)` e `aoAbrir(fn)` (chamado a cada WebSocket aberto, usado pelas assinaturas para reenviar), sem mudar o resto; `preferencias.js` ganhou `ler`/`gravar` (D6; a função interna `ler` virou `lerMaoSalva`).
- `pilha.js` (D5): fábrica devolve elemento ou `{ el, aoTopo, destruir }`; `aoTopo` só é chamado quando muda, `destruir` ao sair (também no `reiniciar`); opção `inicialTopo(sim)` para o Início, que o `app.js` liga a `inicio.aoTopo` se existir (a 15a acrescenta); `criarTelaInicio` e `criarTelaOpcoes` recebem `ctx`.
- Dez módulos provisórios em `app/js/telas/` (D2), `app.js` registra cada área de `AREAS` pelo módulo e monta o `ctx` (D3); onze CSS vazios em `app/css/telas/` ligados no `index.html` (D7); `app/js/telas/LEIAME.md` com ctx, retorno, assinaturas, preferências, CSS, D8 a D10 e exemplo.
- `tools/testes/servidor-teste.ps1` e seção no `tools/testes/README.md` (BackupConfig antes, RestoreConfig no `finally`, marcador `servidor-teste.ativo`, recusa com `publicado\` desatualizado). Não foi executado.
- Build Release x64: 0 avisos, 0 erros. `node --check` em todos os `.js` alterados: OK.
- Testes: 942 aprovados (938 + 4 novos em `WebFrontTests.cs`: módulos e CSS das áreas, registro no `app.js`, assinaturas e `aoMensagem`, LEIAME cita cada campo do `ctx`).
- Conferência no navegador (servidor estático em 127.0.0.1:8799, já parado): a página carrega sem erro, 11 CSS ligados; pilha e assinaturas testadas por script (ordem de `aoTopo`/`destruir`, contagem por grupo).
- `roteiro-f14.ps1` (depois de publicar): 14.1 a 14.4 OK, config e registro iguais, app do usuário reaberto.
- Desvios: `aoAbrir` em `conexao.js` (nome do "aviso de reconexão"); o ctx traz `preferencias` com `lerMao`, `definirMao`, `aoMudarMao`, `ler`, `gravar`; `ctx.toast(texto, ms)` é um invólucro de `toast.mostrar`.

### Partes 15a a 15d — Sonnet, em paralelo (worktrees), 26/11/2026

- 15a (`0d28d53`): Início (favoritos na faixa larga, estado rápido com CPU/GPU/RAM, Todas as funções, Desconectar), Opções (mão e favoritos), Som; GPU por PDH (`Pc/GpuReader.Native.cs`, `PcInfoRules.GpuPercent`), `gpu` no `info`. Desvios: a 1ª leitura da GPU vem sem `gpu` (o contador precisa de duas amostras); sem linha de log quando falta a GPU; o ícone do app é um `<img>` posto no avatar do cartão por fora do componente.
- 15b (`cb01491`): Energia (4 ações com confirmação, agendamento e Cancelar), Monitores (projeção, apagar/acordar, brilho por tela, manter ligada), Timer de sono e Estado do PC. Desvios: o "No fim" do protótipo não entrou no Timer de sono (o PC só tem um comportamento); "Manter a tela ligada" aparece com o grupo `entrada`.
- 15c (`288c6cb`): Controle (Touchpad, Teclado com modificadores presos, Apresentação, Vídeo, Mídia) e Tela do PC (touchpad dentro, Toque direto que move e clica, ponteiro 1×–3×, zoom seguindo o ponteiro, deitado); `InputRules.TryCombo` (recusa Ctrl+Alt+Delete e combinações fora da lista), `cur` e `p` no `/tela/ws`. Desvios: a tela transmite ao abrir (sem "Mostrar a tela"); Mídia sem o cartão da música; Vídeo com Vol+/Vol− em vez de barra.
- 15d (`b824978`): Janelas (lista com ícones, Frente, Minimizar, Fechar com confirmação), Atalhos, Enviar (área de transferência nos dois sentidos, fila de arquivos com progresso e Cancelar).
- Os worktrees nasceram do commit enviado (`f3058b7`), sem a 15.0: a 15b e a 15c fizeram `merge --ff-only main`; a 15a e a 15d copiaram a base e commitaram só os próprios arquivos.

### Junção e conferência (Opus, 26/11/2026)

- Junção no `main` local: 15b (avanço direto), 15d, 15a (conflitos só nos arquivos provisórios das próprias áreas, resolvidos com a versão da parte) e 15c (sem conflito; `RemoteProtocol.cs` com os dois trechos). Build Release sem avisos; testes 1082/1082.
- Código do PC: o canal da tela só muda com `cur`/`p`, o texto do ponteiro só vai antes de um quadro enviado; a GPU libera o buffer e fecha a consulta PDH; `tecla` com `mods` passa por `TryCombo`.
- Roteiros (app publicado, um por vez): `roteiro-f14` 14.1 a 14.4 OK (página antiga idêntica, 64 arquivos servidos); `roteiro-f15a` 15a.1 OK (`gpu` em 3 de 4 mensagens, nada depois do `off`); `roteiro-f15c` 15c.1, 15c.1b, 15c.2 e 15c.3 OK. Config e registro restaurados sem diferenças em todos.
- Navegador embutido com `servidor-teste.ps1` (ganhou `-Grupos`, todos ligados por padrão): pareamento pelo `/novo`; as dez áreas abrem com dados reais (apps com ícones e músicas, janelas, monitores, estado do PC, imagem da tela); Bloquear pede confirmação e chega ao PC de teste (`teste-pc: energia bloquear`); GPU aparece no Início e no Estado do PC. Config devolvido sem diferenças.
- Não conferido no PC: o visual no iPhone, gestos reais (pinça, deitado, tela cheia), o envio de arquivo de verdade e as teclas no PC real (o `--teste-pc` só registra).

## 8. Pendências

- Ícones que faltaram: ponteiro (a 15c usa chip de texto).
- Fase 16 (Segurança) pode começar: a 15 já está junta.
- `.claude/worktrees/` (cópias dos agentes) fica fora do git; apagar os worktrees depois da entrega.
