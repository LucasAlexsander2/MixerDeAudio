// Tela Estado do PC (Fase 15b, D14): CPU, GPU, memória, ligado há, bateria e disco do Windows.
// Assina o grupo `info` só com a tela no topo (D4/D5). Mensagem recebida (página antiga, 12-estado-pc.js):
// { t:'info', cpu, gpu? (0-100, ausente sem leitura), mem:{usada,total}, ligado, bateria:{pct,carregando}|null, disco:{livre,total} }.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarCartao } from '../componentes/cartao.js';
import { criarMedidor } from '../componentes/medidor.js';

export function gb(n) {
  return (n / 1073741824).toFixed(1).replace('.', ',') + ' GB';
}

// 93784 s -> "1 d 2 h 3 min"
export function ligadoHa(seg) {
  const d = Math.floor(seg / 86400), h = Math.floor((seg % 86400) / 3600), mi = Math.floor((seg % 3600) / 60);
  return (d ? d + ' d ' : '') + h + ' h ' + mi + ' min';
}

function el(tag, classe, texto) {
  const e = document.createElement(tag);
  if (classe) e.className = classe;
  if (texto !== undefined) e.textContent = texto;
  return e;
}

// Cartão com título, valor grande e, se pedido, um medidor.
function cartaoMedido(titulo, rotuloMedidor) {
  const valor = el('div', 'mono estado-grande', '—');
  const medidor = criarMedidor({ rotulo: rotuloMedidor });
  const cartao = criarCartao({ filhos: [el('div', 'texto-sec', titulo), valor, medidor.el] });
  return { el: cartao.el, valor, medidor };
}

function cartaoTexto(titulo, inicial) {
  const valor = el('div', 'estado-valor', inicial);
  const extra = el('div', 'texto-sec estado-extra');
  extra.hidden = true;
  const cartao = criarCartao({ filhos: [el('div', 'texto-sec', titulo), valor, extra] });
  return { el: cartao.el, valor, extra };
}

export function criarTela(ctx) {
  const raiz = el('section', 'tela');
  raiz.dataset.tela = 'estado';
  const cab = criarCabecalho({ titulo: 'Estado do PC', aoVoltar: () => ctx.voltar() });

  const cpu = cartaoMedido('CPU', 'CPU');
  const gpu = cartaoMedido('GPU', 'GPU');
  const topo = el('div', 'estado-dupla');
  topo.append(cpu.el, gpu.el);
  const ram = cartaoMedido('Memória (RAM)', 'Memória');
  const ram2 = el('div', 'texto-sec estado-extra');
  ram.el.append(ram2);
  const ligado = cartaoTexto('Tempo ligado', '—');
  const bateria = cartaoTexto('Bateria', '—');
  const disco = cartaoTexto('Disco do Windows', '—');

  const lista = el('div', 'estado-lista');
  lista.append(topo, ram.el, ligado.el, bateria.el, disco.el);
  raiz.append(cab.el, lista);

  function pintar(m) {
    cpu.valor.textContent = (m.cpu || 0) + '%';
    cpu.medidor.definir(m.cpu || 0);
    if (typeof m.gpu === 'number') {
      gpu.valor.textContent = m.gpu + '%';
      gpu.medidor.definir(m.gpu);
    } else {
      gpu.valor.textContent = '—'; // o cartão já se chama GPU: lê "GPU —"
      gpu.medidor.definir(0);
    }
    const mem = m.mem || {};
    const pct = mem.total > 0 ? Math.round((mem.usada / mem.total) * 100) : 0;
    ram.valor.textContent = pct + '%';
    ram.medidor.definir(pct);
    ram2.textContent = gb(mem.usada || 0) + ' de ' + gb(mem.total || 0);
    ligado.valor.textContent = ligadoHa(m.ligado || 0);
    const b = m.bateria;
    bateria.valor.textContent = b ? b.pct + '%' + (b.carregando ? ' (carregando)' : '') : 'Sem bateria';
    bateria.extra.hidden = !!b;
    bateria.extra.textContent = 'PC de mesa, ligado na tomada';
    const dk = m.disco || {};
    disco.valor.textContent = gb(dk.livre || 0) + ' livres de ' + gb(dk.total || 0);
  }

  const soltarMsg = ctx.aoMensagem('info', pintar);
  let soltar = null;
  return {
    el: raiz,
    aoTopo(sim) {
      if (sim && !soltar) soltar = ctx.assinar('info');
      else if (!sim && soltar) { soltar(); soltar = null; }
    },
    destruir() {
      soltarMsg();
      if (soltar) { soltar(); soltar = null; }
    },
  };
}
