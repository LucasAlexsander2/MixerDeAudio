// Tela Atalhos (Fase 15d, D17): blocos com os atalhos da pasta escolhida no PC (`pc.atalhos` do estado).
// Mensagens da página antiga (página antiga, 15-janelas.js e 10-menu.js): `atalho` { nome } e `atualizar` ao abrir a área.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarBloco } from '../componentes/bloco.js';
import { criarGrade } from '../componentes/faixa.js';

// Ícone de cada atalho pelo nome (o PC só manda o nome); sem correspondência, o ícone genérico de atalho.
const ICONES = [
  [/steam|jogo|game|epic|xbox/i, 'gamepad'],
  [/obs|grav|rec/i, 'rec'],
  [/calc/i, 'calc'],
  [/bloco|nota|note|word|texto/i, 'note'],
  [/download|baix/i, 'download'],
  [/spotify|music|m[uú]sica|player|vlc/i, 'music'],
  [/config|setting|painel/i, 'gear'],
  [/tarefa|task/i, 'tasks'],
];
export function iconeDoAtalho(nome) {
  for (const [padrao, ic] of ICONES) if (padrao.test(nome)) return ic;
  return 'shortcut';
}

export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'atalhos';

  const cab = criarCabecalho({ titulo: 'Atalhos', aoVoltar: () => ctx.voltar() });
  const sub = document.createElement('p');
  sub.className = 'texto-sec atalhos-sub';
  sub.textContent = 'Atalhos da pasta escolhida no PC';
  const grade = criarGrade({ colunas: 3, alta: true });
  const vazio = document.createElement('p');
  vazio.className = 'texto-sec atalhos-vazio';
  vazio.textContent = 'Nenhum atalho na pasta escolhida';
  el.append(cab.el, sub, grade.el, vazio);

  let assinatura = null;
  function desenhar(estado) {
    const lista = estado && estado.pc && Array.isArray(estado.pc.atalhos) ? estado.pc.atalhos : [];
    const sig = JSON.stringify(lista);
    if (sig === assinatura) return;
    assinatura = sig;
    vazio.hidden = lista.length > 0;
    grade.definir(lista.map((nome) => criarBloco({
      nome,
      icone: iconeDoAtalho(nome),
      aoClicar: () => { ctx.enviar({ t: 'atalho', nome }); ctx.toast('Abrindo ' + nome + '…'); },
    }).el));
  }

  desenhar(ctx.obterEstado());
  const cancelar = ctx.aoMudar(desenhar);

  return {
    el,
    // a página antiga pedia `atualizar` ao abrir a aba; o PC responde com o estado e a lista de atalhos nova
    aoTopo(sim) { if (sim) ctx.enviar({ t: 'atualizar' }); },
    destruir() { cancelar(); },
  };
}
