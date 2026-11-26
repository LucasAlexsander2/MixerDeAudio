// Teclado do PC, compartilhado pelo Controle e pela Tela do PC (Fase 15c, D15). Só monta os pedaços; quem usa decide onde ficam.
// Mensagens: { t:'tecla', k } e, com modificadores presos, { t:'tecla', k, mods:['ctrl','alt','shift','win'] } (só teclas da lista
// fixa do PC); { t:'texto', s } para texto livre. O modificador solta sozinho depois da próxima tecla.
import { criarChip } from '../componentes/chip.js';
import { criarBotao } from '../componentes/botao.js';
import { criarAviso } from '../componentes/aviso.js';

const MODS = [['ctrl', 'Ctrl'], ['alt', 'Alt'], ['shift', 'Shift'], ['win', 'Win']];
const LETRAS = ['a', 'c', 'd', 'f', 's', 't', 'v', 'w', 'x', 'z']; // as letras que o PC aceita junto de modificador

function div(classe) {
  const d = document.createElement('div');
  d.className = classe;
  return d;
}

// opções: { enviar, camadas }. Devolve { mods (elemento), teclas(lista), atalhos(lista), letras(), texto(opcoes), limpar(), presos() }.
export function criarTeclado({ enviar, camadas }) {
  const ativos = new Set();
  const chips = new Map();

  const mods = div('chips tec-mods');
  mods.setAttribute('role', 'group');
  mods.setAttribute('aria-label', 'Modificadores presos até a próxima tecla');
  for (const [id, nome] of MODS) {
    const c = criarChip({ texto: nome, marcado: false, grande: true, aoClicar: () => alternar(id) });
    chips.set(id, c);
    mods.append(c.el);
  }
  function alternar(id) {
    if (ativos.has(id)) ativos.delete(id); else ativos.add(id);
    chips.get(id).marcar(ativos.has(id));
  }
  function limpar() {
    ativos.clear();
    chips.forEach((c) => c.marcar(false));
  }
  function presos() {
    return MODS.map((m) => m[0]).filter((m) => ativos.has(m));
  }

  // manda a tecla (com os modificadores presos, se houver) e solta os modificadores
  function tecla(k) {
    const lista = presos();
    if (lista.length && !k.includes('+')) enviar({ t: 'tecla', k, mods: lista });
    else enviar({ t: 'tecla', k });
    limpar();
  }

  let aviso = null;
  function pedirConfirmacao(k) {
    if (!aviso) {
      aviso = criarAviso({
        container: camadas,
        titulo: 'Fechar a janela da frente?',
        texto: 'Envia Alt+F4 ao PC.',
        rotuloAcao: 'Fechar',
        perigo: true,
        aoConfirmar: () => tecla('alt+f4'),
      });
    }
    aviso.abrir();
  }
  function atalho(k) {
    if (k === 'alt+f4') pedirConfirmacao(k);
    else tecla(k);
  }

  // lista: [[texto, tecla, rotulo?, colunas?]]
  function botoes(lista, classe) {
    const g = div(classe || 'tec-grade');
    for (const [texto, k, rotulo, colunas] of lista) {
      const b = criarBotao({ texto, aoClicar: () => atalho(k) });
      if (rotulo) b.el.setAttribute('aria-label', rotulo);
      if (colunas) b.el.style.gridColumn = 'span ' + colunas;
      g.append(b.el);
    }
    return g;
  }

  // chips de ação que rolam para o lado (atalhos rápidos, F1 a F12)
  function faixaChips(lista) {
    const f = div('tec-faixa');
    f.setAttribute('data-sem-gesto', '');
    for (const [texto, k, rotulo] of lista) {
      const c = criarChip({ texto, grande: true, aoClicar: () => atalho(k) });
      if (rotulo) c.el.setAttribute('aria-label', rotulo);
      f.append(c.el);
    }
    return f;
  }

  // letras que só valem com modificador preso; sem modificador, a letra é digitada como texto
  function letras() {
    const g = div('tec-grade tec-letras');
    for (const l of LETRAS) {
      const b = criarBotao({
        texto: l.toUpperCase(),
        aoClicar: () => { if (presos().length) tecla(l); else enviar({ t: 'texto', s: l }); },
      });
      b.el.setAttribute('aria-label', 'Letra ' + l.toUpperCase());
      g.append(b.el);
    }
    return g;
  }

  // campo de texto livre; Enter envia. opcoes: { multilinha, placeholder, rotuloBotao }. Devolve { el, campo, enviar() }.
  function texto({ multilinha = false, placeholder = 'Digite o texto para enviar ao PC', rotuloBotao = 'Enviar' } = {}) {
    const linha = div('tec-texto' + (multilinha ? ' multi' : ''));
    const campo = document.createElement(multilinha ? 'textarea' : 'input');
    if (multilinha) campo.rows = 4; else campo.type = 'text';
    campo.maxLength = 1000;
    campo.placeholder = placeholder;
    campo.autocomplete = 'off';
    campo.autocapitalize = 'off';
    campo.setAttribute('aria-label', 'Texto para digitar no PC');
    function enviarTexto() {
      const v = campo.value;
      if (!v) return;
      enviar({ t: 'texto', s: v });
      campo.value = '';
    }
    const go = criarBotao({ texto: rotuloBotao, variante: 'principal', aoClicar: enviarTexto });
    if (!multilinha) campo.addEventListener('keydown', (e) => { if (e.key === 'Enter') { e.preventDefault(); enviarTexto(); } });
    linha.append(campo, go.el);
    return { el: linha, campo, enviar: enviarTexto };
  }

  return {
    mods,
    teclas: (lista, classe) => botoes(lista, classe),
    atalhos: faixaChips,
    letras,
    texto,
    limpar,
    presos,
    destruir() { limpar(); if (aviso) aviso.destruir(); aviso = null; },
  };
}

// Listas de teclas (nomes da lista fixa do PC, InputRules).
export const TECLAS_BASICAS = [
  ['Esc', 'esc'], ['Tab', 'tab'], ['Enter', 'enter'], ['⌫', 'backspace', 'Apagar'],
  ['Del', 'delete', 'Delete'], ['Espaço', 'space', 'Espaço', 2], ['↑', 'up', 'Seta para cima'],
  ['←', 'left', 'Seta para a esquerda'], ['↓', 'down', 'Seta para baixo'], ['→', 'right', 'Seta para a direita'],
  ['Home', 'home'], ['End', 'end'], ['PgUp', 'pgup'], ['PgDn', 'pgdn'],
];
export const TECLAS_CURTAS = [
  ['Esc', 'esc'], ['Tab', 'tab'], ['Enter', 'enter'], ['⌫', 'backspace', 'Apagar'],
  ['←', 'left', 'Seta para a esquerda'], ['↑', 'up', 'Seta para cima'], ['↓', 'down', 'Seta para baixo'], ['→', 'right', 'Seta para a direita'],
];
export const ATALHOS_RAPIDOS = [
  ['Copiar', 'ctrl+c'], ['Colar', 'ctrl+v'], ['Recortar', 'ctrl+x'], ['Desfazer', 'ctrl+z'], ['Selecionar tudo', 'ctrl+a'],
  ['Salvar', 'ctrl+s'], ['Localizar', 'ctrl+f'], ['Fechar aba', 'ctrl+w'], ['Nova aba', 'ctrl+t'],
  ['Alt+Tab', 'alt+tab'], ['Win+D', 'win+d'], ['Tarefas', 'ctrl+shift+esc'], ['Win', 'win'], ['Alt+F4', 'alt+f4'],
];
export const TECLAS_F = Array.from({ length: 12 }, (_, i) => ['F' + (i + 1), 'f' + (i + 1)]);
