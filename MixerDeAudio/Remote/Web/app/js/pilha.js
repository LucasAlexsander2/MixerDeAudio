// Pilha de telas (D12, D13): telas registradas por nome, voltar pela seta, pelo gesto de arrastar para a esquerda
// e pelo histórico do navegador (voltar do Android, borda do Safari, "avançar").

const DURACAO = 280;

// opções: { raiz, inicial: HTMLElement, bloqueado: () => bool, aoAbrir(nome, profundidade) }.
// Devolve { registrar(nome, fabrica), abrir(nome), voltar(), reiniciar(), profundidade(), topo() }.
// A fábrica devolve o elemento da tela (uma seção com a classe "tela"); ele é criado ao abrir e removido ao fechar.
export function criarPilha({ raiz, inicial, bloqueado = () => false, aoAbrir }) {
  const fabricas = new Map();
  const pilha = [{ nome: 'inicio', el: inicial }];
  raiz.append(inicial);
  let ignorarPop = 0;

  const reduzir = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const largura = () => raiz.clientWidth || window.innerWidth;
  const topo = () => pilha[pilha.length - 1];

  function mover(el, x, animar) {
    el.style.transition = animar && !reduzir() ? 'transform ' + DURACAO + 'ms cubic-bezier(.2,.8,.2,1)' : 'none';
    el.style.transform = 'translate3d(' + x + 'px,0,0)';
  }

  function atualizarInerte() {
    pilha.forEach((t, i) => { t.el.inert = i < pilha.length - 1; });
  }

  function empilhar(nome) {
    const f = fabricas.get(nome);
    if (!f) return false;
    const el = f();
    raiz.append(el);
    mover(el, largura(), false);
    void el.offsetWidth;
    mover(el, 0, true);
    pilha.push({ nome, el });
    atualizarInerte();
    if (aoAbrir) aoAbrir(nome, pilha.length - 1);
    return true;
  }

  function desempilhar() {
    if (pilha.length < 2) return;
    const { el } = pilha.pop();
    atualizarInerte();
    mover(el, -largura(), true);
    el.inert = true;
    setTimeout(() => el.remove(), reduzir() ? 0 : DURACAO + 40);
  }

  const api = {
    registrar(nome, fabrica) { fabricas.set(nome, fabrica); },
    profundidade() { return pilha.length - 1; },
    topo() { return topo().nome; },
    abrir(nome) {
      if (!fabricas.has(nome) || topo().nome === nome) return;
      history.pushState({ n: pilha.length, tela: nome }, '');
      empilhar(nome);
    },
    voltar() {
      if (pilha.length > 1) history.back();
    },
    // Volta ao Início sem animação (ex.: o PC desconectou o celular).
    reiniciar() {
      const n = pilha.length - 1;
      if (n <= 0) return;
      while (pilha.length > 1) pilha.pop().el.remove();
      atualizarInerte();
      ignorarPop++;
      history.go(-n);
    },
  };

  // histórico: concilia a pilha com history.state.n
  window.addEventListener('popstate', (e) => {
    if (ignorarPop > 0) { ignorarPop--; return; }
    const n = (e.state && e.state.n) || 0;
    while (pilha.length - 1 > n) desempilhar();
    if (pilha.length - 1 < n) {
      // "avançar": reabre a tela guardada; qualquer outro salto volta ao Início
      if (n === pilha.length && e.state && empilhar(e.state.tela)) return;
      ignorarPop++;
      history.go(-n);
    }
  });

  // ao carregar numa tela interna (recarregar a página): volta ao Início
  const estadoInicial = history.state;
  if (estadoInicial && estadoInicial.n > 0) {
    ignorarPop++;
    history.go(-estadoInicial.n);
  } else if (!estadoInicial || estadoInicial.n !== 0) {
    history.replaceState({ n: 0 }, '');
  }

  // gesto de voltar (D13)
  let g = null;
  raiz.addEventListener('pointerdown', (e) => {
    if (pilha.length < 2 || !e.isPrimary || bloqueado()) return;
    if (e.target.closest('input, textarea, select, [data-sem-gesto]')) return;
    g = { x: e.clientX, y: e.clientY, t: e.timeStamp, ativo: false, id: e.pointerId };
  });
  raiz.addEventListener('pointermove', (e) => {
    if (!g || e.pointerId !== g.id) return;
    const dx = e.clientX - g.x;
    const dy = e.clientY - g.y;
    if (!g.ativo) {
      if (Math.abs(dx) < 8 && Math.abs(dy) < 8) return;
      if (dx < 0 && Math.abs(dx) > Math.abs(dy) * 1.2 && !bloqueado()) {
        g.ativo = true;
        try { raiz.setPointerCapture(e.pointerId); } catch { /* sem captura */ }
      } else { g = null; return; }
    }
    mover(topo().el, Math.min(0, dx), false);
  });
  function fimGesto(e) {
    if (!g || e.pointerId !== g.id) return;
    const s = g;
    g = null;
    if (!s.ativo) return;
    const dx = e.clientX - s.x;
    const v = dx / Math.max(1, e.timeStamp - s.t);
    if (e.type !== 'pointercancel' && (-dx > largura() * 0.3 || (v < -0.5 && -dx > 30))) api.voltar();
    else mover(topo().el, 0, true);
  }
  raiz.addEventListener('pointerup', fimGesto);
  raiz.addEventListener('pointercancel', fimGesto);

  return api;
}
