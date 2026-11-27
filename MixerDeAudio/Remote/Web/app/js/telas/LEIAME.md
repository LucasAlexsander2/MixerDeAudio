# Telas de área (Fase 15, parte 15.0)

Cada área do Início tem um módulo aqui. A 15.0 criou todos como provisórios (`criarTelaProvisoria`); as partes 15a a 15d trocam **só o conteúdo dos seus**.

| Módulo | Área (`AREAS` em `inicio.js`) | Ícone | CSS | Parte |
|---|---|---|---|---|
| `som.js` | `som` | `volume` | `css/telas/som.css` | 15a |
| `energia.js` | `energia` | `power` | `css/telas/energia.css` | 15b |
| `monitores.js` | `monitores` | `monitors` | `css/telas/monitores.css` | 15b |
| `sono.js` | `timer` (Timer de sono) | `moon` | `css/telas/sono.css` | 15b |
| `estado-pc.js` | `estado` (Estado do PC) | `activity` | `css/telas/estado-pc.css` | 15b |
| `controle.js` | `controle` | `gamepad` | `css/telas/controle.css` | 15c |
| `tela-pc.js` | `tela` (Tela do PC) | `monitor` | `css/telas/tela-pc.css` | 15c |
| `janelas.js` | `janelas` | `windows` | `css/telas/janelas.css` | 15d |
| `atalhos.js` | `atalhos` | `shortcut` | `css/telas/atalhos.css` | 15d |
| `enviar.js` | `enviar` | `send` | `css/telas/enviar.css` | 15d |

O Início e o Opções (`inicio.js`, `opcoes.js`, `css/telas/inicio.css`) são da 15a.

## Regras (D8 a D10)

- **D8, arquivos fechados:** nenhuma parte muda `componentes/`, `pilha.js`, `conexao.js`, `estado.js`, `assinaturas.js`, `app.js` nem `index.html`. Se faltar algo, anote como pendência na seção 8 do documento da fase e siga. Cada parte escreve só nos próprios módulos, no próprio CSS (`css/telas/<área>.css`) e nos próprios testes.
- **D9, visual e funções:** textos e visual do protótipo (`docs/prototipo/controladora.html`), montados com os componentes (`componentes/LEIAME.md`). Funções e mensagens para o PC: as da parte antiga correspondente (página antiga, hoje só no histórico do git), para nada se perder. Cores só por tokens (`css/tokens.css`). Ícones só os de `icones.js` (se faltar um, use o mais próximo e anote).
- **D10, segurança e mão:** ação perigosa (desligar, reiniciar, fechar janela...) pede confirmação (`criarAviso`, com `container: ctx.camadas`); a mão usada é respeitada (a classe `mao-esquerda` no `<html>` já inverte o layout; use `ctx.preferencias.aoMudarMao` se precisar reagir); elementos com arrasto próprio levam `data-sem-gesto` para não disparar o gesto de voltar.
- Só mande mensagens (`t:`) que o PC aceita (lista do `RemoteSession`).
- Uma tela só assina (`ctx.assinar`) enquanto está no topo e solta tudo em `aoTopo(false)` e em `destruir()`.

## Contrato do `ctx` (D3)

`export function criarTela(ctx)` recebe sempre o mesmo objeto:

| Campo | Assinatura | O que faz / devolve |
|---|---|---|
| `ctx.enviar(obj)` | `enviar({ t: 'volume', ... })` | Manda o objeto como JSON pelo WebSocket. Sem conexão aberta, não faz nada. Não devolve nada. |
| `ctx.aoMudar(fn)` | `aoMudar((estado) => ...)` | Chama `fn(estado)` a cada `estado` novo do PC. Devolve a função que cancela. Chame o cancelamento em `destruir()`. |
| `ctx.obterEstado()` | `obterEstado()` | Último `estado` recebido (objeto) ou `null` se ainda não chegou. Use para desenhar a tela ao abrir. |
| `ctx.aoMensagem(tipo, fn)` | `aoMensagem('janelas', (m) => ...)` | Chama `fn(mensagem)` para cada mensagem com `m.t === tipo` que a conexão não trata (`janelas`, `clip`, `info`...; nunca `estado`, `ocupado`, `desconectado` nem `aviso`). Devolve a função que cancela. |
| `ctx.assinar(grupo)` | `const soltar = assinar('info')` | Liga o envio periódico de um grupo (`info`, `janelas`...), ver a seção das assinaturas. Devolve `soltar()`. |
| `ctx.toast(texto, ms)` | `toast('Enviado', 1800)` | Mostra um aviso curto na parte de baixo. `ms` é opcional (padrão 1800). Não devolve nada. |
| `ctx.camadas` | `HTMLElement` | Contêiner das camadas por cima da pilha: use como `container` de `criarAviso`, `criarFolha` e semelhantes. |
| `ctx.abrir(nome)` | `abrir('estado')` | Abre outra tela registrada (nome de `AREAS`, `opcoes`). Não devolve nada. |
| `ctx.voltar()` | `voltar()` | Volta uma tela (equivale ao voltar do navegador). Use no `aoVoltar` do cabeçalho. |
| `ctx.preferencias` | objeto, ver abaixo | Mão usada e preferências próprias de cada área. |

## Retorno da tela (D5)

`criarTela(ctx)` devolve `{ el, aoTopo, destruir }`:

- `el` (obrigatório): a `<section class="tela">` da tela.
- `aoTopo(sim)` (opcional): `true` quando a tela fica no topo da pilha (ao abrir e quando a tela de cima sai); `false` quando outra tela entra por cima ou quando ela mesma sai. É aqui que se assina e se solta.
- `destruir()` (opcional): chamada uma vez quando a tela sai da pilha (voltar, gesto, `reiniciar()` por desconexão), logo depois de `aoTopo(false)`. Cancele ouvintes (`aoMudar`, `aoMensagem`), temporizadores e assinaturas.

A pilha também aceita a fábrica devolver só o elemento (como as telas antigas). O Início não vem de fábrica: o `app.js` o passa como `inicial` e liga o `aoTopo` dele pela opção `inicialTopo` de `criarPilha`, chamando `inicio.aoTopo(sim)` se o retorno de `criarTelaInicio` tiver essa função (a 15a acrescenta). `aoTopo(true)` do Início já é chamado na criação da pilha. `criarTelaInicio` e `criarTelaOpcoes` também recebem `ctx` (opção `ctx`).

## Assinaturas (D4)

`assinaturas.js` conta as assinaturas por grupo, então duas telas podem usar o mesmo grupo sem brigar (Início e Estado do PC usam `info`):

- a primeira `assinar(g)` manda `{ t: 'assinar', g, on: true }`; a última `soltar()` manda `{ t: 'assinar', g, on: false }`;
- depois de reconectar, manda de novo os grupos ligados;
- com a página escondida (`visibilitychange`), desliga todos; ao voltar a aparecer, religa;
- `soltar()` chamada duas vezes só vale uma vez.

Padrão de uso numa tela:

```js
let soltar = null;
// dentro do objeto devolvido por criarTela:
aoTopo(sim) {
  if (sim && !soltar) soltar = ctx.assinar('info');
  else if (!sim && soltar) { soltar(); soltar = null; }
},
destruir() { if (soltar) soltar(); },
```

## Preferências (D6)

`ctx.preferencias` (de `preferencias.js`):

- `lerMao()` devolve `'direita'` ou `'esquerda'`; `definirMao(mao)` grava e aplica; `aoMudarMao(fn)` chama `fn(mao)` a cada mudança e devolve o cancelamento.
- `ler(chave, padrao)` devolve o valor guardado (JSON) ou `padrao` se não existir ou se o `localStorage` falhar.
- `gravar(chave, valor)` guarda o valor como JSON; devolve `false` se o `localStorage` falhar. Nunca lança erro.
- Cada área usa chaves com o próprio prefixo: `inicio.` (ex.: `inicio.favoritos`), `tela.`, `controle.`, `som.`...

## CSS por área (D7)

Cada módulo tem o seu `css/telas/<módulo>.css` (e o Início, `css/telas/inicio.css`), já ligado no `index.html` e vazio. Escreva só nele; prefixe as classes com o nome da área (`.som-...`, `.tela-pc-...`) para não colidir com as outras partes. Cores e medidas por tokens.

## Exemplo de tela

```js
// telas/janelas.js
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarBotao } from '../componentes/botao.js';

export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'janelas';
  const cab = criarCabecalho({ titulo: 'Janelas', aoVoltar: () => ctx.voltar() });
  const atualizar = criarBotao({ texto: 'Atualizar', aoClicar: () => ctx.toast('Atualizado') });
  el.append(cab.el, atualizar.el);

  let soltar = null;
  const cancelar = ctx.aoMensagem('janelas', (m) => { /* desenha m.lista */ });
  return {
    el,
    aoTopo(sim) {
      if (sim && !soltar) soltar = ctx.assinar('janelas');
      else if (!sim && soltar) { soltar(); soltar = null; }
    },
    destruir() { cancelar(); if (soltar) soltar(); },
  };
}
```
