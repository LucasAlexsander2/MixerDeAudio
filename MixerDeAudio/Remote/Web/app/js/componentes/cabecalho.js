// Cabeçalho (guia 4.5): 52 px, seta de voltar de 44 px, título 18/600 e ações à direita.
import { icone } from '../icones.js';

// opções: { titulo, aoVoltar, acoes: [HTMLElement] }. Devolve { el, definirTitulo(texto) }.
export function criarCabecalho({ titulo, aoVoltar, acoes = [] }) {
  const el = document.createElement('header');
  el.className = 'cabecalho';

  const voltar = document.createElement('button');
  voltar.type = 'button';
  voltar.className = 'voltar';
  voltar.setAttribute('aria-label', 'Voltar');
  voltar.innerHTML = '<i class="ic">' + icone('back') + '</i>';
  voltar.addEventListener('click', () => { if (aoVoltar) aoVoltar(); });

  const h1 = document.createElement('h1');
  h1.textContent = titulo;

  el.append(voltar, h1);
  if (acoes.length) {
    const grupo = document.createElement('div');
    grupo.className = 'acoes';
    grupo.append(...acoes);
    el.append(grupo);
  }
  return { el, definirTitulo(texto) { h1.textContent = texto; } };
}
