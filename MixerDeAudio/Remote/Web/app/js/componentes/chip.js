// Chip (guia 4.5): pílula de 32 px; em grupo de escolha 40 px; marcado = fundo --accent.
import { icone } from '../icones.js';

// criarChip({ texto, icone, marcado, grande, aoClicar(evento) }).
//  - `marcado` indefinido = chip de ação (sem estado, como os do cabeçalho); true/false = chip que pode ficar marcado.
//  - `grande` usa a altura de 40 px do grupo de escolha.
// Devolve { el, marcar(bool), definirTexto(texto), habilitar(bool) }.
export function criarChip({ texto, icone: nomeIcone, marcado, grande = false, aoClicar } = {}) {
  const el = document.createElement('button');
  el.type = 'button';
  el.className = 'chip' + (grande ? ' grande' : '');
  if (nomeIcone) {
    const ic = document.createElement('i');
    ic.className = 'ic';
    ic.innerHTML = icone(nomeIcone);
    el.append(ic);
  }
  const rotulo = document.createElement('span');
  rotulo.textContent = texto;
  el.append(rotulo);
  if (marcado !== undefined) el.setAttribute('aria-pressed', String(!!marcado));
  if (aoClicar) el.addEventListener('click', aoClicar);
  return {
    el,
    marcar(v) { el.setAttribute('aria-pressed', String(!!v)); },
    definirTexto(t) { rotulo.textContent = t; },
    habilitar(v) { el.disabled = !v; },
  };
}

// criarGrupoChips({ opcoes: [{ valor, texto, icone }], valor, aoEscolher(valor, indice) }): escolha única (tempos do timer, modos).
// Devolve { el, marcar(valor), valor(), habilitar(bool) }. `marcar` não chama aoEscolher (é o estado vindo do PC).
export function criarGrupoChips({ opcoes = [], valor, aoEscolher } = {}) {
  const el = document.createElement('div');
  el.className = 'chips';
  el.setAttribute('role', 'group');
  let atual = valor;
  const chips = opcoes.map((o, i) => {
    const c = criarChip({
      texto: o.texto, icone: o.icone, marcado: o.valor === valor, grande: true,
      aoClicar: () => { marcar(o.valor); if (aoEscolher) aoEscolher(o.valor, i); },
    });
    c.el.dataset.valor = String(o.valor);
    return c;
  });
  function marcar(v) {
    atual = v;
    chips.forEach((c, i) => c.marcar(opcoes[i].valor === v));
  }
  el.append(...chips.map((c) => c.el));
  return {
    el,
    marcar,
    valor() { return atual; },
    habilitar(v) { chips.forEach((c) => c.habilitar(v)); },
  };
}
