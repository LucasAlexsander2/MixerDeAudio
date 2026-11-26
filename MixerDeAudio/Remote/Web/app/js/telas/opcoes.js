// Tela Opções: mão usada (A5) e os favoritos do Início (D11, P1). Os controles passam para o lado da mão escolhida.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarGrupoChips } from '../componentes/chip.js';
import { criarInterruptor } from '../componentes/interruptor.js';
import { criarCartao } from '../componentes/cartao.js';
import { criarBotao } from '../componentes/botao.js';
import { lerMao, definirMao, aoMudarMao } from '../preferencias.js';
import { itensCatalogo, favoritosEfetivos, CHAVE_FAVORITOS } from './inicio.js';

export const OPCOES_MAO = [
  { valor: 'direita', texto: 'Direita' },
  { valor: 'esquerda', texto: 'Esquerda' },
];

// Grupo "Mão usada" (rótulo + chips), reaproveitado pela Galeria. Devolve { el }.
export function criarGrupoMao() {
  const el = document.createElement('div');
  const rotulo = document.createElement('div');
  rotulo.className = 'rotulo';
  rotulo.textContent = 'Mão usada';
  const grupo = criarGrupoChips({ opcoes: OPCOES_MAO, valor: lerMao(), aoEscolher: (v) => definirMao(v) });
  aoMudarMao((m) => grupo.marcar(m));
  el.append(rotulo, grupo.el);
  return { el };
}

// opções: { ctx, aoVoltar }. Devolve { el, destruir() }.
export function criarTelaOpcoes({ ctx = {}, aoVoltar }) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'opcoes';
  const cab = criarCabecalho({ titulo: 'Opções', aoVoltar });
  const texto = document.createElement('p');
  texto.className = 'texto-sec';
  texto.textContent = 'Os controles passam para o lado da mão escolhida.';

  // favoritos: um interruptor por item do catálogo; a escolha fica neste celular (inicio.favoritos)
  const prefs = ctx.preferencias || {};
  const rotuloFav = document.createElement('div');
  rotuloFav.className = 'rotulo';
  rotuloFav.textContent = 'Favoritos do Início';
  const textoFav = document.createElement('p');
  textoFav.className = 'texto-sec';
  textoFav.textContent = 'Escolha o que aparece na faixa de favoritos. A escolha fica neste celular.';
  const lista = criarCartao({ classe: 'opcoes-favoritos' });
  const restaurar = criarBotao({ texto: 'Restaurar favoritos padrão', aoClicar: () => {
    if (prefs.gravar) prefs.gravar(CHAVE_FAVORITOS, null);
    desenhar(true);
  } });
  restaurar.el.classList.add('opcoes-restaurar');

  let assinatura = '';
  const estadoAtual = () => (ctx.obterEstado ? ctx.obterEstado() : null);
  const guardado = () => (prefs.ler ? prefs.ler(CHAVE_FAVORITOS, null) : null);

  function alternar(id, ligado, itens) {
    const ids = new Set(favoritosEfetivos(itens, guardado()));
    if (ligado) ids.add(id); else ids.delete(id);
    if (prefs.gravar) prefs.gravar(CHAVE_FAVORITOS, [...ids]);
  }

  function desenhar(forcar) {
    const itens = itensCatalogo(estadoAtual());
    // o Play/Pausa pode ser escolhido mesmo sem nada tocando; o resto, só o que o PC oferece
    const mostrados = itens.filter((i) => i.disponivel || i.tipo === 'play');
    const sig = mostrados.map((i) => i.id).join(',');
    if (!forcar && sig === assinatura) return;
    assinatura = sig;
    const ids = new Set(favoritosEfetivos(itens, guardado()));
    lista.definir(mostrados.map((i) => {
      const sw = criarInterruptor({ rotulo: i.rotulo, ligado: ids.has(i.id), aoMudar: (v) => alternar(i.id, v, itensCatalogo(estadoAtual())) });
      return sw.el;
    }));
  }

  desenhar(true);
  const cancelar = ctx.aoMudar ? ctx.aoMudar(() => desenhar(false)) : null;
  el.append(cab.el, criarGrupoMao().el, texto, rotuloFav, textoFav, lista.el, restaurar.el);
  return { el, destruir() { if (cancelar) cancelar(); } };
}
