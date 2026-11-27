// Tela Controle (Fase 15c, D15): touchpad, teclado (com Ctrl, Alt, Shift e Win presos), Apresentação, Vídeo e Mídia.
// Funções e mensagens da página antiga (página antiga, 16-controle.js): mouse, clique (esq, dir, duplo), botao (arrastar), rolar, texto,
// tecla e acordado. Contrato do ctx: telas/LEIAME.md.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarGrupoChips } from '../componentes/chip.js';
import { criarBotao } from '../componentes/botao.js';
import { criarBotaoGrande } from '../componentes/botaogrande.js';
import { criarInterruptor } from '../componentes/interruptor.js';
import { criarTouchpad } from '../componentes/touchpad.js';
import { criarTeclado, TECLAS_BASICAS, ATALHOS_RAPIDOS, TECLAS_F } from './teclado-pc.js';

const MODOS = [
  { valor: 'tp', texto: 'Touchpad' },
  { valor: 'kb', texto: 'Teclado' },
  { valor: 'pr', texto: 'Apresentação' },
  { valor: 'vd', texto: 'Vídeo' },
  { valor: 'md', texto: 'Mídia' },
];
const SENSIBILIDADES = [
  { valor: 1, texto: 'Baixa' },
  { valor: 1.6, texto: 'Média' },
  { valor: 2.6, texto: 'Alta' },
];

function div(classe) {
  const d = document.createElement('div');
  d.className = classe;
  return d;
}
function rotulo(texto) {
  const r = document.createElement('div');
  r.className = 'rotulo';
  r.textContent = texto;
  return r;
}

export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela controle';
  el.dataset.tela = 'controle';

  const cab = criarCabecalho({ titulo: 'Controle', aoVoltar: () => ctx.voltar() });
  const enviar = (o) => ctx.enviar(o);
  const teclado = criarTeclado({ enviar, camadas: ctx.camadas });

  // ---- modos ----
  const modos = criarGrupoChips({ opcoes: MODOS, valor: 'tp', aoEscolher: (m) => mostrar(m) });
  const faixaModos = div('ctl-modos');
  faixaModos.setAttribute('data-sem-gesto', '');
  faixaModos.append(modos.el);

  const corpo = div('ctl-corpo');
  const paineis = {};
  for (const m of MODOS) {
    const p = div('ctl-painel ctl-' + m.valor);
    p.hidden = m.valor !== 'tp';
    paineis[m.valor] = p;
    corpo.append(p);
  }

  // ---- touchpad ----
  let sens = Number(ctx.preferencias.ler('controle.sens', 1.6));
  if (!SENSIBILIDADES.some((s) => s.valor === sens)) sens = 1.6;
  let arrasto = null; // botão que está arrastado agora ('esq' | 'dir')
  const tp = criarTouchpad({
    sensibilidade: sens,
    aoMover: (dx, dy) => enviar({ t: 'mouse', dx, dy }),
    aoClicar: (b) => enviar({ t: 'clique', b }),
    aoRolar: (dy) => enviar({ t: 'rolar', dx: 0, dy }),
    aoSegurar: (b, ligado) => {
      arrasto = ligado ? b : null;
      enviar({ t: 'botao', b, baixo: ligado });
    },
  });
  const duplo = criarBotao({ texto: 'Clique duplo', aoClicar: () => enviar({ t: 'clique', b: 'esq', duplo: true }) });
  const sensGrupo = criarGrupoChips({
    opcoes: SENSIBILIDADES, valor: sens,
    aoEscolher: (v) => { sens = v; tp.definirSensibilidade(v); ctx.preferencias.gravar('controle.sens', v); },
  });
  const linhaSens = div('ctl-sens');
  const rotSens = document.createElement('span');
  rotSens.className = 'texto-sec';
  rotSens.textContent = 'Sensibilidade';
  linhaSens.append(rotSens, sensGrupo.el);
  paineis.tp.append(tp.el, duplo.el, linhaSens);

  // ---- teclado ----
  const texto = teclado.texto({ multilinha: true, rotuloBotao: 'Enviar texto' });
  const rotMods = rotulo('Modificadores (ficam presos até a próxima tecla)');
  const rotLetras = rotulo('Letras (com um modificador preso viram atalho; sozinhas são digitadas)');
  paineis.kb.append(
    texto.el,
    teclado.teclas(TECLAS_BASICAS),
    rotMods, teclado.mods,
    rotLetras, teclado.letras(),
    rotulo('Atalhos rápidos'), teclado.atalhos(ATALHOS_RAPIDOS),
    rotulo('Teclas de função'), teclado.atalhos(TECLAS_F),
  );

  // ---- Apresentação ----
  const tecla = (k) => () => enviar({ t: 'tecla', k });
  const prLinha = div('linha-botoes ctl-grandes');
  prLinha.append(
    criarBotaoGrande({ texto: 'Anterior', icone: 'prev', aoClicar: tecla('left') }).el,
    criarBotaoGrande({ texto: 'Próximo', icone: 'next', variante: 'principal', aoClicar: tecla('right') }).el,
  );
  const prBase = div('grade3 alta');
  prBase.append(
    criarBotaoGrande({ texto: 'Começar (F5)', icone: 'play', aoClicar: tecla('f5') }).el,
    criarBotaoGrande({ texto: 'Do slide atual', icone: 'play', aoClicar: tecla('shift+f5') }).el,
    criarBotaoGrande({ texto: 'Tela preta (B)', icone: 'monitor', aoClicar: tecla('b') }).el,
    criarBotaoGrande({ texto: 'Sair (Esc)', icone: 'x', aoClicar: tecla('esc') }).el,
  );
  paineis.pr.append(prLinha, prBase);

  // ---- Vídeo ----
  const vdPlay = criarBotaoGrande({ texto: 'Play/Pausa', icone: 'play', variante: 'principal', aoClicar: tecla('space') });
  vdPlay.el.classList.add('ctl-play');
  const vdSalto = div('linha-botoes');
  vdSalto.append(
    criarBotaoGrande({ texto: 'Voltar', icone: 'rew', aoClicar: tecla('left') }).el,
    criarBotaoGrande({ texto: 'Avançar', icone: 'fwd', aoClicar: tecla('right') }).el,
  );
  const vdVol = div('linha-botoes');
  vdVol.append(
    criarBotao({ texto: 'Vol −', icone: 'volume', aoClicar: tecla('down') }).el,
    criarBotao({ texto: 'Vol +', icone: 'volume', aoClicar: tecla('up') }).el,
  );
  const vdExtra = div('linha-botoes');
  vdExtra.append(
    criarBotao({ texto: 'Tela cheia (F)', icone: 'full', aoClicar: tecla('f') }).el,
    criarBotao({ texto: 'Mudo (M)', icone: 'mute', aoClicar: tecla('m') }).el,
  );
  paineis.vd.append(vdPlay.el, vdSalto, vdVol, vdExtra);

  // ---- Mídia (teclas de mídia do PC) ----
  const mdLinha = div('grade3');
  mdLinha.append(
    criarBotaoGrande({ texto: 'Anterior', icone: 'prev', aoClicar: tecla('prev') }).el,
    criarBotaoGrande({ texto: 'Play/Pausa', icone: 'play', variante: 'principal', aoClicar: tecla('play') }).el,
    criarBotaoGrande({ texto: 'Próxima', icone: 'next', aoClicar: tecla('next') }).el,
  );
  const mdVol = div('linha-botoes');
  mdVol.append(
    criarBotao({ texto: 'Vol −', icone: 'volume', aoClicar: tecla('voldown') }).el,
    criarBotao({ texto: 'Vol +', icone: 'volume', aoClicar: tecla('volup') }).el,
    criarBotao({ texto: 'Mudo', icone: 'mute', aoClicar: tecla('mute') }).el,
  );
  paineis.md.append(mdLinha, mdVol);

  // ---- manter a tela do PC ligada ----
  const acordado = criarInterruptor({
    rotulo: 'Manter a tela do PC ligada',
    aoMudar: (ligado) => enviar({ t: 'acordado', on: ligado }),
  });
  const cartaoAcordado = div('ctl-acordado');
  cartaoAcordado.append(acordado.el);

  el.append(cab.el, faixaModos, corpo, cartaoAcordado);

  function mostrar(modo) {
    for (const m of MODOS) paineis[m.valor].hidden = m.valor !== modo;
    teclado.limpar();
    soltarArrasto();
  }
  function soltarArrasto() {
    if (!arrasto) return;
    enviar({ t: 'botao', b: arrasto, baixo: false });
    arrasto = null;
  }

  function desenhar(estado) {
    acordado.definir(!!(estado && estado.pc && estado.pc.acordado));
  }
  desenhar(ctx.obterEstado());
  const cancelar = ctx.aoMudar(desenhar);

  return {
    el,
    aoTopo(sim) {
      if (!sim) { teclado.limpar(); soltarArrasto(); }
    },
    destruir() {
      cancelar();
      soltarArrasto();
      teclado.destruir();
      tp.destruir();
    },
  };
}
