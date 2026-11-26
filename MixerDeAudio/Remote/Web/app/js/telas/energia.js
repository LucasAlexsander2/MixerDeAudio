// Tela Energia (Fase 15b, D14): desligar, reiniciar, suspender e bloquear (com confirmação), agendamento com contagem e Cancelar.
// Mensagens (antigo/09-pc.js): { t:'energia', acao, seg } e { t:'energiaCancelar' }; o agendamento vem em estado.pc.energia { acao, rest }.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarBotaoGrande } from '../componentes/botaogrande.js';
import { criarBotao } from '../componentes/botao.js';
import { criarGrupoChips } from '../componentes/chip.js';
import { criarCartao } from '../componentes/cartao.js';
import { criarAviso } from '../componentes/aviso.js';

export const ACOES = [
  { valor: 'desligar', texto: 'Desligar', icone: 'power', perigo: true, titulo: 'Desligar o PC', frase: 'O computador será desligado.', feito: 'Desligando o PC' },
  { valor: 'reiniciar', texto: 'Reiniciar', icone: 'restart', perigo: true, titulo: 'Reiniciar o PC', frase: 'O computador será reiniciado.', feito: 'Reiniciando o PC' },
  { valor: 'suspender', texto: 'Suspender', icone: 'moon', perigo: false, titulo: 'Suspender o PC', frase: 'O computador entrará em suspensão. Depois de suspenso, não dá para acordá-lo pelo celular.', feito: 'Suspendendo o PC' },
  { valor: 'bloquear', texto: 'Bloquear', icone: 'lock', perigo: false, titulo: 'Bloquear o PC', frase: 'Será preciso a senha para voltar.', feito: 'PC bloqueado' },
];

// Tempos do "Quando" (os mesmos da página antiga; 0 = agora). Valores em segundos.
export const TEMPOS = [
  [0, 'Agora'], [60, '1 min'], [300, '5 min'], [600, '10 min'], [900, '15 min'], [1800, '30 min'], [2700, '45 min'],
  [3600, '1 h'], [5400, '1 h 30'], [7200, '2 h'], [10800, '3 h'], [14400, '4 h'], [28800, '8 h'], [43200, '12 h'], [86400, '24 h'],
].map(([valor, texto]) => ({ valor, texto }));

const NOME_TEMPO = new Map(TEMPOS.map((t) => [t.valor, t.texto]));

// 90 -> "1:30"; 3700 -> "1:01:40".
export function duracao(n) {
  const h = Math.floor(n / 3600), mi = Math.floor((n % 3600) / 60), s = n % 60;
  const p = (x) => (x < 10 ? '0' + x : '' + x);
  return (h ? h + ':' + p(mi) : mi) + ':' + p(s);
}

export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'energia';
  const cab = criarCabecalho({ titulo: 'Energia', aoVoltar: () => ctx.voltar() });

  // Aviso de confirmação; um só por vez, destruído ao abrir o próximo.
  let aviso = null;
  function perguntar(a, seg) {
    if (aviso) aviso.destruir();
    const quando = seg ? ' em ' + (NOME_TEMPO.get(seg) || seg + ' s') : '';
    aviso = criarAviso({
      container: ctx.camadas,
      titulo: a.titulo + quando + '?',
      texto: a.frase + (seg ? ' Dá para cancelar antes da hora.' : ''),
      rotuloAcao: seg ? 'Agendar' : a.texto,
      perigo: a.perigo,
      aoConfirmar: () => {
        ctx.enviar({ t: 'energia', acao: a.valor, seg });
        ctx.toast(seg ? a.texto + ' agendado' : a.feito);
      },
    });
    aviso.abrir();
  }

  const grade = document.createElement('div');
  grade.className = 'energia-grade';
  ACOES.forEach((a) => {
    grade.append(criarBotaoGrande({
      texto: a.texto, icone: a.icone, variante: a.perigo ? 'perigo' : 'padrao',
      aoClicar: () => perguntar(a, 0),
    }).el);
  });

  const rotAgendar = document.createElement('div');
  rotAgendar.className = 'rotulo';
  rotAgendar.textContent = 'Agendar';
  let tempo = 1800;
  let acao = 'desligar';
  const chipsTempo = criarGrupoChips({ opcoes: TEMPOS, valor: tempo, aoEscolher: (v) => { tempo = v; } });
  const rotAcao = document.createElement('div');
  rotAcao.className = 'rotulo';
  rotAcao.textContent = 'Ação';
  const chipsAcao = criarGrupoChips({
    opcoes: ACOES.map((a) => ({ valor: a.valor, texto: a.texto })), valor: acao, aoEscolher: (v) => { acao = v; },
  });
  const ir = criarBotao({
    texto: 'Agendar', variante: 'principal',
    aoClicar: () => perguntar(ACOES.find((a) => a.valor === acao), tempo),
  });
  ir.el.classList.add('energia-agendar');

  // Cartão do que está agendado (estado.pc.energia), com a contagem e o Cancelar.
  const txt = document.createElement('div');
  txt.className = 'mono energia-contagem';
  const sub = document.createElement('div');
  sub.className = 'texto-sec';
  sub.textContent = 'Agendado';
  const textos = document.createElement('div');
  textos.append(sub, txt);
  const cancelar = criarBotao({ texto: 'Cancelar', variante: 'perigo', aoClicar: () => ctx.enviar({ t: 'energiaCancelar' }) });
  const cartao = criarCartao({ classe: 'energia-plano', filhos: [textos, cancelar.el] });
  cartao.el.hidden = true;

  el.append(cab.el, grade, rotAgendar, chipsTempo.el, rotAcao, chipsAcao.el, ir.el, cartao.el);

  let plano = null; // { acao, em } com em = instante local (ms) em que acontece
  let tick = 0;
  function atualizarContagem() {
    if (!plano) { cartao.el.hidden = true; clearInterval(tick); tick = 0; return; }
    const falta = Math.max(0, Math.round((plano.em - Date.now()) / 1000));
    const a = ACOES.find((x) => x.valor === plano.acao);
    txt.textContent = (a ? a.texto : plano.acao) + ' em ' + duracao(falta);
    cartao.el.hidden = false;
  }
  function aplicar(estado) {
    const e = estado && estado.pc ? estado.pc.energia : null;
    if (e) {
      plano = { acao: e.acao, em: Date.now() + (e.rest || 0) * 1000 };
      if (!tick) tick = setInterval(atualizarContagem, 1000);
    } else plano = null;
    atualizarContagem();
  }
  const soltarEstado = ctx.aoMudar(aplicar);
  aplicar(ctx.obterEstado());

  return {
    el,
    destruir() {
      soltarEstado();
      clearInterval(tick);
      tick = 0;
      if (aviso) aviso.destruir();
    },
  };
}
