// Barra (guia 4.5, D24): trilho de 4 px, botão de 22 px, área de toque de 32 px e número em mono à direita.
// Mesma regra de envio da página antiga (antigo/07-sliders.js):
//  - no máximo 1 aoMudar a cada `intervaloMs` (50 ms) enquanto arrasta, sempre com o último valor;
//  - ao soltar, aoMudar(valor, true) com o valor final;
//  - o valor que vem do PC (definir) é ignorado enquanto o dedo está na barra e por `esperaMs` (500 ms) depois de soltar.

// opções: { min = 0, max = 100, valor = 0, rotulo = 'Barra' (texto para leitor de tela), mostrarNumero = true,
//           formatar(valor) -> texto, intervaloMs = 50, esperaMs = 500, aoMudar(valor, final) }.
// Devolve { el, input, definir(valor), valor(), habilitar(bool), retida() }.
//  - definir(v): atualiza a barra com o que veio do PC; se estiver retida guarda o último valor e aplica quando a espera acabar.
//  - retida(): true enquanto o dedo está nela ou durante a espera depois de soltar.
// Marcada com data-sem-gesto (arrastar a barra não volta a tela).
export function criarBarra({ min = 0, max = 100, valor = 0, rotulo = 'Barra', mostrarNumero = true, formatar, intervaloMs = 50, esperaMs = 500, aoMudar } = {}) {
  const el = document.createElement('div');
  el.className = 'barra';
  el.setAttribute('data-sem-gesto', '');

  const input = document.createElement('input');
  input.type = 'range';
  input.min = String(min);
  input.max = String(max);
  input.step = '1';
  input.setAttribute('aria-label', rotulo);
  el.append(input);

  const num = document.createElement('output');
  num.className = 'num';
  num.hidden = !mostrarNumero;
  el.append(num);

  let retidoAte = 0;          // 0 livre; Infinity = dedo na barra; senão, instante em que a espera acaba
  let adiado = null;          // último valor do PC recebido durante a espera
  let timerAdiado = 0;
  let ultimoEnvio = 0;
  let pendente = null;
  let timerEnvio = 0;
  let sujo = false;           // houve `input` desde o último valor final

  const fmt = (v) => (formatar ? formatar(v) : String(v));
  function pintar() {
    const v = +input.value;
    num.textContent = fmt(v);
    el.style.setProperty('--p', (max > min ? ((v - min) / (max - min)) * 100 : 0) + '%');
  }
  const retida = () => Date.now() < retidoAte;

  function enviar() {
    clearTimeout(timerEnvio);
    timerEnvio = 0;
    ultimoEnvio = Date.now();
    if (pendente !== null) { const v = pendente; pendente = null; if (aoMudar) aoMudar(v, false); }
  }
  function emitir(final) {
    const v = +input.value;
    pintar();
    if (final) {
      clearTimeout(timerEnvio);
      timerEnvio = 0;
      pendente = null;
      ultimoEnvio = Date.now();
      sujo = false;
      if (aoMudar) aoMudar(v, true);
      return;
    }
    pendente = v;
    const espera = intervaloMs - (Date.now() - ultimoEnvio);
    if (espera <= 0) enviar();
    else if (!timerEnvio) timerEnvio = setTimeout(enviar, espera);
  }
  function aplicar(v) {
    const n = Math.min(max, Math.max(min, Math.round(+v)));
    if (+input.value !== n) input.value = String(n);
    pintar();
  }
  function soltar() {
    retidoAte = Date.now() + esperaMs;
    clearTimeout(timerAdiado);
    timerAdiado = setTimeout(() => {
      if (!retida() && adiado !== null) { const v = adiado; adiado = null; aplicar(v); }
    }, esperaMs + 20);
  }
  const fim = () => { if (sujo) emitir(true); soltar(); };

  input.addEventListener('pointerdown', () => { retidoAte = Infinity; });
  input.addEventListener('input', () => {
    sujo = true;
    if (retidoAte !== Infinity) retidoAte = Date.now() + esperaMs; // teclado: sem pointerdown
    emitir(false);
  });
  input.addEventListener('change', fim);
  input.addEventListener('pointerup', fim);
  input.addEventListener('pointercancel', fim);

  aplicar(valor);
  return {
    el,
    input,
    valor() { return +input.value; },
    retida,
    definir(v) {
      if (retida()) { adiado = v; return; }
      adiado = null;
      aplicar(v);
    },
    habilitar(v) { input.disabled = !v; el.classList.toggle('desabilitada', !v); },
  };
}
