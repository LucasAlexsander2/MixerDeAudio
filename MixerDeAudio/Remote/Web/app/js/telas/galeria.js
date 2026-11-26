// Galeria (D26): tela escondida em /novo#galeria que mostra cada componente com as variantes e uma linha com o último evento.
// Abre sem o PC. Serve à conferência e ao teste no iPhone; sai na Fase 17.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarBloco } from '../componentes/bloco.js';
import { criarFaixa, criarGrade } from '../componentes/faixa.js';
import { criarBotao } from '../componentes/botao.js';
import { criarBotaoGrande } from '../componentes/botaogrande.js';
import { criarCartao } from '../componentes/cartao.js';
import { criarCartaoApp } from '../componentes/cartaoapp.js';
import { criarBarra } from '../componentes/barra.js';
import { criarChip, criarGrupoChips } from '../componentes/chip.js';
import { criarInterruptor } from '../componentes/interruptor.js';
import { criarMedidor } from '../componentes/medidor.js';
import { criarAviso } from '../componentes/aviso.js';
import { criarFolha } from '../componentes/folha.js';
import { criarTouchpad } from '../componentes/touchpad.js';

const SAIDAS = [
  { valor: 'fone', texto: 'Fones', icone: 'headphones' },
  { valor: 'caixa', texto: 'Alto-falantes', icone: 'speaker' },
  { valor: 'monitor', texto: 'Monitor', icone: 'monitor' },
];

// opções: { container (camadas), toast, dica, aoVoltar }. Devolve o elemento da tela.
export function criarTelaGaleria({ container, toast, dica, aoVoltar }) {
  const el = document.createElement('section');
  el.className = 'tela galeria';
  el.dataset.tela = 'galeria';

  // ---- linha do último evento (fica no topo) ----
  const cab = criarCabecalho({ titulo: 'Galeria', aoVoltar });
  const evento = document.createElement('div');
  evento.className = 'evento mono';
  evento.id = 'galeria-evento';
  evento.textContent = 'Último evento: nenhum';
  const topo = document.createElement('div');
  topo.className = 'galeria-topo';
  topo.append(cab.el, evento);
  el.append(topo);

  let n = 0;
  const log = (window.__galeriaLog = window.__galeriaLog || []);
  function registrar(origem, texto) {
    n++;
    evento.textContent = '#' + n + ' ' + origem + ': ' + texto;
    evento.dataset.n = String(n);
    evento.dataset.origem = origem;
    log.push({ n, t: Date.now(), origem, texto });
    if (log.length > 500) log.shift();
  }

  function secao(titulo, ...itens) {
    const r = document.createElement('div');
    r.className = 'rotulo';
    r.textContent = titulo;
    const corpo = document.createElement('div');
    corpo.className = 'galeria-corpo';
    corpo.append(...itens);
    el.append(r, corpo);
    return corpo;
  }
  const linhaBotoes = (...itens) => {
    const d = document.createElement('div');
    d.className = 'linha-botoes';
    d.append(...itens);
    return d;
  };

  // ---- cabeçalho ----
  const chipAcao = criarChip({ texto: 'Ação', icone: 'gear', aoClicar: () => registrar('chip', 'ação do cabeçalho') });
  secao('Cabeçalho (com chip de ação)', criarCabecalho({ titulo: 'Título da tela', aoVoltar: () => registrar('cabeçalho', 'voltar'), acoes: [chipAcao.el] }).el);

  // ---- blocos ----
  const nomes = ['Som', 'Tela do PC', 'Controle', 'Energia', 'Monitores', 'Janelas', 'Atalhos', 'Enviar'];
  const icones = ['volume', 'monitor', 'gamepad', 'power', 'monitors', 'windows', 'shortcut', 'send'];
  function blocos(qtd, escolha) {
    return nomes.slice(0, qtd).map((nome, i) => {
      const b = criarBloco({
        nome, icone: icones[i], ligado: escolha && i === 1, ativo: i === 0,
        aoClicar: () => {
          if (escolha) blocosEscolha.forEach((x, j) => x.definirLigado(j === i));
          registrar('bloco', nome);
        },
      });
      return b;
    });
  }
  const blocosEscolha = blocos(8, true);
  secao('Bloco: faixa de 1 linha (um é escolha, um tem ponto)', criarFaixa({ linhas: 1, blocos: blocosEscolha.map((b) => b.el) }).el);
  secao('Bloco: faixa de 2 linhas', criarFaixa({ linhas: 2, blocos: blocos(8, false).map((b) => b.el) }).el);
  secao('Bloco: grade de 2', criarGrade({ colunas: 2, blocos: blocos(4, false).map((b) => b.el) }).el);
  const bDesab = criarBloco({ nome: 'Desligado', icone: 'lock' });
  bDesab.habilitar(false);
  secao('Bloco: grade de 3 (alta, nomes longos) e desabilitado', criarGrade({
    colunas: 3, alta: true,
    blocos: [criarBloco({ nome: 'Abrir o gerenciador de tarefas', icone: 'tasks', aoClicar: () => registrar('bloco', 'tarefas') }).el,
      criarBloco({ nome: 'Calculadora', icone: 'calc', aoClicar: () => registrar('bloco', 'calculadora') }).el, bDesab.el],
  }).el);

  // ---- botão grande e botão ----
  secao('Botão grande: padrão, perigo e principal', linhaBotoes(
    criarBotaoGrande({ texto: 'Bloquear', icone: 'lock', aoClicar: () => registrar('botão grande', 'padrão') }).el,
    criarBotaoGrande({ texto: 'Desligar', icone: 'power', variante: 'perigo', aoClicar: () => registrar('botão grande', 'perigo') }).el,
    criarBotaoGrande({ texto: 'Acordar', icone: 'sun', variante: 'principal', aoClicar: () => registrar('botão grande', 'principal') }).el));
  const bOff = criarBotao({ texto: 'Desabilitado' });
  bOff.habilitar(false);
  secao('Botão: padrão, perigo, principal, com ícone e desabilitado', linhaBotoes(
    criarBotao({ texto: 'Padrão', aoClicar: () => registrar('botão', 'padrão') }).el,
    criarBotao({ texto: 'Perigo', variante: 'perigo', aoClicar: () => registrar('botão', 'perigo') }).el,
    criarBotao({ texto: 'Principal', variante: 'principal', aoClicar: () => registrar('botão', 'principal') }).el),
  linhaBotoes(
    criarBotao({ texto: 'Com ícone', icone: 'restart', aoClicar: () => registrar('botão', 'com ícone') }).el, bOff.el));

  // ---- cartão ----
  const texto = document.createElement('p');
  texto.className = 'texto-sec';
  texto.textContent = 'Cartão: --surface, canto 16, espaço interno 12.';
  secao('Cartão', criarCartao({ filhos: [texto] }).el);

  // ---- cartão de app ----
  function cartaoApp(opts) {
    const c = criarCartaoApp({
      ...opts,
      aoVolume: (v, final) => registrar('volume ' + opts.nome, v + (final ? ' (final)' : '')),
      aoMudo: (m) => registrar('mudo ' + opts.nome, m ? 'ligado' : 'desligado'),
      aoPausa: (t) => registrar('pausa ' + opts.nome, t ? 'tocando' : 'pausado'),
      aoAnterior: () => registrar('anterior', opts.nome),
      aoProxima: () => registrar('próxima', opts.nome),
      aoSaida: () => abrirFolha(c, opts.nome),
    });
    return c;
  }
  function abrirFolha(cartaoDoApp, nome) {
    const atual = cartaoDoApp.saidaAtual || 'fone';
    const f = criarFolha({
      container, titulo: 'Saída de áudio', opcoes: SAIDAS, valor: atual,
      aoEscolher: (v) => {
        const o = SAIDAS.find((x) => x.valor === v);
        cartaoDoApp.saidaAtual = v;
        cartaoDoApp.definirSaida(o.texto, o.icone);
        registrar('folha', nome + ' → ' + o.texto);
      },
      aoFechar: () => f.destruir(),
    });
    f.abrir();
  }
  const appA = cartaoApp({ nome: 'Spotify', musica: 'Lo-fi Beats · Chillhop', saida: { nome: 'Fones', icone: 'headphones' }, volume: 81, tocando: true });
  const appB = cartaoApp({ nome: 'Discord', musica: null, saida: { nome: 'Alto-falantes', icone: 'speaker' }, volume: 42 });
  const appC = cartaoApp({ nome: 'Chrome', musica: 'YouTube · Live de música', saida: { nome: 'Monitor', icone: 'monitor' }, volume: 57, mudo: true });
  const lista = document.createElement('div');
  lista.className = 'galeria-lista';
  lista.append(appA.el, appB.el, appC.el);
  secao('Cartão de app: com música, sem música e mudo', lista);

  // ---- barra e medidor ----
  const medidor = criarMedidor({ valor: 35, rotulo: 'Medidor de exemplo' });
  const barra = criarBarra({
    valor: 35, rotulo: 'Barra de exemplo',
    aoMudar: (v, final) => { medidor.definir(v); registrar('barra', v + (final ? ' (final)' : '')); },
  });
  const barraPct = criarBarra({ valor: 70, rotulo: 'Brilho', formatar: (v) => v + '%', aoMudar: (v, final) => registrar('brilho', v + (final ? ' (final)' : '')) });
  const barraOff = criarBarra({ valor: 50, rotulo: 'Barra desabilitada' });
  barraOff.habilitar(false);
  const definirBtn = criarChip({ texto: 'definir(80) agora', aoClicar: () => { barra.definir(80); registrar('definir', barra.retida() ? '80 ignorado (retida)' : '80 aplicado'); } });
  secao('Barra: normal (mexe no medidor), com formato, desabilitada', barra.el, barraPct.el, barraOff.el, definirBtn.el);
  const medidor2 = criarMedidor({ valor: 80, rotulo: 'Outro medidor' });
  secao('Medidor', medidor.el, medidor2.el);

  // ---- chips ----
  const chipMarcavel = criarChip({ texto: 'Chip marcável', marcado: false, aoClicar: () => { const v = chipMarcavel.el.getAttribute('aria-pressed') !== 'true'; chipMarcavel.marcar(v); registrar('chip', v ? 'marcado' : 'desmarcado'); } });
  const grupo = criarGrupoChips({
    opcoes: [{ valor: 15, texto: '15 min' }, { valor: 30, texto: '30 min' }, { valor: 60, texto: '1 h' }, { valor: 120, texto: '2 h' }],
    valor: 30, aoEscolher: (v) => registrar('chip', v + ' min'),
  });
  secao('Chip: de ação, marcável e grupo de escolha', criarChip({ texto: 'Chip de ação', icone: 'gear', aoClicar: () => registrar('chip', 'ação') }).el, chipMarcavel.el, grupo.el);

  // ---- interruptor ----
  const intOff = criarInterruptor({ rotulo: 'Desabilitado', ligado: true });
  intOff.habilitar(false);
  secao('Interruptor: com rótulo, sem rótulo e desabilitado',
    criarCartao({ filhos: [criarInterruptor({ rotulo: 'Manter a tela do PC ligada', aoMudar: (v) => registrar('interruptor', v ? 'ligado' : 'desligado') }).el] }).el,
    criarInterruptor({ aoMudar: (v) => registrar('interruptor', 'sem rótulo ' + (v ? 'ligado' : 'desligado')) }).el,
    intOff.el);

  // ---- camadas: aviso, folha, toast e dica ----
  function abrirAviso(perigo) {
    const a = criarAviso({
      container, titulo: perigo ? 'Desligar o PC?' : 'Aplicar?', texto: perigo ? 'O computador será desligado agora.' : 'Isto é só um exemplo.',
      rotuloAcao: perigo ? 'Desligar' : 'Aplicar', perigo,
      aoConfirmar: () => { registrar('aviso', 'confirmou'); a.destruir(); },
      aoCancelar: () => { registrar('aviso', 'cancelou'); a.destruir(); },
    });
    a.abrir();
  }
  let marcada = 'caixa';
  function abrirFolhaSolta() {
    const f = criarFolha({
      container, titulo: 'Escolher saída', opcoes: SAIDAS, valor: marcada,
      aoEscolher: (v) => { marcada = v; registrar('folha', String(v)); },
      aoFechar: () => f.destruir(),
    });
    f.abrir();
  }
  secao('Aviso, folha, toast e dica', linhaBotoes(
    criarBotao({ texto: 'Aviso', aoClicar: () => abrirAviso(false) }).el,
    criarBotao({ texto: 'Aviso perigo', variante: 'perigo', aoClicar: () => abrirAviso(true) }).el),
  linhaBotoes(
    criarBotao({ texto: 'Folha', aoClicar: abrirFolhaSolta }).el,
    criarBotao({ texto: 'Toast', aoClicar: () => { if (toast) toast.mostrar('Exemplo de toast'); registrar('toast', 'mostrado'); } }).el,
    criarBotao({ texto: 'Dica', aoClicar: () => { if (dica) dica.mostrar('Exemplo de dica'); registrar('dica', 'mostrada'); } }).el));

  // ---- touchpad ----
  const pad = criarTouchpad({
    aoMover: (dx, dy) => registrar('touchpad', 'mover ' + dx + ',' + dy),
    aoClicar: (b) => registrar('touchpad', 'clique ' + b),
    aoRolar: (dy) => registrar('touchpad', 'rolar ' + dy),
    aoSegurar: (b, ligado) => registrar('touchpad', (ligado ? 'segurar ' : 'soltar ') + b),
  });
  const palcoPad = document.createElement('div');
  palcoPad.className = 'galeria-pad';
  palcoPad.append(pad.el);
  secao('Touchpad em pé', palcoPad);

  // painel de vidro deitado: uma camada em tela cheia com um "PC" de mentira atrás
  let camadaDeitado = null;
  function abrirDeitado() {
    if (camadaDeitado) return;
    const camada = document.createElement('div');
    camada.className = 'galeria-deitado';
    camada.setAttribute('data-sem-gesto', '');
    const fundo = document.createElement('div');
    fundo.className = 'texto-sec';
    fundo.textContent = 'Aqui ficaria a tela do PC';
    const fechar = criarChip({ texto: 'Fechar', icone: 'x', aoClicar: () => { padD.destruir(); camada.remove(); camadaDeitado = null; } });
    fechar.el.classList.add('fechar-deitado');
    const padD = criarTouchpad({
      aoMover: (dx, dy) => registrar('painel deitado', 'mover ' + dx + ',' + dy),
      aoClicar: (b) => registrar('painel deitado', 'clique ' + b),
      aoRolar: (dy) => registrar('painel deitado', 'rolar ' + dy),
      aoSegurar: (b, ligado) => registrar('painel deitado', (ligado ? 'segurar ' : 'soltar ') + b),
      aoVisibilidade: (v) => registrar('painel deitado', v ? 'mostrado' : 'escondido'),
    });
    padD.definirDeitado(true);
    camada.append(fundo, fechar.el, padD.el);
    (container || document.body).append(camada);
    camadaDeitado = camada;
  }
  secao('Touchpad deitado (painel de vidro: arraste pela alça, esconda pela seta)',
    criarBotao({ texto: 'Abrir painel deitado', icone: 'land', aoClicar: abrirDeitado }).el);

  const rodape = document.createElement('div');
  rodape.className = 'rodape';
  rodape.textContent = 'Galeria dos componentes (sai na Fase 17)';
  el.append(rodape);
  return el;
}
