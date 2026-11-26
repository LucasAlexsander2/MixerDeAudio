// Início provisório (D19): "Todas as funções" em blocos (grade de 3) e Desconectar com aviso de confirmação.
// Mesma regra de grupos da página antiga (antigo/10-menu.js, renderPc): cada área só aparece se o grupo dela estiver ligado no PC.
import { criarBloco } from '../componentes/bloco.js';
import { criarBotao } from '../componentes/botao.js';

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

// opções: { aoAbrirArea(nome), aoDesconectar }. Devolve { el, atualizar(estado) }.
export function criarTelaInicio({ aoAbrirArea, aoDesconectar }) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'inicio';

  const titulo = document.createElement('h1');
  titulo.className = 'tela-titulo';
  titulo.textContent = 'Controladora PC';
  const rotulo = document.createElement('div');
  rotulo.className = 'rotulo';
  rotulo.textContent = 'Todas as funções';
  const grade = document.createElement('div');
  grade.className = 'grade3';
  const sair = criarBotao({ texto: 'Desconectar', variante: 'perigo', largo: true, aoClicar: () => aoDesconectar() });
  const rodape = document.createElement('div');
  rodape.className = 'rodape';
  rodape.textContent = 'Prévia da Controladora PC';
  el.append(titulo, rotulo, grade, sair.el, rodape);

  let assinatura = '';
  return {
    el,
    atualizar(estado) {
      const lista = areasVisiveis(estado && estado.ferr);
      const sig = lista.map((a) => a.nome).join(',');
      if (sig === assinatura) return;
      assinatura = sig;
      grade.replaceChildren(...lista.map((a) => criarBloco({ nome: a.rotulo, icone: a.icone, aoClicar: () => aoAbrirArea(a.nome) }).el));
    },
  };
}
