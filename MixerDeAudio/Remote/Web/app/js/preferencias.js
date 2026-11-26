// Preferência "mão usada" (A5): direita (padrão) ou esquerda. Fica no localStorage e vira a classe `mao-esquerda` no <html>.
const CHAVE = 'mao';
const ouvintes = [];
let atual = null;

function ler() {
  try { return localStorage.getItem(CHAVE) === 'esquerda' ? 'esquerda' : 'direita'; } catch { return 'direita'; }
}
function aplicar(mao) {
  document.documentElement.classList.toggle('mao-esquerda', mao === 'esquerda');
}

// 'direita' ou 'esquerda'
export function lerMao() {
  if (atual === null) { atual = ler(); aplicar(atual); }
  return atual;
}

export function definirMao(mao) {
  atual = mao === 'esquerda' ? 'esquerda' : 'direita';
  try { localStorage.setItem(CHAVE, atual); } catch { /* sem armazenamento */ }
  aplicar(atual);
  ouvintes.forEach((fn) => fn(atual));
}

// fn(mao) a cada mudança; devolve a função que cancela.
export function aoMudarMao(fn) {
  ouvintes.push(fn);
  return () => { const i = ouvintes.indexOf(fn); if (i >= 0) ouvintes.splice(i, 1); };
}
