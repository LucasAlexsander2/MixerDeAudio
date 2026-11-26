// Medidor (guia 4.5): barra de 8 px, cheia em --accent. Só mostra; não responde ao toque.

// opções: { valor = 0, max = 100, rotulo (texto para leitor de tela) }. Devolve { el, definir(valor), valor() }.
export function criarMedidor({ valor = 0, max = 100, rotulo = 'Medidor' } = {}) {
  const el = document.createElement('div');
  el.className = 'medidor';
  el.setAttribute('role', 'progressbar');
  el.setAttribute('aria-label', rotulo);
  el.setAttribute('aria-valuemin', '0');
  el.setAttribute('aria-valuemax', String(max));
  const cheio = document.createElement('i');
  el.append(cheio);
  let atual = 0;
  const api = {
    el,
    definir(v) {
      atual = Math.min(max, Math.max(0, +v || 0));
      cheio.style.width = (max > 0 ? (atual / max) * 100 : 0) + '%';
      el.setAttribute('aria-valuenow', String(Math.round(atual)));
    },
    valor() { return atual; },
  };
  api.definir(valor);
  return api;
}
