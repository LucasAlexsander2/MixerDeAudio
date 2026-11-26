// Toast (guia 4.5): pílula no topo, fundo --fg e texto --bg, some sozinha.

// opções: { container }. Devolve { el, mostrar(texto, ms = 1800), destruir() }.
export function criarToast({ container }) {
  const el = document.createElement('div');
  el.className = 'toast';
  el.hidden = true;
  el.setAttribute('role', 'status');
  el.setAttribute('aria-live', 'polite');
  (container || document.body).append(el);

  let timer = 0;
  return {
    el,
    destruir() { clearTimeout(timer); el.remove(); },
    mostrar(texto, ms = 1800) {
      el.textContent = texto;
      el.hidden = false;
      clearTimeout(timer);
      timer = setTimeout(() => { el.hidden = true; }, ms);
    },
  };
}
