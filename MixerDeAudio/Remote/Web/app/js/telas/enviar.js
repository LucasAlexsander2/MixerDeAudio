// Tela Enviar (Fase 15d, D17): área de transferência nos dois sentidos e envio de arquivos com progresso.
// Mensagens e regras da página antiga (antigo/13-enviar.js): `clipLer`, `clipGravar` { s } (máximo 4.000 caracteres),
// resposta `clip` { s, cortado } e `POST /arquivo?nome=` por XHR (máximo 500 MB, erros 401/403/409/411/413/507).
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarCartao } from '../componentes/cartao.js';
import { criarBotao } from '../componentes/botao.js';
import { criarChip } from '../componentes/chip.js';
import { criarMedidor } from '../componentes/medidor.js';

export const MAX_TEXTO = 4000;
export const MAX_ARQUIVO = 524288000; // 500 MB
export const ERROS_ARQUIVO = {
  401: 'Pareamento recusado.',
  403: 'O envio de arquivos está desligado no PC.',
  409: 'Outro envio em andamento ou este celular não está no controle.',
  411: 'O navegador não informou o tamanho.',
  413: 'Arquivo grande demais (máximo 500 MB).',
  507: 'Sem espaço livre no PC.',
};

export function tamanho(n) {
  return n >= 1048576 ? (n / 1048576).toFixed(1).replace('.', ',') + ' MB' : Math.max(1, Math.round(n / 1024)) + ' KB';
}

function rotulo(texto) {
  const d = document.createElement('div');
  d.className = 'rotulo';
  d.textContent = texto;
  return d;
}

export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'enviar';
  const cab = criarCabecalho({ titulo: 'Enviar', aoVoltar: () => ctx.voltar() });

  // ---------- área de transferência ----------
  // o texto do PC é selecionável com o dedo; arrastar nele não pode voltar a tela
  const textoPc = document.createElement('div');
  textoPc.className = 'enviar-clip texto-sec';
  textoPc.setAttribute('data-sem-gesto', '');
  const vazioPc = 'A área de transferência do PC ainda não foi lida.';
  textoPc.textContent = vazioPc;
  const titPc = document.createElement('b');
  titPc.textContent = 'Copiado no PC';
  const nota = document.createElement('div');
  nota.className = 'texto-sec enviar-nota';
  nota.hidden = true;
  const ler = criarBotao({ texto: 'Ler do PC', icone: 'clip', aoClicar: () => ctx.enviar({ t: 'clipLer' }) });
  const copiar = criarBotao({ texto: 'Copiar para o celular', icone: 'clip', aoClicar: () => copiarParaCelular() });
  copiar.habilitar(false);
  const linhaPc = document.createElement('div');
  linhaPc.className = 'linha-botoes';
  linhaPc.append(ler.el, copiar.el);
  const cartaoPc = criarCartao({ classe: 'enviar-cartao', filhos: [titPc, textoPc, nota, linhaPc] });

  const campo = document.createElement('textarea');
  campo.className = 'enviar-campo';
  campo.rows = 3;
  campo.placeholder = 'Escreva ou cole o texto aqui';
  campo.setAttribute('aria-label', 'Texto para enviar ao PC');
  const titEnv = document.createElement('b');
  titEnv.textContent = 'Mandar para o PC';
  const mandar = criarBotao({ texto: 'Enviar', variante: 'principal', aoClicar: () => mandarTexto() });
  const cartaoEnv = criarCartao({ classe: 'enviar-cartao', filhos: [titEnv, campo, mandar.el] });

  let textoLido = '';
  function mostrarTexto(s, cortado) {
    textoLido = s || '';
    textoPc.textContent = textoLido || 'A área de transferência do PC está sem texto';
    textoPc.classList.toggle('texto-sec', !textoLido);
    copiar.habilitar(!!textoLido);
    nota.hidden = !cortado;
    nota.textContent = cortado ? 'O texto era grande e foi cortado.' : '';
    if (!textoLido) ctx.toast('A área de transferência do PC está sem texto');
  }
  function copiarParaCelular() {
    if (!textoLido) return;
    const selecionar = () => {
      const r = document.createRange();
      r.selectNodeContents(textoPc);
      const q = window.getSelection();
      q.removeAllRanges();
      q.addRange(r);
      ctx.toast('Selecionado: copie manualmente');
    };
    try {
      navigator.clipboard.writeText(textoLido).then(() => ctx.toast('Copiado para o celular'), selecionar);
    } catch { selecionar(); }
  }
  function mandarTexto() {
    const v = campo.value;
    if (!v.trim()) { ctx.toast('Escreva algo primeiro'); return; }
    if (v.length > MAX_TEXTO) { ctx.toast('Máximo de 4.000 caracteres por envio'); return; }
    ctx.enviar({ t: 'clipGravar', s: v });
    campo.value = '';
    ctx.toast('Texto enviado ao PC');
  }

  // ---------- arquivos ----------
  const entrada = document.createElement('input');
  entrada.type = 'file';
  entrada.multiple = true;
  entrada.hidden = true;
  const escolher = criarBotao({ texto: 'Escolher arquivo', icone: 'file', aoClicar: () => entrada.click() });
  const linhaArq = document.createElement('div');
  linhaArq.className = 'linha-botoes';
  linhaArq.append(escolher.el);
  const lista = document.createElement('div');
  lista.className = 'enviar-lista';
  const nenhum = document.createElement('p');
  nenhum.className = 'texto-sec enviar-vazio';
  nenhum.textContent = 'Nenhum arquivo enviado ainda';
  lista.append(nenhum);

  let ativo = null; // { item, xhr, cancelado }
  const fila = [];
  let destruida = false;

  function novoItem(arquivo) {
    const nome = document.createElement('b');
    nome.className = 'enviar-nome';
    nome.textContent = arquivo.name + ' · ' + tamanho(arquivo.size);
    const estado = document.createElement('span');
    estado.className = 'texto-sec mono enviar-estado';
    const cancelar = criarChip({ texto: 'Cancelar', icone: 'x', aoClicar: () => cancelarItem(item) });
    const topo = document.createElement('div');
    topo.className = 'enviar-topo';
    topo.append(nome, estado, cancelar.el);
    const barra = criarMedidor({ rotulo: 'Progresso de ' + arquivo.name });
    const c = criarCartao({ classe: 'enviar-cartao', filhos: [topo, barra.el] });
    const item = { arquivo, el: c.el, estado, barra, cancelar, fim: false };
    return item;
  }
  function terminar(item, texto, { ok = false, erro = false } = {}) {
    item.fim = true;
    item.estado.textContent = texto;
    item.estado.classList.toggle('ok', ok);
    item.estado.classList.toggle('erro', erro);
    item.cancelar.el.hidden = true;
    if (!ok) item.barra.el.hidden = true;
    else item.barra.definir(100);
  }
  function cancelarItem(item) {
    if (item.fim) return;
    if (ativo && ativo.item === item) { ativo.cancelado = true; ativo.xhr.abort(); return; }
    const i = fila.indexOf(item);
    if (i >= 0) fila.splice(i, 1);
    terminar(item, 'Cancelado');
  }
  function proximo() {
    if (ativo || destruida) return;
    const item = fila.shift();
    if (!item) return;
    enviarArquivo(item);
  }
  function enviarArquivo(item) {
    const f = item.arquivo;
    const x = new XMLHttpRequest();
    const a = { item, xhr: x, cancelado: false };
    ativo = a;
    const fim = (texto, opcoes) => {
      ativo = null;
      terminar(item, texto, opcoes);
      if (destruida && opcoes && opcoes.erro) ctx.toast(texto);
      if (!destruida) proximo();
    };
    x.open('POST', '/arquivo?nome=' + encodeURIComponent(f.name));
    x.upload.onprogress = (e) => {
      if (!e.lengthComputable) return;
      const p = Math.round((e.loaded * 100) / e.total);
      item.barra.definir(p);
      item.estado.textContent = p + '%';
    };
    x.onload = () => {
      let r = {};
      try { r = JSON.parse(x.responseText); } catch { /* sem corpo JSON */ }
      if (x.status === 200) {
        const nome = r.nome || f.name;
        fim('Enviado: ' + nome, { ok: true });
        ctx.toast('Enviado: ' + nome);
      } else {
        fim(ERROS_ARQUIVO[x.status] || 'Falha ao enviar (' + x.status + ').', { erro: true });
      }
    };
    x.onabort = () => fim(a.cancelado ? 'Cancelado' : 'A conexão caiu durante o envio.', { erro: !a.cancelado });
    x.onerror = x.ontimeout = () => fim('A conexão caiu durante o envio.', { erro: true });
    item.estado.textContent = 'Enviando…';
    item.barra.definir(0);
    x.send(f);
  }
  function adicionar(arquivos) {
    for (const f of arquivos) {
      nenhum.remove();
      const item = novoItem(f);
      lista.insertBefore(item.el, lista.firstChild);
      if (f.size > MAX_ARQUIVO) { terminar(item, ERROS_ARQUIVO[413], { erro: true }); continue; }
      item.estado.textContent = 'Na fila';
      fila.push(item);
    }
    proximo();
  }
  entrada.addEventListener('change', () => {
    adicionar(Array.from(entrada.files || []));
    entrada.value = '';
  });

  el.append(cab.el, rotulo('Área de transferência'), cartaoPc.el, cartaoEnv.el, rotulo('Arquivos'), linhaArq, entrada, lista);

  const cancelarClip = ctx.aoMensagem('clip', (m) => mostrarTexto(m.s, !!m.cortado));
  let primeiraVez = true;

  return {
    el,
    aoTopo(sim) {
      // ao abrir pela primeira vez, já mostra o que está copiado no PC (o botão "Ler do PC" lê de novo)
      if (sim && primeiraVez) { primeiraVez = false; ctx.enviar({ t: 'clipLer' }); }
    },
    destruir() {
      destruida = true;
      cancelarClip();
      // o envio que já está em andamento termina sozinho (o resultado sai em um toast); o resto da fila é descartado
      fila.length = 0;
    },
  };
}
