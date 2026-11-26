// Touchpad (guia 4.5, D25): só os gestos, sem protocolo. Mesmos limites da página antiga (antigo/16-controle.js):
//  - um dedo move; o movimento é somado e entregue uma vez por quadro (aoMover) com a sensibilidade;
//  - toque curto (menos de 350 ms e menos de 10 px) = clique esquerdo; toque com dois dedos = clique direito;
//  - segurar 450 ms quase parado = segurar o botão esquerdo (arrastar), e soltar solta;
//  - dois dedos na vertical ou a faixa lateral = rolar (um passo a cada 24 px, entregue em aoRolar);
//  - botões Esquerdo e Direito na base: toque = clique; segurar (450 ms) ou mexer o dedo no painel enquanto o botão está
//    apertado = arrastar (aoSegurar(botao, true) ... aoSegurar(botao, false)).
// Deitado (definirDeitado(true)) o painel vira vidro (--glass) flutuante: arrasta pela alça no topo e esconde (vira uma bolha).
import { icone } from '../icones.js';
import { vibrar } from '../util.js';

const PASSO_ROLAR = 24;      // px de movimento para cada passo de rolagem
const TOQUE_MAX_MS = 350;
const TOQUE_MAX_PX = 10;
const SEGURAR_MS = 450;

// opções: { sensibilidade = 1.6, botoes = true, faixa = true, dica (texto sobre o painel; some no primeiro toque),
//           aoMover(dx, dy) (inteiros), aoClicar('esq' | 'dir'), aoRolar(dy) (inteiro, mesmo sinal da página antiga:
//           positivo = dedos para baixo), aoSegurar('esq' | 'dir', ligado), aoVisibilidade(visivel) (deitado: painel mostrado/escondido) }.
// Devolve { el, painel, bolha, definirSensibilidade(n), definirDeitado(bool), deitado(), esconder(), mostrar(), destruir() }.
// `el` ocupa o espaço do pai (em pé: flex 1, mínimo 280 px). Deitado ele cobre o pai, que deve ter position: relative.
// Marcado com data-sem-gesto (o gesto de voltar da tela não vale sobre o touchpad).
export function criarTouchpad({ sensibilidade = 1.6, botoes = true, faixa = true, dica = 'Um dedo move · toque clica · dois dedos rolam',
  aoMover, aoClicar, aoRolar, aoSegurar, aoVisibilidade } = {}) {
  let sens = sensibilidade;
  let estaDeitado = false;

  const el = document.createElement('div');
  el.className = 'touchpad';
  el.setAttribute('data-sem-gesto', '');

  const painel = document.createElement('div');
  painel.className = 'tp-painel';
  const area = document.createElement('div');
  area.className = 'tp-area';
  area.setAttribute('role', 'application');
  area.setAttribute('aria-label', 'Touchpad');
  const textoDica = document.createElement('div');
  textoDica.className = 'tp-dica';
  textoDica.textContent = dica;
  textoDica.hidden = !dica;
  painel.append(area, textoDica);

  // alça e botão de esconder (só aparecem deitado)
  const alca = document.createElement('div');
  alca.className = 'tp-alca';
  alca.innerHTML = '<i class="ic">' + icone('grip') + '</i>';
  const esconderBtn = document.createElement('button');
  esconderBtn.type = 'button';
  esconderBtn.className = 'tp-esconder';
  esconderBtn.setAttribute('aria-label', 'Esconder o touchpad');
  esconderBtn.innerHTML = '<i class="ic">' + icone('chevd') + '</i>';
  alca.append(esconderBtn);
  painel.append(alca);

  const bolha = document.createElement('button');
  bolha.type = 'button';
  bolha.className = 'tp-bolha';
  bolha.setAttribute('aria-label', 'Mostrar o touchpad');
  bolha.innerHTML = '<i class="ic">' + icone('pad') + '</i>';
  bolha.hidden = true;

  // faixa de rolagem lateral
  if (faixa) {
    const f = document.createElement('div');
    f.className = 'tp-faixa';
    f.setAttribute('aria-label', 'Rolar');
    f.innerHTML = '<i class="ic">' + icone('up') + '</i><i class="ic">' + icone('down') + '</i>';
    let y = null;
    let acum = 0;
    f.addEventListener('pointerdown', (e) => {
      escondeDica();
      y = e.clientY;
      acum = 0;
      try { f.setPointerCapture(e.pointerId); } catch { /* sem captura */ }
    });
    f.addEventListener('pointermove', (e) => {
      if (y === null) return;
      acum += e.clientY - y;
      y = e.clientY;
      const passos = Math.trunc(acum / PASSO_ROLAR);
      if (passos) { acum -= passos * PASSO_ROLAR; if (aoRolar) aoRolar(passos); }
    });
    const fimFaixa = () => { y = null; };
    f.addEventListener('pointerup', fimFaixa);
    f.addEventListener('pointercancel', fimFaixa);
    painel.append(f);
  }

  // botões Esquerdo / Direito
  let segurado = null;        // botão apertado agora ('esq' | 'dir')
  let arrastoBotao = false;
  let timerBotao = 0;
  function iniciarArrastoBotao() {
    if (!segurado || arrastoBotao) return;
    arrastoBotao = true;
    vibrar(20);
    if (aoSegurar) aoSegurar(segurado, true);
  }
  if (botoes) {
    const linha = document.createElement('div');
    linha.className = 'tp-botoes';
    for (const [nome, texto] of [['esq', 'Esquerdo'], ['dir', 'Direito']]) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'tp-botao';
      b.textContent = texto;
      b.addEventListener('pointerdown', (e) => {
        escondeDica();
        vibrar();
        segurado = nome;
        arrastoBotao = false;
        b.classList.add('segurado');
        try { b.setPointerCapture(e.pointerId); } catch { /* sem captura */ }
        clearTimeout(timerBotao);
        timerBotao = setTimeout(iniciarArrastoBotao, SEGURAR_MS);
      });
      const solta = (e) => {
        if (segurado !== nome) return;
        clearTimeout(timerBotao);
        b.classList.remove('segurado');
        const arrastava = arrastoBotao;
        segurado = null;
        arrastoBotao = false;
        if (arrastava) { if (aoSegurar) aoSegurar(nome, false); }
        else if (e.type === 'pointerup' && aoClicar) aoClicar(nome);
      };
      b.addEventListener('pointerup', solta);
      b.addEventListener('pointercancel', solta);
      b.addEventListener('contextmenu', (e) => e.preventDefault());
      linha.append(b);
    }
    painel.append(linha);
  }

  el.append(painel, bolha);

  // ---- gestos na área ----
  const dedos = new Map();    // pointerId -> { x, y }
  const g = { t0: 0, moveu: 0, max: 1, arrasto: false, hold: 0, quadro: 0, ax: 0, ay: 0, sy: 0 };

  function escondeDica() { textoDica.hidden = true; }

  function entregar() {
    const dx = Math.round(g.ax);
    const dy = Math.round(g.ay);
    if (dx || dy) { g.ax -= dx; g.ay -= dy; if (aoMover) aoMover(dx, dy); }
    const passos = Math.trunc(g.sy / PASSO_ROLAR);
    if (passos) { g.sy -= passos * PASSO_ROLAR; if (aoRolar) aoRolar(passos); }
  }
  function quadro() {
    g.quadro = 0;
    if (dedos.size === 0) return;
    entregar();
    g.quadro = requestAnimationFrame(quadro);
  }
  function parar() {
    clearTimeout(g.hold);
    if (g.quadro) { cancelAnimationFrame(g.quadro); g.quadro = 0; }
  }

  area.addEventListener('contextmenu', (e) => e.preventDefault());
  area.addEventListener('pointerdown', (e) => {
    if (e.pointerType === 'mouse' && e.button === 2) { if (aoClicar) aoClicar('dir'); return; }
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    e.preventDefault();
    escondeDica();
    try { area.setPointerCapture(e.pointerId); } catch { /* sem captura */ }
    dedos.set(e.pointerId, { x: e.clientX, y: e.clientY });
    if (dedos.size === 1) {
      g.t0 = Date.now(); g.moveu = 0; g.max = 1; g.arrasto = false; g.ax = g.ay = g.sy = 0;
      g.hold = setTimeout(() => {
        if (dedos.size === 1 && g.moveu < TOQUE_MAX_PX) { g.arrasto = true; vibrar(20); if (aoSegurar) aoSegurar('esq', true); }
      }, SEGURAR_MS);
      if (!g.quadro) g.quadro = requestAnimationFrame(quadro);
    } else {
      g.max = Math.max(g.max, dedos.size);
      clearTimeout(g.hold);
    }
  });
  area.addEventListener('pointermove', (e) => {
    const p = dedos.get(e.pointerId);
    if (!p) return;
    const dx = e.clientX - p.x;
    const dy = e.clientY - p.y;
    p.x = e.clientX; p.y = e.clientY;
    g.moveu += Math.abs(dx) + Math.abs(dy);
    if (dedos.size === 1) {
      if (g.max < 2) { g.ax += dx * sens; g.ay += dy * sens; }
      if (segurado && !arrastoBotao && g.moveu > 3) iniciarArrastoBotao(); // botão apertado + dedo no painel = arrastar
    } else {
      g.sy += dy / dedos.size;
    }
  });
  function fimArea(e) {
    if (!dedos.has(e.pointerId)) return;
    dedos.delete(e.pointerId);
    if (dedos.size > 0) return;
    parar();
    entregar();
    if (g.arrasto) { if (aoSegurar) aoSegurar('esq', false); }
    else if (e.type === 'pointerup' && g.moveu < TOQUE_MAX_PX && Date.now() - g.t0 < TOQUE_MAX_MS) {
      if (aoClicar) aoClicar(g.max >= 2 ? 'dir' : 'esq');
    }
    g.arrasto = false;
  }
  area.addEventListener('pointerup', fimArea);
  area.addEventListener('pointercancel', fimArea);

  // ---- deitado: alça, esconder e bolha ----
  let gp = null;
  alca.addEventListener('pointerdown', (e) => {
    if (e.target.closest('.tp-esconder')) return;
    const r = painel.getBoundingClientRect();
    gp = { dx: e.clientX - r.left, dy: e.clientY - r.top, w: r.width, h: r.height };
    painel.style.right = painel.style.bottom = 'auto';
    painel.style.left = (r.left - el.getBoundingClientRect().left) + 'px';
    painel.style.top = (r.top - el.getBoundingClientRect().top) + 'px';
    try { alca.setPointerCapture(e.pointerId); } catch { /* sem captura */ }
  });
  alca.addEventListener('pointermove', (e) => {
    if (!gp) return;
    const o = el.getBoundingClientRect();
    const limitar = (v, a, b) => Math.min(b, Math.max(a, v));
    painel.style.left = limitar(e.clientX - gp.dx - o.left, 0, Math.max(0, o.width - gp.w)) + 'px';
    painel.style.top = limitar(e.clientY - gp.dy - o.top, 0, Math.max(0, o.height - gp.h)) + 'px';
  });
  const fimAlca = () => { gp = null; };
  alca.addEventListener('pointerup', fimAlca);
  alca.addEventListener('pointercancel', fimAlca);

  const api = {
    el,
    painel,
    bolha,
    definirSensibilidade(n) { if (n > 0) sens = n; },
    deitado() { return estaDeitado; },
    definirDeitado(v) {
      estaDeitado = !!v;
      el.classList.toggle('deitado', estaDeitado);
      painel.style.left = painel.style.top = painel.style.right = painel.style.bottom = '';
      painel.hidden = false;
      bolha.hidden = true;
    },
    esconder() {
      if (!estaDeitado) return;
      painel.hidden = true;
      bolha.hidden = false;
      if (aoVisibilidade) aoVisibilidade(false);
    },
    mostrar() {
      painel.hidden = false;
      bolha.hidden = true;
      if (aoVisibilidade) aoVisibilidade(true);
    },
    destruir() { parar(); clearTimeout(timerBotao); el.remove(); },
  };
  esconderBtn.addEventListener('click', () => api.esconder());
  bolha.addEventListener('click', () => { vibrar(); api.mostrar(); });
  return api;
}
