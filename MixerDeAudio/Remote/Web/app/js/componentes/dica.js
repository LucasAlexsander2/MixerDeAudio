// Dica (guia 4.5): pílula embaixo, aparece uma vez ("Arraste para o lado para voltar"). Quem decide "uma vez" é a tela que a usa.

// opções: { container }. Devolve { el, mostrar(texto, ms = 3000), esconder(), destruir() }.
export function criarDica({ container }) {
  const el = document.createElement('div');
  el.className = 'dica';
  el.hidden = true;
  el.setAttribute('role', 'note');
  (container || document.body).append(el);

  let timer = 0;
  const api = {
    el,
    mostrar(texto, ms = 3000) {
      el.textContent = texto;
      el.hidden = false;
      clearTimeout(timer);
      timer = setTimeout(() => { el.hidden = true; }, ms);
    },
    esconder() { clearTimeout(timer); el.hidden = true; },
    destruir() { clearTimeout(timer); el.remove(); },
  };
  return api;
}
