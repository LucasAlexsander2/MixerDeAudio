// Tela do PC (Fase 15c, D16): a imagem do PC com o touchpad dentro (nota "Tela do PC com touchpad"), modos Touchpad e Toque direto,
// barra com teclado, modo, zoom, ponteiro, qualidade e tela cheia; deitado, o touchpad vira o painel de vidro.
// Canal próprio /tela/ws (antigo/17-tela.js): um quadro por vez (a página manda {"ok":1} depois de desenhar), configuração
// {mon, w, q} mais {cur, p:1} (tamanho do ponteiro e posição dele antes de cada quadro, para o zoom seguir o ponteiro).
// A tela só transmite enquanto está no topo da pilha e a página está visível. Contrato do ctx: telas/LEIAME.md.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarChip, criarGrupoChips } from '../componentes/chip.js';
import { criarFolha } from '../componentes/folha.js';
import { criarTouchpad } from '../componentes/touchpad.js';
import { icone } from '../icones.js';
import { criarTeclado, TECLAS_CURTAS, ATALHOS_RAPIDOS } from './teclado-pc.js';

const QUALIDADES = { eco: 'Econômica', normal: 'Normal', alta: 'Alta' };
const ORDEM_QUALIDADE = ['eco', 'normal', 'alta'];
const PONTEIROS = [1, 1.5, 2, 3];
const ZOOM_MAX = 4;
const MARGEM_SEGUIR = 0.22; // o ponteiro não chega a menos de 22% da borda com zoom (como no protótipo)

function nomePonteiro(v) { return (v === 1.5 ? '1,5' : String(v)) + '×'; }
function limitar(v, a, b) { return v < a ? a : v > b ? b : v; }
function criar(tag, classe, texto) {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texto !== undefined) e.textContent = texto;
  return e;
}
function botaoIcone(nome, rotulo) {
  const b = criar('button', 'tpc-ib');
  b.type = 'button';
  b.setAttribute('aria-label', rotulo);
  b.innerHTML = '<i class="ic">' + icone(nome) + '</i>';
  return b;
}

// Conta do ponto (como a página antiga): (cx, cy) = px dentro do palco (sw × sh); imagem iw × ih desenhada com "contain";
// zoom z e deslocamento tx, ty (translate(tx, ty) scale(z), origem 0,0). Devolve x, y de 0 a 1 dentro da imagem.
export function pontoNaImagem(cx, cy, sw, sh, iw, ih, z, tx, ty) {
  const s = Math.min(sw / iw, sh / ih);
  const dw = iw * s;
  const dh = ih * s;
  const ox = (sw - dw) / 2;
  const oy = (sh - dh) / 2;
  return { x: limitar(((cx - tx) / z - ox) / dw, 0, 1), y: limitar(((cy - ty) / z - oy) / dh, 0, 1) };
}
// Inverso: ponto 0..1 da imagem para px no palco.
export function pontoNoPalco(fx, fy, sw, sh, iw, ih, z, tx, ty) {
  const s = Math.min(sw / iw, sh / ih);
  const dw = iw * s;
  const dh = ih * s;
  const ox = (sw - dw) / 2;
  const oy = (sh - dh) / 2;
  return { x: tx + z * (ox + fx * dw), y: ty + z * (oy + fy * dh) };
}

export function criarTela(ctx) {
  const pref = ctx.preferencias;
  const enviar = (o) => ctx.enviar(o);

  let qual = pref.ler('tela.qualidade', 'normal');
  if (!QUALIDADES[qual]) qual = 'normal';
  let ponteiro = Number(pref.ler('tela.ponteiro', 1.5));
  if (!PONTEIROS.includes(ponteiro)) ponteiro = 1.5;
  let modo = pref.ler('tela.modo', 'tp');
  if (modo !== 'tp' && modo !== 'dir') modo = 'tp';
  let mon = 0;
  let monitores = [];
  let entrada = true;

  // estado da transmissão e da imagem
  const sc = {
    on: false, ws: null, got: false, iw: 0, ih: 0, z: 1, tx: 0, ty: 0, pt: null, pend: null,
    url: null, fs: false, nat: false, kb: false, barT: 0, cfgT: 0, land: false,
  };
  let topo = false;
  let destruida = false;

  // ---- elementos ----
  const el = criar('section', 'tela tela-pc');
  el.dataset.tela = 'tela';

  const chipQual = criarChip({ texto: QUALIDADES[qual], aoClicar: () => trocarQualidade() });
  const chipMon = criarChip({ texto: 'Tela 1', icone: 'monitors', aoClicar: () => abrirMonitores() });
  chipMon.el.hidden = true;
  const cab = criarCabecalho({ titulo: 'Tela do PC', aoVoltar: () => ctx.voltar(), acoes: [chipMon.el, chipQual.el] });

  const caixa = criar('div', 'tpc-caixa');
  const palco = criar('div', 'tpc-palco');
  palco.setAttribute('data-sem-gesto', '');
  palco.setAttribute('role', 'img');
  palco.setAttribute('aria-label', 'Tela do PC');
  const zoomEl = criar('div', 'tpc-zoom');
  const cv = criar('canvas', 'tpc-img');
  const cvCtx = cv.getContext ? cv.getContext('2d') : null;
  const temBitmap = !!(window.createImageBitmap && cvCtx);
  const img = criar('img', 'tpc-img');
  img.alt = '';
  cv.hidden = !temBitmap;
  img.hidden = temBitmap;
  zoomEl.append(cv, img);
  const ponto = criar('div', 'tpc-ponto');
  ponto.hidden = true;
  const msg = criar('div', 'tpc-msg');
  const msgTexto = criar('p', 'texto-sec');
  const msgBotao = criar('button', 'botao principal', 'Tentar de novo');
  msgBotao.type = 'button';
  msg.append(msgTexto, msgBotao);
  msg.hidden = true;
  const girar = criar('div', 'tpc-girar', 'Gire o celular');
  girar.hidden = true;
  palco.append(zoomEl, ponto, msg, girar);

  // barra: modo, teclado, zoom, ponteiro, duplo, tela cheia
  const barra = criar('div', 'tpc-barra');
  barra.setAttribute('data-sem-gesto', '');
  const ibVoltar = botaoIcone('back', 'Voltar');
  ibVoltar.classList.add('so-deitada');
  const modos = criarGrupoChips({
    opcoes: [{ valor: 'tp', texto: 'Touchpad' }, { valor: 'dir', texto: 'Toque direto' }],
    valor: modo,
    aoEscolher: (v) => { modo = v; pref.gravar('tela.modo', v); ctx.toast(v === 'tp' ? 'Modo touchpad' : 'Modo toque direto'); barraMostrar(); },
  });
  const ibTeclado = botaoIcone('keyboard', 'Teclado');
  ibTeclado.setAttribute('aria-pressed', 'false');
  const ibZoom = botaoIcone('zoom', 'Zoom');
  ibZoom.setAttribute('aria-pressed', 'false');
  const chipPonteiro = criarChip({ texto: 'Ponteiro ' + nomePonteiro(ponteiro), aoClicar: () => abrirPonteiros() });
  const chipDuplo = criarChip({ texto: 'Duplo', aoClicar: () => { enviar({ t: 'clique', b: 'esq', duplo: true }); barraMostrar(); } });
  const ibTelaCheia = botaoIcone('full', 'Tela cheia');
  ibTelaCheia.setAttribute('aria-pressed', 'false');
  barra.append(ibVoltar, modos.el, ibTeclado, ibZoom, chipPonteiro.el, chipDuplo.el, ibTelaCheia);

  // teclado do PC (texto, teclas e modificadores presos)
  const teclado = criarTeclado({ enviar, camadas: ctx.camadas });
  const kbLinha = criar('div', 'tpc-teclado');
  kbLinha.hidden = true;
  const kbTexto = teclado.texto({ placeholder: 'Digite aqui', rotuloBotao: 'Enviar' });
  kbLinha.append(
    kbTexto.el,
    teclado.teclas(TECLAS_CURTAS, 'tec-grade tpc-teclas'),
    teclado.mods,
    teclado.atalhos(ATALHOS_RAPIDOS),
  );

  // touchpad (o componente da Fase 14), ligado ao protocolo
  let arrasto = null;
  const tp = criarTouchpad({
    dica: 'Um dedo move · toque clica · dois dedos rolam',
    aoMover: (dx, dy) => enviar({ t: 'mouse', dx, dy }),
    aoClicar: (b) => enviar({ t: 'clique', b }),
    aoRolar: (dy) => enviar({ t: 'rolar', dx: 0, dy }),
    aoSegurar: (b, ligado) => { arrasto = ligado ? b : null; enviar({ t: 'botao', b, baixo: ligado }); },
  });

  caixa.append(palco, barra, kbLinha, tp.el);
  el.append(cab.el, caixa);

  // ---- imagem: zoom e ponteiro ----
  function palcoTam() { return { sw: palco.clientWidth || 1, sh: palco.clientHeight || 1 }; }

  function aplicar() {
    const { sw, sh } = palcoTam();
    sc.z = limitar(sc.z, 1, ZOOM_MAX);
    sc.tx = limitar(sc.tx, sw - sw * sc.z, 0);
    sc.ty = limitar(sc.ty, sh - sh * sc.z, 0);
    zoomEl.style.transform = 'translate(' + sc.tx + 'px,' + sc.ty + 'px) scale(' + sc.z + ')';
    ibZoom.setAttribute('aria-pressed', String(sc.z > 1));
    if (sc.mostrarPonto && sc.pt && sc.iw) {
      const p = pontoNoPalco(sc.pt.x, sc.pt.y, sw, sh, sc.iw, sc.ih, sc.z, sc.tx, sc.ty);
      ponto.style.left = p.x + 'px';
      ponto.style.top = p.y + 'px';
      ponto.hidden = false;
    } else ponto.hidden = true;
  }
  function zoomZero() { sc.z = 1; sc.tx = 0; sc.ty = 0; aplicar(); }

  // com zoom, a imagem acompanha o ponteiro: ele nunca chega a menos de 22% da borda do palco
  function seguir() {
    if (sc.z <= 1 || !sc.pt || !sc.iw || pinca) return;
    const { sw, sh } = palcoTam();
    const p = pontoNoPalco(sc.pt.x, sc.pt.y, sw, sh, sc.iw, sc.ih, sc.z, sc.tx, sc.ty);
    const mx = sw * MARGEM_SEGUIR;
    const my = sh * MARGEM_SEGUIR;
    if (p.x < mx) sc.tx += mx - p.x; else if (p.x > sw - mx) sc.tx -= p.x - (sw - mx);
    if (p.y < my) sc.ty += my - p.y; else if (p.y > sh - my) sc.ty -= p.y - (sh - my);
  }
  // botão de zoom: 1x <-> 2x, com o ponteiro no meio
  function alternarZoom() {
    if (sc.z > 1) { zoomZero(); ctx.toast('Zoom desligado'); return; }
    const { sw, sh } = palcoTam();
    sc.z = 2;
    if (sc.pt && sc.iw) {
      const p = pontoNoPalco(sc.pt.x, sc.pt.y, sw, sh, sc.iw, sc.ih, 1, 0, 0);
      sc.tx = sw / 2 - 2 * p.x;
      sc.ty = sh / 2 - 2 * p.y;
    } else { sc.tx = -sw / 2; sc.ty = -sh / 2; }
    aplicar();
    ctx.toast('Zoom 2×');
  }
  ibZoom.addEventListener('click', () => { alternarZoom(); barraMostrar(); });

  function dimensionar(w, h) {
    if (sc.iw === w && sc.ih === h) return;
    sc.iw = w; sc.ih = h;
    palco.style.setProperty('--ar', w + '/' + h);
    aplicar();
  }

  // ---- canal /tela/ws ----
  function ws(o) { if (sc.ws && sc.ws.readyState === 1) sc.ws.send(JSON.stringify(o)); }
  function largura() {
    const lado = sc.fs ? Math.max(window.innerWidth, window.innerHeight) : (palco.clientWidth || window.innerWidth);
    return Math.max(160, Math.round(lado * (window.devicePixelRatio || 1)));
  }
  function config() { ws({ mon, w: largura(), q: qual, cur: ponteiro, p: 1 }); }
  function configLogo() { clearTimeout(sc.cfgT); sc.cfgT = setTimeout(config, 300); }
  function ack() { ws({ ok: 1 }); }

  function desenhar(blob) {
    if (temBitmap) {
      window.createImageBitmap(blob).then((bm) => {
        if (!sc.on) { bm.close(); return; }
        if (cv.width !== bm.width || cv.height !== bm.height) { cv.width = bm.width; cv.height = bm.height; }
        cvCtx.drawImage(bm, 0, 0);
        const w = bm.width;
        const h = bm.height;
        bm.close();
        quadroPronto(w, h);
      }).catch(() => parar('Tela indisponível.'));
      return;
    }
    const u = URL.createObjectURL(blob);
    img.onload = () => {
      img.onload = null;
      if (!sc.on) { URL.revokeObjectURL(u); return; }
      if (sc.url) URL.revokeObjectURL(sc.url);
      sc.url = u;
      quadroPronto(img.naturalWidth, img.naturalHeight);
    };
    img.onerror = () => { URL.revokeObjectURL(u); parar('Tela indisponível.'); };
    img.src = u;
  }
  function quadroPronto(w, h) {
    if (sc.pend) { sc.pt = sc.pend; sc.pend = null; sc.mostrarPonto = false; }
    dimensionar(w, h);
    msg.hidden = true;
    seguir();
    aplicar();
    ack();
  }

  function parar(texto) {
    if (!sc.on && !texto) return;
    sc.on = false;
    clearTimeout(sc.cfgT);
    const w = sc.ws;
    sc.ws = null;
    if (w) { w.onopen = w.onmessage = w.onclose = w.onerror = null; try { w.close(); } catch { /* já fechado */ } }
    if (cvCtx) { cv.width = 1; cv.height = 1; }
    img.removeAttribute('src');
    if (sc.url) { URL.revokeObjectURL(sc.url); sc.url = null; }
    sc.iw = sc.ih = 0; sc.pt = sc.pend = null; sc.got = false; sc.mostrarPonto = false;
    zoomZero();
    if (texto) { msgTexto.textContent = texto; msgBotao.hidden = false; msg.hidden = false; }
  }
  // WebSocket recusado: o navegador não mostra o status; um GET à mesma rota repete as regras (409, 403)
  function porque() {
    return fetch('/tela/ws', { cache: 'no-store' }).then((r) => (
      r.status === 409 ? 'Outro celular está no controle.' : r.status === 403 ? 'A tela não está liberada no PC.' : 'Tela indisponível.'
    )).catch(() => 'Tela indisponível.');
  }
  function iniciar() {
    if (sc.on || destruida) return;
    sc.on = true; sc.got = false;
    msgTexto.textContent = 'Conectando à tela…';
    msgBotao.hidden = true;
    msg.hidden = false;
    let w;
    try { w = new WebSocket((location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/tela/ws'); } catch { parar('Tela indisponível.'); return; }
    sc.ws = w;
    w.binaryType = 'blob';
    w.onopen = () => { if (sc.ws === w) config(); };
    w.onmessage = (e) => {
      if (sc.ws !== w) return;
      if (typeof e.data === 'string') { // {"p":[x,y]}: onde está o ponteiro no próximo quadro
        try {
          const m = JSON.parse(e.data);
          if (m && Array.isArray(m.p) && m.p.length === 2) sc.pend = { x: limitar(+m.p[0] || 0, 0, 1), y: limitar(+m.p[1] || 0, 0, 1) };
        } catch { /* texto que não é JSON */ }
        return;
      }
      sc.got = true;
      desenhar(e.data);
    };
    w.onclose = (e) => {
      if (sc.ws !== w || !sc.on) return;
      if (e.code === 4003) { parar('A tela não está liberada no PC.'); return; }
      if (e.code === 4004) { parar('Tela indisponível.'); return; }
      if (sc.got) { parar('Tela indisponível.'); return; }
      porque().then((m) => { if (sc.ws === w && sc.on) parar(m); });
    };
  }
  msgBotao.addEventListener('click', () => { msg.hidden = true; iniciar(); });

  // só transmite com a tela no topo e a página visível
  function atualizarAtivo() {
    const ativo = topo && !destruida && document.visibilityState === 'visible';
    if (ativo && !sc.on) iniciar();
    else if (!ativo && sc.on) { parar(); msg.hidden = true; }
  }
  const aoVisivel = () => atualizarAtivo();
  document.addEventListener('visibilitychange', aoVisivel);

  // ---- qualidade, ponteiro e monitores ----
  function trocarQualidade(nova) {
    qual = nova || ORDEM_QUALIDADE[(ORDEM_QUALIDADE.indexOf(qual) + 1) % ORDEM_QUALIDADE.length];
    pref.gravar('tela.qualidade', qual);
    chipQual.definirTexto(QUALIDADES[qual]);
    ctx.toast('Qualidade: ' + QUALIDADES[qual]);
    config();
    barraMostrar();
  }
  function abrirPonteiros() {
    barraMostrar(true);
    const f = criarFolha({
      container: ctx.camadas,
      titulo: 'Tamanho do ponteiro',
      opcoes: PONTEIROS.map((v) => ({ valor: v, texto: nomePonteiro(v) })),
      valor: ponteiro,
      aoEscolher: (v) => {
        ponteiro = v;
        pref.gravar('tela.ponteiro', v);
        chipPonteiro.definirTexto('Ponteiro ' + nomePonteiro(v));
        config();
      },
      aoFechar: () => { f.destruir(); barraMostrar(); },
    });
    f.abrir();
  }
  function abrirMonitores() {
    const f = criarFolha({
      container: ctx.camadas,
      titulo: 'Tela do PC',
      opcoes: monitores.map((m) => ({ valor: m.i, texto: 'Tela ' + (m.i + 1) + (m.principal ? ' (principal)' : '') + ' · ' + m.w + '×' + m.h, icone: 'monitor' })),
      valor: mon,
      aoEscolher: (v) => { mon = v; sc.pt = sc.pend = null; zoomZero(); chipMon.definirTexto('Tela ' + (v + 1)); config(); },
      aoFechar: () => f.destruir(),
    });
    f.abrir();
  }
  function desenharEstado(estado) {
    const f = (estado && estado.ferr) || {};
    entrada = !!f.entrada;
    const lista = (estado && estado.pc && estado.pc.monitores) || [];
    const sig = JSON.stringify(lista);
    if (sig !== sc.sigMon) {
      sc.sigMon = sig;
      monitores = lista;
      if (mon >= lista.length) mon = 0;
      chipMon.el.hidden = lista.length < 2;
      chipMon.definirTexto('Tela ' + (mon + 1));
    }
    // sem o grupo de entrada, só se vê a tela (como na página antiga): sem touchpad, teclado nem cliques
    tp.el.hidden = !entrada;
    modos.el.hidden = !entrada;
    ibTeclado.hidden = !entrada;
    chipDuplo.el.hidden = !entrada;
    chipPonteiro.el.hidden = false;
    if (!entrada) { kbLinha.hidden = true; sc.kb = false; }
  }
  desenharEstado(ctx.obterEstado());
  const cancelarEstado = ctx.aoMudar(desenharEstado);

  // ---- barra: some sozinha deitado ----
  function barraMostrar(manter) {
    barra.classList.remove('oculta');
    clearTimeout(sc.barT);
    if (sc.land && !sc.kb && !manter) sc.barT = setTimeout(() => barra.classList.add('oculta'), 3000);
  }
  ibTeclado.addEventListener('click', () => {
    sc.kb = !sc.kb;
    kbLinha.hidden = !sc.kb;
    ibTeclado.setAttribute('aria-pressed', String(sc.kb));
    if (sc.kb) { clearTimeout(sc.barT); barra.classList.remove('oculta'); kbTexto.campo.focus(); } else { teclado.limpar(); barraMostrar(); }
  });
  ibVoltar.addEventListener('click', () => { if (sc.fs) sairTelaCheia(); else ctx.voltar(); });

  // ---- deitado (aparelho deitado ou tela cheia) ----
  const mqDeitado = window.matchMedia ? window.matchMedia('(orientation: landscape)') : null;
  // deitado, o app ocupa a largura toda (no protótipo: body.land #app { max-width: none })
  function marcarCorpo() { document.body.classList.toggle('tela-pc-deitada', sc.land && topo && !destruida); }
  function layout() {
    const land = sc.fs || !!(mqDeitado && mqDeitado.matches && window.innerWidth >= 560);
    if (land !== sc.land || el.classList.contains('deitada') !== land) {
      sc.land = land;
      el.classList.toggle('deitada', land);
      tp.definirDeitado(land);
    }
    marcarCorpo();
    barraMostrar();
    requestAnimationFrame(() => { aplicar(); });
    if (sc.on) configLogo();
  }
  if (mqDeitado) {
    if (mqDeitado.addEventListener) mqDeitado.addEventListener('change', layout); else if (mqDeitado.addListener) mqDeitado.addListener(layout);
  }
  const aoRedimensionar = () => { if (sc.on) { aplicar(); configLogo(); } };
  window.addEventListener('resize', aoRedimensionar);

  function entrarTelaCheia() {
    if (sc.fs) return;
    const rq = caixa.requestFullscreen || caixa.webkitRequestFullscreen;
    sc.fs = true; sc.nat = false;
    caixa.classList.add('fs');
    el.classList.add('tpc-fs');
    if (rq) {
      try {
        const p = rq.call(caixa);
        const travar = () => { try { if (screen.orientation && screen.orientation.lock) { const r = screen.orientation.lock('landscape'); if (r && r.catch) r.catch(() => {}); } } catch { /* sem trava */ } };
        if (p && p.then) p.then(travar, () => {}); else travar();
      } catch { /* sem tela cheia nativa */ }
    } else if (window.innerHeight > window.innerWidth) {
      girar.hidden = false;
      setTimeout(() => { girar.hidden = true; }, 4000);
    }
    ibTelaCheia.setAttribute('aria-pressed', 'true');
    zoomZero();
    layout();
  }
  function sairTelaCheia() {
    if (!sc.fs) return;
    sc.fs = false;
    caixa.classList.remove('fs');
    el.classList.remove('tpc-fs');
    girar.hidden = true;
    try {
      if (document.fullscreenElement && document.exitFullscreen) document.exitFullscreen();
      else if (document.webkitFullscreenElement && document.webkitExitFullscreen) document.webkitExitFullscreen();
    } catch { /* já saiu */ }
    try { if (screen.orientation && screen.orientation.unlock) screen.orientation.unlock(); } catch { /* sem trava */ }
    ibTelaCheia.setAttribute('aria-pressed', 'false');
    zoomZero();
    layout();
  }
  function aoMudarTelaCheia() {
    const e = document.fullscreenElement || document.webkitFullscreenElement;
    if (e === caixa) sc.nat = true;
    else if (sc.fs && sc.nat) sairTelaCheia();
  }
  const aoTecla = (e) => { if (e.key === 'Escape' && sc.fs) sairTelaCheia(); };
  document.addEventListener('fullscreenchange', aoMudarTelaCheia);
  document.addEventListener('webkitfullscreenchange', aoMudarTelaCheia);
  document.addEventListener('keydown', aoTecla);
  ibTelaCheia.addEventListener('click', () => { if (sc.fs) sairTelaCheia(); else entrarTelaCheia(); });

  // ---- toque, pinça e arraste na imagem ----
  const dedos = new Map();
  let g = null;
  let pinca = false;
  let ultimo = { t: -9999, x: 0, y: 0 };

  function relativo(e) { const r = palco.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; }
  function pontoDe(x, y) {
    const { sw, sh } = palcoTam();
    return pontoNaImagem(x, y, sw, sh, sc.iw, sc.ih, sc.z, sc.tx, sc.ty);
  }
  // quantos px do PC valem 1 px do dedo no palco (a imagem é uma versão reduzida da tela)
  function escala() {
    const m = monitores.find((x) => x.i === mon);
    const { sw, sh } = palcoTam();
    if (!m || !sc.iw) return 1;
    const dw = Math.min(sw, sh * sc.iw / sc.ih);
    return limitar(m.w / dw / sc.z, 0.3, 10);
  }
  function irPara(p) { enviar({ t: 'mouseAbs', mon, x: p.x, y: p.y }); sc.pt = p; }

  function entregar() {
    if (!g) return;
    const dx = Math.round(g.ax);
    const dy = Math.round(g.ay);
    if (dx || dy) { g.ax -= dx; g.ay -= dy; enviar({ t: 'mouse', dx, dy }); }
    const passos = Math.trunc(g.sy / 24);
    if (passos) { g.sy -= passos * 24; enviar({ t: 'rolar', dx: 0, dy: passos }); }
    if (g.abs) { irPara(g.abs); g.abs = null; }
  }
  function quadro() {
    if (!g) return;
    g.quadro = 0;
    if (dedos.size === 0) return;
    entregar();
    g.quadro = requestAnimationFrame(quadro);
  }
  function iniciarArrasto() {
    if (!g || g.arrasto) return;
    g.arrasto = true;
    enviar({ t: 'botao', b: 'esq', baixo: true });
    try { navigator.vibrate && navigator.vibrate(20); } catch { /* sem vibração */ }
  }
  function fimArrasto() {
    if (g && g.arrasto) { g.arrasto = false; enviar({ t: 'botao', b: 'esq', baixo: false }); }
  }
  function lista() { return [...dedos.values()]; }

  palco.addEventListener('contextmenu', (e) => e.preventDefault());
  palco.addEventListener('pointerdown', (e) => {
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    barraMostrar();
    try { palco.setPointerCapture(e.pointerId); } catch { /* sem captura */ }
    const p = relativo(e);
    dedos.set(e.pointerId, { x: p.x, y: p.y, t: e.timeStamp });
    if (dedos.size === 1) {
      g = { t: e.timeStamp, mv: 0, max: 1, segundo: false, arrasto: false, timer: 0, quadro: 0, ax: 0, ay: 0, sy: 0, abs: null, x0: p.x, y0: p.y, gx: 0, gy: 0 };
      pinca = false;
      if (modo === 'tp' && entrada && e.timeStamp - ultimo.t < 300 && Math.hypot(p.x - ultimo.x, p.y - ultimo.y) < 40) {
        g.segundo = true;
        g.timer = setTimeout(() => { if (g && g.segundo && g.mv < 8 && dedos.size === 1) iniciarArrasto(); }, 220);
      }
      if (entrada && modo === 'dir' && sc.iw) g.abs = pontoDe(p.x, p.y);
      g.quadro = requestAnimationFrame(quadro);
    } else if (g) {
      g.max = Math.max(g.max, dedos.size);
      clearTimeout(g.timer);
      g.segundo = false; g.mv = 0; g.t = e.timeStamp;
      if (dedos.size === 2) {
        const [a, b] = lista();
        g.d0 = Math.hypot(a.x - b.x, a.y - b.y) || 1;
        g.mx = (a.x + b.x) / 2; g.my = (a.y + b.y) / 2;
        g.z0 = sc.z; g.tx0 = sc.tx; g.ty0 = sc.ty;
        pinca = false;
      }
    }
  });
  palco.addEventListener('pointermove', (e) => {
    const d = dedos.get(e.pointerId);
    if (!d || !g) return;
    const p = relativo(e);
    const dx = p.x - d.x;
    const dy = p.y - d.y;
    const dt = Math.max(1, e.timeStamp - d.t);
    d.x = p.x; d.y = p.y; d.t = e.timeStamp;
    g.mv += Math.hypot(dx, dy);
    if (dedos.size === 1) {
      if (g.max > 1 || !entrada) return;
      if (modo === 'dir') { if (sc.iw) g.abs = pontoDe(p.x, p.y); return; }
      if (g.segundo && !g.arrasto && g.mv > 8) iniciarArrasto();
      const v = Math.hypot(dx, dy) / dt;
      const ganho = (0.5 + Math.min(1.5, v * 1.2)) * escala();
      g.ax += dx * ganho; g.ay += dy * ganho;
    } else if (dedos.size === 2) {
      const [a, b] = lista();
      const dist = Math.hypot(a.x - b.x, a.y - b.y);
      const razao = dist / g.d0;
      if (!pinca && (razao > 1.15 || razao < 0.87)) pinca = true;
      if (pinca) {
        // o ponto da imagem que estava sob o meio dos dedos continua sob ele
        const mx = (a.x + b.x) / 2;
        const my = (a.y + b.y) / 2;
        const z = limitar(g.z0 * razao, 1, ZOOM_MAX);
        const ux = (g.mx - g.tx0) / g.z0;
        const uy = (g.my - g.ty0) / g.z0;
        sc.z = z; sc.tx = mx - z * ux; sc.ty = my - z * uy;
        aplicar();
      } else if (entrada && g.mv > 10) g.sy += dy / 2;
    }
  });
  function fim(e, cancelado) {
    const d = dedos.get(e.pointerId);
    if (!d || !g) return;
    dedos.delete(e.pointerId);
    if (dedos.size > 0) return;
    clearTimeout(g.timer);
    if (g.quadro) cancelAnimationFrame(g.quadro);
    entregar();
    const dur = e.timeStamp - g.t;
    if (g.arrasto) fimArrasto();
    else if (!cancelado && entrada) {
      const direto = modo === 'dir';
      if (g.max === 1 && g.mv < (direto ? 14 : 8) && dur < (direto ? 500 : 200)) {
        if (direto && sc.iw) { const p = pontoDe(d.x, d.y); irPara(p); sc.mostrarPonto = true; aplicar(); }
        enviar({ t: 'clique', b: 'esq' });
        const p2 = relativo(e);
        ultimo = { t: g.segundo ? -9999 : e.timeStamp, x: p2.x, y: p2.y };
      } else if (g.max === 2 && g.mv < 12 && dur < 260 && !pinca) {
        if (direto && sc.iw) irPara(pontoDe(d.x, d.y));
        enviar({ t: 'clique', b: 'dir' });
      }
    }
    g = null;
    pinca = false;
    seguir();
    aplicar();
  }
  palco.addEventListener('pointerup', (e) => fim(e, false));
  palco.addEventListener('pointercancel', (e) => fim(e, true));
  layout();
  aplicar();

  return {
    el,
    aoTopo(sim) {
      topo = !!sim;
      marcarCorpo();
      if (!sim && sc.fs) sairTelaCheia();
      if (!sim) { teclado.limpar(); if (arrasto) { enviar({ t: 'botao', b: arrasto, baixo: false }); arrasto = null; } }
      atualizarAtivo();
    },
    destruir() {
      destruida = true;
      topo = false;
      marcarCorpo();
      if (sc.fs) sairTelaCheia();
      parar();
      cancelarEstado();
      clearTimeout(sc.barT);
      clearTimeout(sc.cfgT);
      document.removeEventListener('visibilitychange', aoVisivel);
      document.removeEventListener('fullscreenchange', aoMudarTelaCheia);
      document.removeEventListener('webkitfullscreenchange', aoMudarTelaCheia);
      document.removeEventListener('keydown', aoTecla);
      window.removeEventListener('resize', aoRedimensionar);
      if (mqDeitado) {
        if (mqDeitado.removeEventListener) mqDeitado.removeEventListener('change', layout); else if (mqDeitado.removeListener) mqDeitado.removeListener(layout);
      }
      if (arrasto) { enviar({ t: 'botao', b: arrasto, baixo: false }); arrasto = null; }
      teclado.destruir();
      tp.destruir();
    },
  };
}
