// Último estado recebido do PC e quem quer saber quando ele muda (D11).
let ultimo = null;
const ouvintes = new Set();

export function obterEstado() {
  return ultimo;
}

export function definirEstado(estado) {
  ultimo = estado;
  for (const fn of [...ouvintes]) {
    try { fn(estado); } catch (e) { console.error(e); }
  }
}

export function limparEstado() {
  ultimo = null;
}

// Registra um ouvinte e devolve a função que o remove.
export function aoMudar(fn) {
  ouvintes.add(fn);
  return () => ouvintes.delete(fn);
}
