// Botão grande (guia 4.5): mínimo 96 px de altura, ícone de 32 px e texto 16/700; variantes perigo e principal.
import { icone } from '../icones.js';
import { vibrar } from '../util.js';

// opções: { texto, icone, variante: 'padrao' | 'perigo' | 'principal', aoClicar }.
// Eventos: aoClicar(evento) no toque (vibra 8 ms).
// Devolve { el, definirTexto(texto), definirIcone(nome), habilitar(bool) }.
export function criarBotaoGrande({ texto, icone: nomeIcone, variante = 'padrao', aoClicar }) {
  const el = document.createElement('button');
  el.type = 'button';
  el.className = 'botao-grande';
  if (variante === 'perigo') el.classList.add('perigo');
  if (variante === 'principal') el.classList.add('principal');

  const ic = document.createElement('i');
  ic.className = 'ic';
  if (nomeIcone) ic.innerHTML = icone(nomeIcone);
  const rotulo = document.createElement('span');
  rotulo.textContent = texto;
  el.append(ic, rotulo);

  el.addEventListener('click', (e) => { vibrar(); if (aoClicar) aoClicar(e); });
  return {
    el,
    definirTexto(t) { rotulo.textContent = t; },
    definirIcone(n) { ic.innerHTML = icone(n); },
    habilitar(v) { el.disabled = !v; },
  };
}
