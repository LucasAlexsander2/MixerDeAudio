// Tela Opções (A5): mão usada. Os controles passam para o lado da mão escolhida.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarGrupoChips } from '../componentes/chip.js';
import { lerMao, definirMao, aoMudarMao } from '../preferencias.js';

export const OPCOES_MAO = [
  { valor: 'direita', texto: 'Direita' },
  { valor: 'esquerda', texto: 'Esquerda' },
];

// Grupo "Mão usada" (rótulo + chips), reaproveitado pela Galeria. Devolve { el }.
export function criarGrupoMao() {
  const el = document.createElement('div');
  const rotulo = document.createElement('div');
  rotulo.className = 'rotulo';
  rotulo.textContent = 'Mão usada';
  const grupo = criarGrupoChips({ opcoes: OPCOES_MAO, valor: lerMao(), aoEscolher: (v) => definirMao(v) });
  aoMudarMao((m) => grupo.marcar(m));
  el.append(rotulo, grupo.el);
  return { el };
}

// opções: { aoVoltar }. Devolve o elemento da tela.
export function criarTelaOpcoes({ aoVoltar }) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'opcoes';
  const cab = criarCabecalho({ titulo: 'Opções', aoVoltar });
  const texto = document.createElement('p');
  texto.className = 'texto-sec';
  texto.textContent = 'Os controles passam para o lado da mão escolhida.';
  el.append(cab.el, criarGrupoMao().el, texto);
  return el;
}
