// Interruptor (guia 4.5): 50 x 30 px; ligado = --accent. A linha inteira (rótulo e interruptor) é a área de toque, com 44 px de altura.

// opções: { rotulo (texto à esquerda; sem ele só o interruptor), ligado, aoMudar(ligado) }.
// Eventos: aoMudar(ligado) quando o usuário toca (definir não chama).
// Devolve { el, input, definir(bool), valor(), habilitar(bool) }.
export function criarInterruptor({ rotulo, ligado = false, aoMudar } = {}) {
  const el = document.createElement('label');
  el.className = 'interruptor';
  if (rotulo) {
    const t = document.createElement('span');
    t.textContent = rotulo;
    el.append(t);
  }
  const input = document.createElement('input');
  input.type = 'checkbox';
  input.className = 'tgl';
  input.setAttribute('role', 'switch');
  if (!rotulo) input.setAttribute('aria-label', 'Interruptor');
  input.checked = !!ligado;
  input.setAttribute('aria-checked', String(!!ligado));
  el.append(input);
  input.addEventListener('change', () => {
    input.setAttribute('aria-checked', String(input.checked));
    if (aoMudar) aoMudar(input.checked);
  });
  return {
    el,
    input,
    definir(v) { input.checked = !!v; input.setAttribute('aria-checked', String(!!v)); },
    valor() { return input.checked; },
    habilitar(v) { input.disabled = !v; },
  };
}
