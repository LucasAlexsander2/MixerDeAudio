// Tela Janelas (Fase 15d, D17): lista de janelas do PC com ícone, trazer para a frente, minimizar e fechar (com confirmação).
// Funções e mensagens da página antiga (antigo/15-janelas.js): `janelas` (pede a lista), `janela` { id, acao } e a assinatura `janelas`.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarChip } from '../componentes/chip.js';
import { criarBotao } from '../componentes/botao.js';
import { criarCartao } from '../componentes/cartao.js';
import { criarAviso } from '../componentes/aviso.js';

const ATRASO_ATUALIZAR_MS = 900; // a página antiga pede a lista de novo 900 ms depois de cada ação

export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'janelas';

  const atualizar = criarChip({ texto: 'Atualizar', aoClicar: () => { ctx.enviar({ t: 'janelas' }); ctx.toast('Lista atualizada'); } });
  const cab = criarCabecalho({ titulo: 'Janelas', aoVoltar: () => ctx.voltar(), acoes: [atualizar.el] });

  // sem data-sem-gesto: a lista só rola na vertical, então o gesto de arrastar para voltar continua valendo sobre ela
  const lista = document.createElement('div');
  lista.className = 'janelas-lista';
  const vazio = document.createElement('p');
  vazio.className = 'texto-sec janelas-vazio';
  vazio.textContent = 'Nenhuma janela aberta';
  el.append(cab.el, lista, vazio);

  let assinatura = '';
  let soltar = null;
  let noTopo = false;
  let aviso = null;
  const tempos = new Set();

  function limparTempos() {
    for (const id of tempos) clearTimeout(id);
    tempos.clear();
  }
  function pedirDepois() {
    const id = setTimeout(() => { tempos.delete(id); if (noTopo) ctx.enviar({ t: 'janelas' }); }, ATRASO_ATUALIZAR_MS);
    tempos.add(id);
  }
  function agir(id, acao) {
    ctx.enviar({ t: 'janela', id, acao });
    pedirDepois();
  }
  function confirmarFechar(w) {
    if (aviso) aviso.destruir();
    aviso = criarAviso({
      container: ctx.camadas,
      titulo: 'Fechar esta janela?',
      texto: '"' + (w.titulo || w.proc || 'Janela') + '" será fechada no PC. O programa pode perguntar se você quer salvar.',
      rotuloAcao: 'Fechar',
      perigo: true,
      aoConfirmar: () => { agir(w.id, 'fechar'); ctx.toast('Fechando a janela…'); },
    });
    aviso.abrir();
  }

  function icone(w) {
    const inicial = document.createElement('span');
    inicial.className = 'janelas-ico';
    inicial.textContent = (w.proc || w.titulo || '?').charAt(0).toUpperCase();
    if (!w.icone) return inicial;
    const img = new Image();
    img.alt = '';
    img.className = 'janelas-ico';
    img.width = img.height = 40;
    img.onerror = () => img.replaceWith(inicial);
    img.src = '/icone/' + encodeURIComponent(w.icone);
    return img;
  }

  function cartao(w) {
    const nome = document.createElement('b');
    nome.className = 'janelas-nome';
    nome.textContent = w.titulo || '(sem título)';
    const sub = document.createElement('span');
    sub.className = 'texto-sec janelas-sub';
    sub.textContent = (w.proc || '') + (w.min ? (w.proc ? ' · ' : '') + 'minimizada' : '');
    const textos = document.createElement('div');
    textos.className = 'janelas-textos';
    textos.append(nome, sub);
    const topo = document.createElement('div');
    topo.className = 'janelas-topo';
    topo.append(icone(w), textos);

    const frente = criarBotao({ texto: 'Frente', aoClicar: () => { agir(w.id, 'frente'); ctx.toast('Trazendo para a frente'); } });
    const minimizar = criarBotao({ texto: 'Minimizar', aoClicar: () => { agir(w.id, 'minimizar'); ctx.toast('Minimizando'); } });
    const fechar = criarBotao({ texto: 'Fechar', variante: 'perigo', aoClicar: () => confirmarFechar(w) });
    const acoes = document.createElement('div');
    acoes.className = 'linha-botoes';
    acoes.append(frente.el, minimizar.el, fechar.el);

    return criarCartao({ classe: 'janelas-cartao' + (w.min ? ' minimizada' : ''), filhos: [topo, acoes] }).el;
  }

  function desenhar(itens) {
    const sig = JSON.stringify(itens);
    if (sig === assinatura) return;
    assinatura = sig;
    vazio.hidden = itens.length > 0;
    lista.replaceChildren(...itens.map(cartao));
  }

  const cancelar = ctx.aoMensagem('janelas', (m) => desenhar(Array.isArray(m.lista) ? m.lista : []));
  desenhar([]);

  return {
    el,
    aoTopo(sim) {
      noTopo = sim;
      if (sim) {
        if (!soltar) soltar = ctx.assinar('janelas');
        ctx.enviar({ t: 'janelas' });
      } else {
        if (soltar) { soltar(); soltar = null; }
        limparTempos();
      }
    },
    destruir() {
      noTopo = false;
      cancelar();
      if (soltar) { soltar(); soltar = null; }
      limparTempos();
      if (aviso) { aviso.destruir(); aviso = null; }
    },
  };
}
