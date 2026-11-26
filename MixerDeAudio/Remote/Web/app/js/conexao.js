// Conexão com o PC e pareamento (D11), portados de antigo/05-conexao.js e antigo/06-pareamento.js com as mesmas regras:
// /sessao 401 leva ao pareamento, qualquer outra falha fica em "Reconectando…" (espelha RemoteRules.DecidePage);
// espera crescente de 0,5 s a 5 s (espelha RemoteRules.ReconnectDelayMs); 4000/4001 = desconectado; 4009/"ocupado" = ocupado;
// reconecta ao voltar a ficar visível.
import { definirEstado } from './estado.js';

let ws = null;
let abriu = false;
let tentativas = 0;
let timer = null;
let desconectado = false;
let ocupado = false;
let pareando = false;
let retomavel = false;
let h = {};
const ouvintesMsg = new Map(); // tipo -> Set de funções (mensagens que a conexão não trata: janelas, clip, info...)
const ouvintesAbriu = new Set(); // chamados a cada conexão aberta (a primeira e as reconexões)

// h: { aoConectado, aoOcupado(por), aoDesconectado(mensagem), aoPareamento, aoReconectando(ligado, texto), aoAviso(texto) }
export function iniciarConexao(handlers) {
  h = handlers;
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible' && retomavel && !(ws && ws.readyState === 1)) {
      clearTimeout(timer);
      timer = null;
      conectar();
    }
  });
  const m = /^#c=(\d{6})$/.exec(location.hash);
  if (m) {
    pareando = true;
    h.aoPareamento(m[1]);
    parear(m[1]);
  } else {
    h.aoReconectando(true, 'Conectando…');
    conectar();
  }
}

export function enviar(obj) {
  if (ws && ws.readyState === 1) ws.send(JSON.stringify(obj));
}

// Recebe as mensagens do PC que a conexão não trata (tudo menos estado, ocupado, desconectado e aviso), por tipo (`m.t`).
// fn(mensagem). Devolve a função que cancela.
export function aoMensagem(tipo, fn) {
  let conj = ouvintesMsg.get(tipo);
  if (!conj) { conj = new Set(); ouvintesMsg.set(tipo, conj); }
  conj.add(fn);
  return () => conj.delete(fn);
}

// fn() a cada vez que o WebSocket abre (primeira conexão e reconexões); usado por assinaturas.js para mandar de novo os `assinar`.
// Devolve a função que cancela.
export function aoAbrir(fn) {
  ouvintesAbriu.add(fn);
  return () => ouvintesAbriu.delete(fn);
}

// Botão "Conectar" da tela de desconectado.
export function reconectarAgora() {
  desconectado = false;
  ocupado = false;
  h.aoReconectando(true, 'Conectando…');
  conectar();
}

// Devolve null se pareou, ou o texto do erro.
export async function parear(codigo) {
  let erro = null;
  try {
    const r = await fetch('/parear', { method: 'POST', body: JSON.stringify({ codigo }) });
    if (r.ok) {
      history.replaceState(history.state, '', location.pathname);
      pareando = false;
      conectar();
    } else {
      // Fase 16 (D2 a D4): o PC explica o motivo no JSON ({"motivo":"fechado"|"recusado"|"bloqueado"|"ocupado"|"limite"}); sem JSON, vale o status
      let motivo = '';
      try { motivo = (await r.json()).motivo || ''; } catch { /* sem corpo JSON */ }
      erro = motivo === 'fechado' ? 'Abra as Configurações no PC para parear.'
        : motivo === 'recusado' ? 'O PC recusou.'
        : motivo === 'limite' ? 'Esqueça um celular nas Configurações do PC.'
        : r.status === 401 ? 'Código incorreto.'
        : r.status === 429 ? 'Muitas tentativas. Aguarde alguns minutos ou abra as Configurações no PC.'
        : r.status === 409 ? 'Outro pareamento está esperando confirmação no PC.'
        : 'Não foi possível conectar (' + r.status + ').';
    }
  } catch {
    erro = 'Sem conexão com o PC.';
  }
  if (erro) h.aoPareamento(null, erro);
  return erro;
}

function conectar() {
  if (ws && (ws.readyState === 0 || ws.readyState === 1)) return;
  clearTimeout(timer);
  timer = null;
  abriu = false;
  let s;
  try { s = new WebSocket('ws://' + location.host + '/ws'); } catch { esperar(); return; }
  ws = s;
  s.onopen = () => {
    abriu = true;
    tentativas = 0;
    h.aoReconectando(false);
    for (const fn of [...ouvintesAbriu]) { try { fn(); } catch (e) { console.error(e); } }
  };
  s.onmessage = (e) => {
    let m;
    try { m = JSON.parse(e.data); } catch { return; }
    if (!m) return;
    if (m.t === 'estado') {
      ocupado = false;
      pareando = false;
      retomavel = true;
      definirEstado(m);
      h.aoConectado();
    } else if (m.t === 'ocupado') {
      ocupado = true;
      retomavel = true;
      pareando = false;
      h.aoOcupado(m.por);
    } else if (m.t === 'desconectado') {
      desconectado = true;
    } else if (m.t === 'aviso') {
      h.aoAviso(m.msg);
    } else {
      const conj = ouvintesMsg.get(m.t);
      if (conj) for (const fn of [...conj]) { try { fn(m); } catch (e) { console.error(e); } }
    }
  };
  s.onclose = (e) => {
    if (ws !== s) return;
    ws = null;
    if (desconectado || e.code === 4000 || e.code === 4001) {
      desconectado = true;
      retomavel = false;
      h.aoDesconectado(e.code === 4001 ? 'Esta página foi substituída por outra conexão deste celular.' : 'Este celular foi desconectado do PC.');
      return;
    }
    if (ocupado || e.code === 4009) {
      ocupado = true;
      h.aoOcupado();
      esperar(5000);
      return;
    }
    if (abriu) { h.aoReconectando(true, 'Reconectando…'); esperar(); return; }
    // nunca abriu: só um 401 de /sessao (cookie ausente ou chave desconhecida) leva ao pareamento;
    // sem resposta, 5xx e qualquer outro status ficam em "Reconectando…"
    fetch('/sessao', { cache: 'no-store' }).then((r) => {
      if (r.status === 401) { tentativas = 0; pareando = true; retomavel = false; h.aoPareamento(); } else falhou();
    }).catch(falhou);
  };
}

function falhou() {
  if (!pareando) h.aoReconectando(true, 'Reconectando…');
  esperar();
}

// espera crescente: 0,5 s, 1 s, 2 s, 4 s, 5 s
function esperar(ms) {
  if (timer) return;
  if (!ms) { ms = Math.min(5000, 500 << Math.min(tentativas, 4)); tentativas++; }
  timer = setTimeout(() => { timer = null; conectar(); }, ms);
}
