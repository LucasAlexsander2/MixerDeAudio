// Pilha de telas (D12, D13): telas registradas por nome, voltar pela seta, pelo gesto de arrastar para os lados
// e pelo histórico do navegador (voltar do Android, borda do Safari, "avançar").

const DURACAO = 280;

// opções: { raiz, inicial: HTMLElement, inicialTopo(sim), bloqueado: () => bool, aoAbrir(nome, profundidade) }.
// Devolve { registrar(nome, fabrica), abrir(nome), voltar(), reiniciar(), profundidade(), topo() }.
// A fábrica devolve o elemento da tela (uma seção com a classe "tela") ou, na Fase 15 (D5), { el, aoTopo?(sim), destruir?() }.
// O elemento é criado ao abrir e removido ao fechar. aoTopo(true) quando a tela fica no topo; aoTopo(false) quando outra tela entra por
// cima ou quando ela sai; destruir() quando ela sai da pilha (inclusive no reiniciar). O Início não vem de fábrica: `inicialTopo(sim)`
// faz o papel do aoTopo dele (chamado com true já na criação, e com false/true quando outra tela entra/sai por cima).
export function criarPilha({ raiz, inicial, inicialTopo, bloqueado = () => false, aoAbrir }) {
  const fabricas = new Map();
  const pilha = [{ nome: 'inicio', el: inicial, aoTopo: inicialTopo }];
  raiz.append(inicial);
  let ignorarPop = 0;
  let sentidoSaida = -1; // -1: sai para a esquerda (padrão); 1: sai para a direita (gesto arrastado para a direita)

  const reduzir = () => window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const largura = () => raiz.clientWidth || window.innerWidth;
  const topo = () => pilha[pilha.length - 1];

  function mover(el, x, animar) {
    el.style.transition = animar && !reduzir() ? 'transform ' + DURACAO + 'ms cubic-bezier(.2,.8,.2,1)' : 'none';
    el.style.transform = 'translate3d(' + x + 'px,0,0)';
  }

  // chama o aoTopo da tela de forma segura (um erro de uma tela não derruba a pilha)
  function avisarTopo(t, sim) {
    if (!t || !t.aoTopo || t.noTopo === sim) return; // só avisa quando muda (não repete false ao sair depois de ficar coberta)
    t.noTopo = sim;
    try { t.aoTopo(sim); } catch (e) { console.error(e); }
  }
  function destruir(t) {
    avisarTopo(t, false);
    if (t && t.destruir) { try { t.destruir(); } catch (e) { console.error(e); } }
  }

  function atualizarInerte() {
    pilha.forEach((t, i) => { t.el.inert = i < pilha.length - 1; });
  }

  function empilhar(nome) {
    const f = fabricas.get(nome);
    if (!f) return false;
    const r = f();
    const tela = r instanceof Element ? { el: r } : r;
    const el = tela.el;
    raiz.append(el);
    mover(el, largura(), false);
    void el.offsetWidth;
    mover(el, 0, true);
    avisarTopo(topo(), false);
    pilha.push({ nome, el, aoTopo: tela.aoTopo, destruir: tela.destruir });
    atualizarInerte();
    avisarTopo(topo(), true);
    if (aoAbrir) aoAbrir(nome, pilha.length - 1);
    return true;
  }

  function desempilhar() {
    if (pilha.length < 2) return;
    const saiu = pilha.pop();
    const el = saiu.el;
    destruir(saiu);
    avisarTopo(topo(), true);
    atualizarInerte();
    mover(el, sentidoSaida * largura(), true);
    sentidoSaida = -1;
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
      while (pilha.length > 1) {
        const saiu = pilha.pop();
        destruir(saiu);
        saiu.el.remove();
      }
      avisarTopo(topo(), true);
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
  avisarTopo(topo(), true); // o Início começa no topo

  // gesto de voltar (D13)
  let g = null;
  raiz.addEventListener('pointerdown', (e) => {
    if (pilha.length < 2 || !e.isPrimary || bloqueado()) return;
    if (e.target.closest('input, textarea, select, [data-sem-gesto]')) return;
    if (e.clientX < 24) return; // borda esquerda: fica com o voltar do Safari
    g = { x: e.clientX, y: e.clientY, t: e.timeStamp, ativo: false, id: e.pointerId };
  });
  raiz.addEventListener('pointermove', (e) => {
    if (!g || e.pointerId !== g.id) return;
    const dx = e.clientX - g.x;
    const dy = e.clientY - g.y;
    if (!g.ativo) {
      if (Math.abs(dx) < 8 && Math.abs(dy) < 8) return;
      if (Math.abs(dx) > Math.abs(dy) * 1.2 && !bloqueado()) {
        g.ativo = true;
        try { raiz.setPointerCapture(e.pointerId); } catch { /* sem captura */ }
      } else { g = null; return; }
    }
    mover(topo().el, dx, false);
  });
  function fimGesto(e) {
    if (!g || e.pointerId !== g.id) return;
    const s = g;
    g = null;
    if (!s.ativo) return;
    const dx = e.clientX - s.x;
    const v = dx / Math.max(1, e.timeStamp - s.t);
    if (e.type !== 'pointercancel' && (Math.abs(dx) > largura() * 0.3 || (Math.abs(v) > 0.5 && Math.abs(dx) > 30))) {
      sentidoSaida = dx > 0 ? 1 : -1;
      api.voltar();
    }
    else mover(topo().el, 0, true);
  }
  raiz.addEventListener('pointerup', fimGesto);
  raiz.addEventListener('pointercancel', fimGesto);

  return api;
}
