// Preferência "mão usada" (A5): direita (padrão) ou esquerda. Fica no localStorage e vira a classe `mao-esquerda` no <html>.
const CHAVE = 'mao';
const ouvintes = [];
let atual = null;

function lerMaoSalva() {
  try { return localStorage.getItem(CHAVE) === 'esquerda' ? 'esquerda' : 'direita'; } catch { return 'direita'; }
}
function aplicar(mao) {
  document.documentElement.classList.toggle('mao-esquerda', mao === 'esquerda');
}

// 'direita' ou 'esquerda'
export function lerMao() {
  if (atual === null) { atual = lerMaoSalva(); aplicar(atual); }
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

// Preferências genéricas (D6): cada área usa chaves com o próprio prefixo (`inicio.`, `tela.`...).
// ler(chave, padrao): o valor guardado (JSON) ou `padrao` se não houver, estiver estragado ou o armazenamento falhar.
export function ler(chave, padrao) {
  try {
    const v = localStorage.getItem(chave);
    return v === null ? padrao : JSON.parse(v);
  } catch { return padrao; }
}

// gravar(chave, valor): guarda o valor como JSON; sem armazenamento, não faz nada (devolve false).
export function gravar(chave, valor) {
  try { localStorage.setItem(chave, JSON.stringify(valor)); return true; } catch { return false; }
}
