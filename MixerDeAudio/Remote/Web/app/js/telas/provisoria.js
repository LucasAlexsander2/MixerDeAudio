// Tela interna provisória de cada área (D19): cabeçalho com a seta e o aviso de que a área chega na Fase 15.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { icone } from '../icones.js';

// opções: { titulo, icone, aoVoltar }. Devolve o elemento da tela.
export function criarTelaProvisoria({ titulo, icone: nomeIcone, aoVoltar }) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = titulo;

  const cab = criarCabecalho({ titulo, aoVoltar });
  const corpo = document.createElement('div');
  corpo.className = 'provisoria';
  corpo.innerHTML = '<i class="ic">' + icone(nomeIcone) + '</i>';
  const p = document.createElement('p');
  p.className = 'texto-sec';
  p.textContent = 'Chega na Fase 15.';
  corpo.append(p);

  el.append(cab.el, corpo);
  return el;
}
