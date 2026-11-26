// Bloco (guia 4.5): 100 x 76 px, ícone de 24 px e nome curto; ligado = borda e texto em destaque; ponto verde = ativo.
import { icone } from '../icones.js';
import { vibrar } from '../util.js';

// opções: { nome, icone, ligado, ativo, aoClicar }.
// Eventos: aoClicar(evento) no toque (vibra 8 ms).
// Devolve { el, definirLigado(bool), definirAtivo(bool), definirIcone(nome), definirNome(texto), habilitar(bool) }.
// Para ser um "bloco de escolha" marque com definirLigado; para só indicar atividade use definirAtivo.
export function criarBloco({ nome, icone: nomeIcone, ligado = false, ativo = false, aoClicar }) {
  const el = document.createElement('button');
  el.type = 'button';
  el.className = 'bloco';

  const ic = document.createElement('i');
  ic.className = 'ic';
  ic.innerHTML = icone(nomeIcone);
  const rotulo = document.createElement('span');
  rotulo.textContent = nome;
  const ponto = document.createElement('span');
  ponto.className = 'ponto';
  el.append(ic, rotulo, ponto);

  const api = {
    el,
    definirLigado(v) { el.classList.toggle('ligado', !!v); el.setAttribute('aria-pressed', String(!!v)); },
    definirAtivo(v) { ponto.hidden = !v; },
    definirIcone(n) { ic.innerHTML = icone(n); },
    definirNome(t) { rotulo.textContent = t; },
    habilitar(v) { el.disabled = !v; },
  };
  api.definirLigado(ligado);
  api.definirAtivo(ativo);
  el.addEventListener('click', (e) => { vibrar(); if (aoClicar) aoClicar(e); });
  return api;
}
