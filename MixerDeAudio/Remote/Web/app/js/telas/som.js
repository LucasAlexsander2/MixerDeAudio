// Tela Som (Fase 15a): saídas (tocar = virar a padrão), volume geral com mudo e um cartão por app
// (mudo, volume, saída pela folha, Anterior/Pausa/Próxima e a música). Funções e mensagens da página antiga (antigo/08-som.js).
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarFaixa } from '../componentes/faixa.js';
import { criarBloco } from '../componentes/bloco.js';
import { criarBarra } from '../componentes/barra.js';
import { criarCartao } from '../componentes/cartao.js';
import { criarCartaoApp } from '../componentes/cartaoapp.js';
import { criarFolha } from '../componentes/folha.js';
import { icone } from '../icones.js';
import { vibrar } from '../util.js';
import { iconeDaSaida } from './inicio.js';

// "Música · Artista" (ou só o título); sem mídia, nulo (o cartão mostra "Sem música" e esconde Anterior/Pausa/Próxima).
export function textoMusica(midia) {
  if (!midia || !midia.titulo) return null;
  return midia.artista ? midia.titulo + ' · ' + midia.artista : midia.titulo;
}

// A saída que o cartão do app mostra: a rota ativa; sem ela, a saída padrão do PC.
export function saidaDoApp(app, saidas) {
  const rota = (app.rotas || []).find((r) => r.ativa) || null;
  if (rota) return { id: rota.id, nome: rota.nome, icone: iconeDaSaida(rota.glifo) };
  const padrao = (saidas || []).find((s) => s.ativa);
  return padrao ? { id: padrao.id, nome: padrao.nome, icone: iconeDaSaida(padrao.glifo) } : { id: '', nome: 'Saída', icone: 'speaker' };
}

// ctx: ver telas/LEIAME.md. Devolve { el, aoTopo?(sim), destruir?() }.
export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'som';
  const cab = criarCabecalho({ titulo: 'Som', aoVoltar: () => ctx.voltar() });

  // saídas
  const rotuloSaidas = document.createElement('div');
  rotuloSaidas.className = 'rotulo';
  rotuloSaidas.textContent = 'Dispositivos conectados';
  const saidas = criarFaixa();

  // volume geral
  const mudoGeral = document.createElement('button');
  mudoGeral.type = 'button';
  mudoGeral.className = 'som-mudo-geral';
  mudoGeral.setAttribute('aria-label', 'Mudo geral');
  const mudoIc = document.createElement('i');
  mudoIc.className = 'ic';
  mudoGeral.append(mudoIc);
  let geralMudo = false;
  const barraGeral = criarBarra({ rotulo: 'Volume geral', aoMudar: (v) => ctx.enviar({ t: 'geral', v }) });
  mudoGeral.addEventListener('click', () => { vibrar(); ctx.enviar({ t: 'geralMudo', m: !geralMudo }); });
  const geral = criarCartao({ classe: 'som-geral', filhos: [mudoGeral, barraGeral.el] });

  // apps
  const rotuloApps = document.createElement('div');
  rotuloApps.className = 'rotulo';
  rotuloApps.textContent = 'Aplicações';
  const lista = document.createElement('div');
  lista.className = 'som-apps';
  const vazio = document.createElement('p');
  vazio.className = 'texto-sec';
  vazio.textContent = 'Nenhum app com som no momento.';
  vazio.hidden = true;
  el.append(cab.el, rotuloSaidas, saidas.el, geral.el, rotuloApps, lista, vazio);

  let assinaturaSaidas = '';
  const cartoes = new Map(); // chave do app -> { cartao, avatar, ... }
  let folha = null;

  function desenharGeral(g) {
    const disp = !!g.disponivel;
    geralMudo = !!g.mudo;
    mudoIc.innerHTML = icone(geralMudo ? 'mute' : 'volume');
    mudoGeral.classList.toggle('mudo', geralMudo);
    mudoGeral.setAttribute('aria-pressed', String(geralMudo));
    mudoGeral.disabled = !disp;
    barraGeral.habilitar(disp);
    geral.el.classList.toggle('indisponivel', !disp);
    barraGeral.definir(g.volume || 0);
  }

  function desenharSaidas(lista0) {
    const sig = JSON.stringify(lista0.map((s) => [s.id, s.nome, s.glifo, !!s.ativa]));
    if (sig === assinaturaSaidas) return;
    assinaturaSaidas = sig;
    saidas.definir(lista0.map((s) => criarBloco({
      nome: s.nome,
      icone: iconeDaSaida(s.glifo),
      ligado: !!s.ativa,
      aoClicar: () => ctx.enviar({ t: 'padrao', id: s.id }),
    }).el));
    rotuloSaidas.hidden = saidas.el.hidden = lista0.length === 0;
  }

  // o ícone do app vem de /icone/<id>; sem ele, fica a inicial do nome (como na página antiga)
  function colocarIcone(c) {
    if (!c.icone) return;
    const img = new Image();
    img.alt = '';
    img.className = 'som-icone';
    img.onload = () => { c.avatar.replaceChildren(img); c.comIcone = img; };
    img.src = '/icone/' + encodeURIComponent(c.icone);
  }

  function abrirFolha(chave) {
    const c = cartoes.get(chave);
    const estado = ctx.obterEstado();
    const app = estado && (estado.apps || []).find((a) => a.chave === chave);
    if (!c || !app || !(app.rotas || []).length) return;
    if (folha) folha.destruir();
    const ativa = (app.rotas.find((r) => r.ativa) || {}).id;
    const f = criarFolha({
      container: ctx.camadas,
      titulo: 'Saída de áudio de ' + app.nome,
      opcoes: app.rotas.map((r) => ({ valor: r.id, texto: r.nome, icone: iconeDaSaida(r.glifo) })),
      valor: ativa,
      aoEscolher: (id) => ctx.enviar({ t: 'rota', app: chave, id }),
      aoFechar: () => { f.destruir(); if (folha === f) folha = null; },
    });
    folha = f;
    f.abrir();
  }

  function criarCartaoDoApp(a) {
    const chave = a.chave;
    const cartao = criarCartaoApp({
      nome: a.nome,
      aoVolume: (v) => ctx.enviar({ t: 'volume', app: chave, v }),
      aoMudo: (m) => ctx.enviar({ t: 'mudo', app: chave, m }),
      aoSaida: () => abrirFolha(chave),
      aoAnterior: () => ctx.enviar({ t: 'midia', app: chave, acao: 'ant' }),
      aoPausa: () => ctx.enviar({ t: 'midia', app: chave, acao: 'pp' }),
      aoProxima: () => ctx.enviar({ t: 'midia', app: chave, acao: 'prox' }),
    });
    const c = {
      cartao,
      avatar: cartao.el.querySelector('.avatar'),
      chipSaida: cartao.el.querySelector('.chip-saida'),
      botoesMidia: [...cartao.el.querySelectorAll('.botao-midia')],
      icone: a.icone,
      nome: a.nome,
      comIcone: null,
    };
    colocarIcone(c);
    cartoes.set(chave, c);
    return c;
  }

  function atualizarCartao(c, a, estado) {
    const { cartao } = c;
    if (c.nome !== a.nome) {
      c.nome = a.nome;
      cartao.definirNome(a.nome); // reescreve o avatar (a inicial): o ícone entra de novo
      c.comIcone = null;
      colocarIcone(c);
    }
    if (c.icone !== a.icone) { c.icone = a.icone; colocarIcone(c); }
    const md = a.midia || null;
    cartao.definirMusica(textoMusica(md));
    cartao.definirTocando(!!(md && md.tocando));
    if (md) { c.botoesMidia[0].disabled = !md.ant; c.botoesMidia[1].disabled = !md.pp; c.botoesMidia[2].disabled = !md.prox; }
    // app só de mídia (sem sessão de áudio): sem mudo nem volume, como na página antiga
    cartao.barra.el.hidden = !!a.soMidia;
    c.avatar.disabled = !!a.soMidia;
    if (!a.soMidia) { cartao.definirVolume(a.volume || 0); cartao.definirMudo(!!a.mudo); } else cartao.definirMudo(false);
    const saida = saidaDoApp(a, estado.saidas);
    cartao.definirSaida(saida.nome, saida.icone);
    c.chipSaida.hidden = !(a.rotas || []).length;
  }

  function desenhar(estado) {
    if (!estado) return;
    desenharSaidas(estado.saidas || []);
    desenharGeral(estado.geral || {});
    const apps = estado.apps || [];
    const vistos = new Set();
    apps.forEach((a, i) => {
      vistos.add(a.chave);
      const c = cartoes.get(a.chave) || criarCartaoDoApp(a);
      atualizarCartao(c, a, estado);
      if (lista.children[i] !== c.cartao.el) lista.insertBefore(c.cartao.el, lista.children[i] || null);
    });
    for (const [chave, c] of [...cartoes]) {
      if (vistos.has(chave)) continue;
      c.cartao.el.remove();
      cartoes.delete(chave);
    }
    vazio.hidden = apps.length > 0;
  }

  const cancelar = ctx.aoMudar(desenhar);
  desenhar(ctx.obterEstado());

  return {
    el,
    aoTopo(sim) {
      if (!sim && folha) folha.fechar();
    },
    destruir() {
      cancelar();
      if (folha) folha.destruir();
    },
  };
}
