# Componentes do novo front (Fase 14c, D22)

Regras de uso (partes 15a a 15d):
- Cada componente é `export function criarX(opções)` e devolve um objeto com `el` (o elemento a inserir) e os métodos de atualização. Nenhum depende de ids globais nem do protocolo do PC: quem usa liga os callbacks (`aoX`) ao `enviar(...)` e chama os métodos (`definir`, `marcar`...) com o estado que vem do PC.
- **Não altere os componentes.** Se faltar algo, avise na conferência.
- Os métodos de atualização (`definir…`, `marcar`) nunca chamam os callbacks; os callbacks só disparam por toque do usuário.
- `data-sem-gesto`: a barra, a faixa, o touchpad (e o cartão de app, por conter a barra) já vêm marcados e não deixam o gesto de arrastar para voltar começar sobre eles. Em qualquer outro elemento que precise de arrasto próprio, ponha `data-sem-gesto` na mão.
- Cores só por tokens; a vibração de 8 ms (D9) já está no bloco e nos botões. Veja todos e as variantes na Galeria: `/novo#galeria`.
- Ícones: `import { icone } from '../icones.js'` (nome → SVG); os componentes aceitam o nome (`'volume'`, `'power'`...).
- Classes de layout prontas em `css/componentes.css`: `.linha-botoes` (botões em linha dividindo a largura) e `.chips`.

Exemplo geral de uma tela:

```js
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarBarra } from '../componentes/barra.js';
import { enviar } from '../conexao.js';

const barra = criarBarra({ rotulo: 'Brilho', aoMudar: (v, final) => enviar({ t: 'brilho', v }) });
// ao receber o estado: barra.definir(estado.brilho);
```

---

## cabecalho.js — `criarCabecalho`
Cabeçalho de 52 px com seta de voltar de 44 px, título e ações à direita.
- Opções: `{ titulo, aoVoltar, acoes: [HTMLElement] }` (as ações costumam ser `criarChip(...).el`).
- Eventos: `aoVoltar()` ao tocar na seta.
- Métodos: `definirTitulo(texto)`. Devolve `{ el, definirTitulo }`.
- Variantes: sem ações / com ações. `data-sem-gesto`: não.
- Uso: `const c = criarCabecalho({ titulo: 'Som', aoVoltar: () => pilha.voltar() });`

## bloco.js — `criarBloco`
Bloco de 100 x 76 px com ícone e nome curto.
- Opções: `{ nome, icone, ligado, ativo, aoClicar }`.
- Eventos: `aoClicar(evento)` (vibra 8 ms).
- Métodos: `definirLigado(bool)` (escolha: borda e texto em destaque), `definirAtivo(bool)` (ponto verde), `definirIcone(nome)`, `definirNome(texto)`, `habilitar(bool)`. Devolve `{ el, ... }`.
- Variantes: normal, ligado, com ponto, desabilitado. `data-sem-gesto`: não (a faixa que o contém já tem).
- Uso: `criarBloco({ nome: 'Fones', icone: 'headphones', aoClicar: () => escolher(1) })`.

## faixa.js — `criarFaixa` e `criarGrade`
Contêineres de blocos.
- `criarFaixa({ linhas: 1 | 2, blocos: [HTMLElement] })`: rolagem para o lado, uma ou duas linhas. **Tem `data-sem-gesto`.**
- `criarGrade({ colunas: 2 | 3, alta, blocos })`: grade; `alta` dá no mínimo 88 px aos blocos (nomes longos, atalhos).
- Métodos (os dois): `definir([HTMLElement])` (troca tudo), `adicionar(HTMLElement)`, `limpar()`. Devolvem `{ el, definir, adicionar, limpar }`.
- Uso: `const f = criarFaixa({ linhas: 2 }); f.definir(saidas.map((s) => criarBloco({...}).el));`

## botao.js — `criarBotao`
Botão de 48 px.
- Opções: `{ texto, icone, variante: 'padrao' | 'perigo' | 'principal', largo, aoClicar }` (`largo` é o botão de rodapé do Início).
- Eventos: `aoClicar(evento)` (vibra 8 ms).
- Métodos: `definirTexto(texto)`, `definirIcone(nome)`, `habilitar(bool)` (`desativar(bool)` é o inverso, mantido da 14b). Devolve `{ el, ... }`.
- Em linha: ponha 2 ou 3 dentro de um elemento com a classe `linha-botoes`.
- Uso: `criarBotao({ texto: 'Reiniciar', icone: 'restart', variante: 'perigo', aoClicar })`.

## botaogrande.js — `criarBotaoGrande`
Botão de no mínimo 96 px, ícone de 32 px, texto 16/700 (Energia, Acordar).
- Opções: `{ texto, icone, variante: 'padrao' | 'perigo' | 'principal', aoClicar }`.
- Eventos: `aoClicar(evento)` (vibra 8 ms). Métodos: `definirTexto`, `definirIcone`, `habilitar(bool)`. Devolve `{ el, ... }`.
- Uso: `criarBotaoGrande({ texto: 'Desligar', icone: 'power', variante: 'perigo', aoClicar: () => aviso.abrir() })`.

## cartao.js — `criarCartao`
Cartão `--surface`, canto 16, espaço interno 12. Só contêiner.
- Opções: `{ filhos: [HTMLElement], classe }`. Métodos: `definir([HTMLElement])`, `adicionar(HTMLElement)`, `limpar()`. Devolve `{ el, ... }`.
- Uso: `criarCartao({ filhos: [rotulo, medidor.el] })`.

## cartaoapp.js — `criarCartaoApp`
Cartão de um app do Som: avatar (tocar = mudo), nome, música, botãozinho da saída, barra de volume, Anterior/Pausa/Próxima. Mudo deixa o cartão a 55%.
- Opções: `{ nome, musica (texto ou null = sem música e some a linha de mídia), saida: { nome, icone }, volume, mudo, tocando, intervaloMs, esperaMs, aoVolume(valor, final), aoMudo(mudo), aoSaida(), aoAnterior(), aoPausa(tocando), aoProxima() }`.
- Eventos: `aoMudo` e `aoPausa` trazem o NOVO estado (o cartão já o mostra; se o PC discordar, chame `definirMudo` / `definirTocando`). `aoVolume` segue a regra da barra. `aoSaida()` costuma abrir uma folha.
- Métodos: `definirNome(texto)`, `definirMusica(texto | null)`, `definirSaida(nome, icone)`, `definirVolume(valor)` (respeita a espera da barra), `definirMudo(bool)`, `definirTocando(bool)`. Devolve `{ el, barra, ... }`.
- `data-sem-gesto`: a barra dentro dele tem; o resto do cartão não.
- Uso: `criarCartaoApp({ nome: 'Spotify', musica: 'Faixa', saida: { nome: 'Fones', icone: 'headphones' }, volume: 80, aoVolume: (v, f) => enviar({ t: 'volume', v }) })`.

## barra.js — `criarBarra`
Barra de volume ou brilho (D24): trilho de 4 px, botão de 22 px, número em mono à direita.
- Opções: `{ min = 0, max = 100, valor, rotulo (leitor de tela), mostrarNumero = true, formatar(valor) → texto, intervaloMs = 50, esperaMs = 500, aoMudar(valor, final) }`.
- Regra de envio (igual à página antiga): no máximo 1 `aoMudar` a cada `intervaloMs` enquanto arrasta (sempre com o último valor, `final = false`); ao soltar, `aoMudar(valor, true)`; o que vem do PC fica ignorado enquanto o dedo está na barra e por `esperaMs` depois de soltar.
- Métodos: `definir(valor)` (estado do PC; se a barra está retida guarda o último e aplica quando a espera acaba), `valor()`, `retida()`, `habilitar(bool)`. Devolve `{ el, input, ... }`.
- **`data-sem-gesto`: sim.**
- Uso: `const b = criarBarra({ rotulo: 'Volume', aoMudar: (v, final) => enviar({ t: 'volume', v }) }); b.definir(estado.vol);`

## chip.js — `criarChip` e `criarGrupoChips`
- `criarChip({ texto, icone, marcado, grande, aoClicar })`: pílula de 32 px (`grande` = 40 px). `marcado` indefinido = chip de ação; `true/false` = chip que pode ficar marcado (fundo `--accent`). Métodos: `marcar(bool)`, `definirTexto(texto)`, `habilitar(bool)`. Devolve `{ el, ... }`.
- `criarGrupoChips({ opcoes: [{ valor, texto, icone }], valor, aoEscolher(valor, indice) })`: escolha única (tempos do timer, modos). Métodos: `marcar(valor)` (não chama `aoEscolher`), `valor()`, `habilitar(bool)`. Devolve `{ el, ... }`.
- Uso: `criarGrupoChips({ opcoes: [{ valor: 15, texto: '15 min' }, { valor: 30, texto: '30 min' }], valor: 15, aoEscolher: (v) => enviar({ t: 'sono', min: v }) })`.

## interruptor.js — `criarInterruptor`
Interruptor 50 x 30 px; a linha inteira (rótulo + interruptor) é o alvo de toque de 44 px.
- Opções: `{ rotulo, ligado, aoMudar(ligado) }`. Sem `rotulo`, só o interruptor.
- Eventos: `aoMudar(ligado)` por toque. Métodos: `definir(bool)`, `valor()`, `habilitar(bool)`. Devolve `{ el, input, ... }`.
- Uso: `criarInterruptor({ rotulo: 'Manter a tela ligada', aoMudar: (v) => enviar({ t: 'acordado', on: v }) })`.

## medidor.js — `criarMedidor`
Barra de 8 px, só mostra (Estado do PC).
- Opções: `{ valor, max = 100, rotulo }`. Métodos: `definir(valor)`, `valor()`. Devolve `{ el, ... }`.
- Uso: `const m = criarMedidor({ rotulo: 'CPU' }); m.definir(estado.cpu);`

## aviso.js — `criarAviso`
Aviso de confirmação (D8 da Fase 13): cartão centralizado sobre o fundo escurecido; o foco começa em Cancelar; Esc e toque fora cancelam.
- Opções: `{ container, titulo, texto, rotuloAcao, perigo, aoConfirmar, aoCancelar }`. Use o `container` `camadas` da página.
- Eventos: `aoConfirmar()` e `aoCancelar()`. Métodos: `abrir()`, `fechar()`, `aberto()`, `destruir()` (remove o elemento). Devolve `{ el, ... }`.
- Uso: `const a = criarAviso({ container, titulo: 'Desligar?', texto: '...', rotuloAcao: 'Desligar', perigo: true, aoConfirmar: () => enviar({ t: 'energia', a: 'desligar' }) }); a.abrir();`

## folha.js — `criarFolha`
Folha de opções presa embaixo (escolher a saída de um app, a projeção...). Marcada = `--accent`.
- Opções: `{ container, titulo, opcoes: [{ valor, texto, icone }], valor (marcada), aoEscolher(valor, indice), aoFechar() }`.
- Eventos: `aoEscolher` ao tocar numa opção (a folha fecha); `aoFechar()` depois de qualquer fechamento (escolha, toque fora, Esc, `fechar()`).
- Métodos: `abrir()`, `fechar()`, `aberto()`, `marcar(valor)`, `definirOpcoes(opcoes, valor)`, `destruir()`. Devolve `{ el, ... }`. Crie ao precisar e chame `destruir()` em `aoFechar`, ou reaproveite a mesma com `definirOpcoes`.
- Uso: `const f = criarFolha({ container, titulo: 'Saída de áudio', opcoes: saidas, valor: atual, aoEscolher: (v) => enviar({ t: 'saida', id: v }), aoFechar: () => f.destruir() }); f.abrir();`

## toast.js — `criarToast`
Pílula no topo que some sozinha. Opções: `{ container }`. Métodos: `mostrar(texto, ms = 1800)`, `destruir()`. A página já tem um (o de `app.js`); as telas recebem o dela.

## dica.js — `criarDica`
Pílula embaixo (a dica de voltar). Opções: `{ container }`. Métodos: `mostrar(texto, ms = 3000)`, `esconder()`, `destruir()`. Quem decide o "uma vez só" é a tela.

## touchpad.js — `criarTouchpad`
Só os gestos (D25), sem protocolo. Regras da página antiga (`16-controle.js`): um dedo move (somado e entregue uma vez por quadro, com a sensibilidade); toque curto (menos de 350 ms e 10 px) = clique esquerdo; dois dedos juntos = clique direito; segurar 450 ms quase parado = arrastar com o esquerdo; dois dedos na vertical ou a faixa lateral = rolar (um passo a cada 24 px); botões Esquerdo/Direito na base (toque = clique; segurar ou mexer o dedo no painel enquanto apertado = arrastar).
- Opções: `{ sensibilidade = 1.6, botoes = true, faixa = true, dica, aoMover(dx, dy), aoClicar('esq' | 'dir'), aoRolar(dy), aoSegurar('esq' | 'dir', ligado), aoVisibilidade(visivel) }`.
- Eventos: `aoMover` (inteiros, uma vez por quadro), `aoClicar`, `aoRolar` (inteiro; positivo = dedos para baixo, o mesmo sinal da página antiga), `aoSegurar(botao, true)` ao começar e `(botao, false)` ao soltar, `aoVisibilidade` quando o painel deitado esconde ou aparece.
- Métodos: `definirSensibilidade(n)`, `definirDeitado(bool)`, `deitado()`, `esconder()`, `mostrar()`, `destruir()`. Devolve `{ el, painel, bolha, ... }`.
- Em pé: `el` ocupa o espaço do pai (flex 1, mínimo 280 px). **Deitado** (`definirDeitado(true)`): `el` cobre o pai, que precisa de `position: relative` e do tamanho da tela; o painel vira vidro (`--glass`) de 44% x 52%, arrasta pela alça do topo e esconde pela seta (vira a bolha no canto). Quem usa decide quando deitar (orientação do aparelho) e chama `definirDeitado`.
- **`data-sem-gesto`: sim.**
- Uso: `const p = criarTouchpad({ aoMover: (dx, dy) => enviar({ t: 'mouse', dx, dy }), aoClicar: (b) => enviar({ t: 'clique', b }), aoRolar: (dy) => enviar({ t: 'rolar', dx: 0, dy }), aoSegurar: (b, on) => enviar({ t: 'botao', b, baixo: on }) });`
