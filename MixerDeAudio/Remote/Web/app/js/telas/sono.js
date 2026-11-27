// Tela Timer de sono (Fase 15b, D14): programa a hora em que a música para e mostra a contagem num anel.
// Mensagens (página antiga, 14-sono.js): { t:'sono', min } (5 a 240) e { t:'sono', min:0 } para cancelar; o plano vem em estado.pc.sono { rest } (nulo = sem timer).
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarBotao } from '../componentes/botao.js';
import { criarGrupoChips } from '../componentes/chip.js';

export const MINUTOS = [
  [5, '5 min'], [10, '10 min'], [15, '15 min'], [30, '30 min'], [45, '45 min'],
  [60, '1 h'], [90, '1 h 30'], [120, '2 h'], [180, '3 h'], [240, '4 h'],
].map(([valor, texto]) => ({ valor, texto }));

const CIRCUNFERENCIA = 282.74; // 2 * pi * 45 (raio do anel do SVG)

// 90 -> "1:30"; 3700 -> "1:01:40".
export function duracao(n) {
  const h = Math.floor(n / 3600), mi = Math.floor((n % 3600) / 60), s = n % 60;
  const p = (x) => (x < 10 ? '0' + x : '' + x);
  return (h ? h + ':' + p(mi) : mi) + ':' + p(s);
}

function rotulo(texto) {
  const d = document.createElement('div');
  d.className = 'rotulo';
  d.textContent = texto;
  return d;
}

export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'sono';
  const cab = criarCabecalho({ titulo: 'Timer de sono', aoVoltar: () => ctx.voltar() });

  // Programar
  let minutos = 30;
  const chips = criarGrupoChips({ opcoes: MINUTOS, valor: minutos, aoEscolher: (v) => { minutos = v; } });
  const iniciar = criarBotao({
    texto: 'Iniciar', variante: 'principal',
    aoClicar: () => { total = minutos * 60; ctx.enviar({ t: 'sono', min: minutos }); },
  });
  iniciar.el.classList.add('sono-iniciar');
  const aviso = document.createElement('p');
  aviso.className = 'texto-sec';
  aviso.textContent = 'Nos últimos 30 segundos o volume desce até zero, a música pausa e o volume volta ao que era.';
  const preparo = document.createElement('div');
  preparo.append(rotulo('Parar a música em'), chips.el, iniciar.el, aviso);

  // Contando
  const corrida = document.createElement('div');
  corrida.hidden = true;
  const anel = document.createElement('div');
  anel.className = 'sono-anel';
  anel.innerHTML = '<svg viewBox="0 0 100 100" aria-hidden="true"><circle class="sono-trilho" cx="50" cy="50" r="45"/><circle class="sono-avanco" cx="50" cy="50" r="45"/></svg>';
  const avanco = anel.querySelector('.sono-avanco');
  const tempo = document.createElement('div');
  tempo.className = 'mono sono-tempo';
  anel.append(tempo);
  const info = document.createElement('p');
  info.className = 'texto-sec sono-info';
  info.textContent = 'A música para quando chegar a zero.';
  const cancelar = criarBotao({ texto: 'Cancelar', variante: 'perigo', aoClicar: () => ctx.enviar({ t: 'sono', min: 0 }) });
  const linha = document.createElement('div');
  linha.className = 'linha-botoes';
  linha.append(cancelar.el);
  corrida.append(anel, info, linha);

  el.append(cab.el, preparo, corrida);

  let plano = null; // { em } instante local (ms) em que a música para
  let total = 0;    // duração total (s), para o anel
  let tick = 0;
  function atualizar() {
    preparo.hidden = !!plano;
    corrida.hidden = !plano;
    if (!plano) { clearInterval(tick); tick = 0; return; }
    const falta = Math.max(0, Math.round((plano.em - Date.now()) / 1000));
    if (falta > total) total = falta;
    tempo.textContent = duracao(falta);
    avanco.style.strokeDashoffset = String(CIRCUNFERENCIA * (1 - (total > 0 ? falta / total : 0)));
  }
  function aplicar(estado) {
    const s = estado && estado.pc ? estado.pc.sono : null;
    if (s) {
      plano = { em: Date.now() + (s.rest || 0) * 1000 };
      if (!tick) tick = setInterval(atualizar, 1000);
    } else {
      plano = null;
      total = 0;
    }
    atualizar();
  }
  const soltarEstado = ctx.aoMudar(aplicar);
  aplicar(ctx.obterEstado());

  return {
    el,
    destruir() {
      soltarEstado();
      clearInterval(tick);
      tick = 0;
    },
  };
}
