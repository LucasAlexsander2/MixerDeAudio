// Início (Fase 15a): estado rápido (CPU, GPU, RAM), favoritos (D11), "Todas as funções" em blocos e Desconectar.
// Mesma regra de grupos da página antiga (antigo/10-menu.js, renderPc): cada área só aparece se o grupo dela estiver ligado no PC.
import { criarBloco } from '../componentes/bloco.js';
import { criarBotao } from '../componentes/botao.js';
import { criarChip } from '../componentes/chip.js';
import { criarFaixa } from '../componentes/faixa.js';
import { criarMedidor } from '../componentes/medidor.js';
import { criarAviso } from '../componentes/aviso.js';

export const AREAS = [
  { nome: 'som', rotulo: 'Som', icone: 'volume', grupo: null },
  { nome: 'tela', rotulo: 'Tela do PC', icone: 'monitor', grupo: 'tela' },
  { nome: 'controle', rotulo: 'Controle', icone: 'gamepad', grupo: 'entrada' },
  { nome: 'energia', rotulo: 'Energia', icone: 'power', grupo: 'energia' },
  { nome: 'monitores', rotulo: 'Monitores', icone: 'monitors', grupo: 'monitores' },
  { nome: 'janelas', rotulo: 'Janelas', icone: 'windows', grupo: 'janelas' },
  { nome: 'atalhos', rotulo: 'Atalhos', icone: 'shortcut', grupo: 'atalhos' },
  { nome: 'enviar', rotulo: 'Enviar', icone: 'send', grupo: 'transferencia' },
  { nome: 'timer', rotulo: 'Timer de sono', icone: 'moon', grupo: 'estado' },
  { nome: 'estado', rotulo: 'Estado do PC', icone: 'activity', grupo: 'estado' },
];

// Quais áreas aparecem para o estado `ferr` recebido do PC.
export function areasVisiveis(ferr) {
  const f = ferr || {};
  return AREAS.filter((a) => a.grupo === null || !!f[a.grupo]);
}

// ---------- favoritos (D11, P1): catálogo fixo ----------
// Itens fixos (ação e ícone próprios). As saídas de áudio (uma por saída do PC) e as áreas (uma por área de AREAS)
// entram na lista por `itensCatalogo`. `tipo` diz o que o toque faz (ver `executar`).
export const CATALOGO_FIXO = [
  { id: 'mudo', rotulo: 'Mudo geral', curto: 'Mudo', icone: 'volume', tipo: 'mudo' },
  { id: 'brilho', rotulo: 'Brilho', curto: 'Brilho', icone: 'sun', tipo: 'brilho', grupo: 'monitores' },
  { id: 'bloquear', rotulo: 'Bloquear', curto: 'Bloquear', icone: 'lock', tipo: 'bloquear', grupo: 'energia' },
  { id: 'play', rotulo: 'Play/Pausa', curto: 'Play/Pausa', icone: 'play', tipo: 'play' },
];
export const TIPOS_FAVORITO = ['mudo', 'saida', 'play', 'bloquear', 'brilho', 'area'];
export const CHAVE_FAVORITOS = 'inicio.favoritos';

// O glifo de cada saída vem do PC como caractere do Segoe Fluent Icons (DeviceGlyph.cs).
export function iconeDaSaida(glifo) {
  const c = glifo ? glifo.codePointAt(0) : 0;
  if (c === 0xe7f6) return 'headphones';
  if (c === 0xe7f4) return 'monitor';
  return 'speaker';
}

// O app que o Play/Pausa do favorito controla: o que está tocando; senão, o primeiro com música e botão de pausa.
export function appDePlay(estado) {
  const apps = (estado && estado.apps) || [];
  return apps.find((a) => a.midia && a.midia.pp && a.midia.tocando) || apps.find((a) => a.midia && a.midia.pp) || null;
}

// Lista completa do catálogo para o estado atual, na ordem padrão do protótipo. Cada item:
// { id, rotulo (Opções), curto (bloco), icone, tipo, arg, ligado, ativo, disponivel }.
export function itensCatalogo(estado) {
  const ferr = (estado && estado.ferr) || {};
  const itens = [];
  const fixo = (id) => CATALOGO_FIXO.find((i) => i.id === id);
  const adicionar = (base, extra) => {
    const disponivel = base.grupo ? !!ferr[base.grupo] : true;
    itens.push({ ligado: false, ativo: false, ...base, disponivel, ...extra });
  };
  const mudo = !!(estado && estado.geral && estado.geral.mudo);
  adicionar(fixo('mudo'), { icone: mudo ? 'mute' : 'volume', ligado: mudo, ativo: mudo, disponivel: !!(estado && estado.geral && estado.geral.disponivel) });
  for (const s of (estado && estado.saidas) || []) {
    adicionar({ id: 'saida:' + s.id, rotulo: 'Saída: ' + s.nome, curto: s.nome, icone: iconeDaSaida(s.glifo), tipo: 'saida', arg: s.id },
      { ligado: !!s.ativa, ativo: !!s.ativa });
  }
  adicionar(fixo('brilho'), {});
  adicionar(fixo('bloquear'), {});
  const tela = AREAS.find((a) => a.nome === 'tela');
  adicionar({ id: 'area:tela', rotulo: tela.rotulo, curto: tela.rotulo, icone: tela.icone, tipo: 'area', arg: 'tela', grupo: tela.grupo }, {});
  const play = appDePlay(estado);
  adicionar(fixo('play'), { icone: play && play.midia.tocando ? 'pause' : 'play', disponivel: !!play });
  for (const a of AREAS) {
    if (a.nome === 'tela') continue;
    adicionar({ id: 'area:' + a.nome, rotulo: a.rotulo, curto: a.rotulo, icone: a.icone, tipo: 'area', arg: a.nome, grupo: a.grupo }, {});
  }
  return itens;
}

// Os favoritos padrão (os do protótipo): Mudo, as saídas, Brilho, Bloquear, Tela do PC e Play/Pausa.
export function favoritosPadrao(itens) {
  const padrao = new Set(['mudo', 'brilho', 'bloquear', 'area:tela', 'play']);
  return itens.filter((i) => padrao.has(i.id) || i.tipo === 'saida').map((i) => i.id);
}

// Ids escolhidos: a lista guardada (inicio.favoritos) ou, se nunca escolheu, a padrão.
export function favoritosEfetivos(itens, guardado) {
  return Array.isArray(guardado) ? guardado.filter((id) => typeof id === 'string') : favoritosPadrao(itens);
}

// Os itens que aparecem na faixa: escolhidos e disponíveis, na ordem do catálogo.
export function favoritosVisiveis(itens, guardado) {
  const ids = new Set(favoritosEfetivos(itens, guardado));
  return itens.filter((i) => ids.has(i.id) && i.disponivel);
}

// ---------- estado rápido (D12) ----------
const GIB = 1073741824;
const gb = (n) => (n / GIB).toFixed(1).replace('.', ',');

function criarMetrica(nome) {
  const el = document.createElement('div');
  el.className = 'inicio-metrica';
  const linha = document.createElement('div');
  linha.className = 'inicio-metrica-linha';
  const rotulo = document.createElement('span');
  rotulo.textContent = nome;
  const valor = document.createElement('b');
  valor.className = 'mono';
  valor.textContent = '—';
  linha.append(rotulo, valor);
  const medidor = criarMedidor({ rotulo: nome });
  el.append(linha, medidor.el);
  return { el, definir(texto, pct) { valor.textContent = texto; medidor.definir(pct); } };
}

// opções: { ctx, aoAbrirArea(nome), aoAbrirOpcoes, aoDesconectar }.
// Devolve { el, atualizar(estado), aoTopo(sim), destruir() }.
export function criarTelaInicio({ ctx = {}, aoAbrirArea, aoAbrirOpcoes, aoDesconectar }) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'inicio';
  const prefs = ctx.preferencias || {};
  const lerPref = (chave, padrao) => (prefs.ler ? prefs.ler(chave, padrao) : padrao);
  const avisar = (texto) => { if (ctx.toast) ctx.toast(texto); };
  const enviar = (m) => { if (ctx.enviar) ctx.enviar(m); };

  const titulo = document.createElement('h1');
  titulo.className = 'tela-titulo';
  titulo.textContent = 'Controladora PC';
  const opcoes = criarChip({ texto: 'Opções', icone: 'gear', aoClicar: () => { if (aoAbrirOpcoes) aoAbrirOpcoes(); } });
  const topo = document.createElement('div');
  topo.className = 'inicio-topo';
  topo.append(titulo, opcoes.el);

  // estado rápido: tocar abre o Estado do PC
  const botaoEstado = document.createElement('button');
  botaoEstado.type = 'button';
  botaoEstado.className = 'inicio-estado';
  botaoEstado.setAttribute('aria-label', 'Abrir Estado do PC');
  const mCpu = criarMetrica('CPU');
  const mGpu = criarMetrica('GPU');
  const mRam = criarMetrica('RAM');
  botaoEstado.append(mCpu.el, mGpu.el, mRam.el);
  botaoEstado.addEventListener('click', () => { if (aoAbrirArea) aoAbrirArea('estado'); });
  botaoEstado.hidden = true;
  function desenharInfo(m) {
    const cpu = Math.round(+m.cpu || 0);
    mCpu.definir(cpu + '%', cpu);
    if (typeof m.gpu === 'number') mGpu.definir(Math.round(m.gpu) + '%', m.gpu);
    else mGpu.definir('—', 0);
    const mem = m.mem || {};
    if (mem.total > 0) mRam.definir(gb(mem.usada || 0) + '/' + Math.round(mem.total / GIB) + ' GB', (mem.usada / mem.total) * 100);
  }

  const rotuloFav = document.createElement('div');
  rotuloFav.className = 'rotulo';
  rotuloFav.textContent = 'Favoritos';
  const favoritos = criarFaixa({ larga: true });
  const rotulo = document.createElement('div');
  rotulo.className = 'rotulo';
  rotulo.textContent = 'Todas as funções';
  const grade = document.createElement('div');
  grade.className = 'grade3';
  const sair = criarBotao({ texto: 'Desconectar', variante: 'perigo', largo: true, aoClicar: () => aoDesconectar() });
  const rodape = document.createElement('div');
  rodape.className = 'rodape';
  rodape.textContent = 'Prévia da Controladora PC';
  el.append(topo, botaoEstado, rotuloFav, favoritos.el, rotulo, grade, sair.el, rodape);

  // Bloquear pede confirmação (D10)
  const confirmarBloqueio = ctx.camadas ? criarAviso({
    container: ctx.camadas,
    titulo: 'Bloquear o PC?',
    texto: 'Será preciso a senha para voltar.',
    rotuloAcao: 'Bloquear',
    aoConfirmar: () => enviar({ t: 'energia', acao: 'bloquear', seg: 0 }),
  }) : null;

  function executar(item, estado) {
    switch (item.tipo) {
      case 'mudo':
        enviar({ t: 'geralMudo', m: !(estado && estado.geral && estado.geral.mudo) });
        break;
      case 'saida':
        enviar({ t: 'padrao', id: item.arg });
        break;
      case 'play': {
        const app = appDePlay(estado);
        if (app) enviar({ t: 'midia', app: app.chave, acao: 'pp' });
        else avisar('Nenhum app com música');
        break;
      }
      case 'bloquear':
        if (confirmarBloqueio) confirmarBloqueio.abrir();
        else enviar({ t: 'energia', acao: 'bloquear', seg: 0 });
        break;
      case 'brilho':
        if (aoAbrirArea) aoAbrirArea('monitores');
        break;
      case 'area':
        if (aoAbrirArea) aoAbrirArea(item.arg);
        break;
      default:
        break;
    }
  }

  let ultimo = null;
  let assinaturaAreas = '';
  let assinaturaFav = '';
  let noTopo = false;
  let soltarInfo = null;
  let cancelarInfo = null;

  function desenharFavoritos() {
    const itens = itensCatalogo(ultimo);
    const lista = favoritosVisiveis(itens, lerPref(CHAVE_FAVORITOS, null));
    const sig = JSON.stringify(lista.map((i) => [i.id, i.curto, i.icone, i.ligado]));
    if (sig === assinaturaFav) return;
    assinaturaFav = sig;
    rotuloFav.hidden = favoritos.el.hidden = lista.length === 0;
    favoritos.definir(lista.map((i) => {
      const b = criarBloco({ nome: i.curto, icone: i.icone, ligado: i.ligado, ativo: i.ativo && i.tipo === 'mudo', aoClicar: () => executar(i, ultimo) });
      return b.el;
    }));
  }

  // a assinatura do `info` só existe com o Início no topo e o grupo "estado" ligado (D12)
  function sincronizar() {
    const ligado = !!(ultimo && ultimo.ferr && ultimo.ferr.estado);
    botaoEstado.hidden = !ligado;
    const quer = noTopo && ligado && !!ctx.assinar;
    if (quer && !soltarInfo) soltarInfo = ctx.assinar('info');
    else if (!quer && soltarInfo) { soltarInfo(); soltarInfo = null; }
  }

  if (ctx.aoMensagem) cancelarInfo = ctx.aoMensagem('info', desenharInfo);

  return {
    el,
    atualizar(estado) {
      ultimo = estado;
      const lista = areasVisiveis(estado && estado.ferr);
      const sig = lista.map((a) => a.nome).join(',');
      if (sig !== assinaturaAreas) {
        assinaturaAreas = sig;
        grade.replaceChildren(...lista.map((a) => criarBloco({ nome: a.rotulo, icone: a.icone, aoClicar: () => aoAbrirArea(a.nome) }).el));
      }
      desenharFavoritos();
      sincronizar();
    },
    aoTopo(sim) {
      noTopo = !!sim;
      if (noTopo) { assinaturaFav = ''; desenharFavoritos(); } // a escolha pode ter mudado em Opções
      sincronizar();
    },
    destruir() {
      noTopo = false;
      sincronizar();
      if (cancelarInfo) cancelarInfo();
      if (confirmarBloqueio) confirmarBloqueio.destruir();
    },
  };
}
