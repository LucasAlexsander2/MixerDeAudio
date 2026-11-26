// Botão (guia 4.5): 48 px, fundo em --surface-2; variantes perigo e principal; "largo" é o botão de rodapé do Início.
import { icone } from '../icones.js';
import { vibrar } from '../util.js';

// opções: { texto, icone, variante: 'padrao' | 'perigo' | 'principal', largo, aoClicar }.
// Eventos: aoClicar(evento) no toque (vibra 8 ms). Em linha (classe .linha-botoes) os botões dividem a largura.
// Devolve { el, definirTexto(texto), definirIcone(nome), habilitar(bool), desativar(bool) }.
export function criarBotao({ texto, icone: nomeIcone, variante = 'padrao', largo = false, aoClicar }) {
  const el = document.createElement('button');
  el.type = 'button';
  el.className = 'botao';
  if (variante === 'perigo') el.classList.add('perigo');
  if (variante === 'principal') el.classList.add('principal');
  if (largo) el.classList.add('largo');

  const ic = document.createElement('i');
  ic.className = 'ic';
  if (nomeIcone) ic.innerHTML = icone(nomeIcone); else ic.hidden = true;
  const rotulo = document.createElement('span');
  rotulo.textContent = texto;
  el.append(ic, rotulo);

  el.addEventListener('click', (e) => { vibrar(); if (aoClicar) aoClicar(e); });
  return {
    el,
    definirTexto(t) { rotulo.textContent = t; },
    definirIcone(n) { ic.innerHTML = icone(n); ic.hidden = false; },
    habilitar(v) { el.disabled = !v; },
    desativar(v) { el.disabled = !!v; },
  };
}
