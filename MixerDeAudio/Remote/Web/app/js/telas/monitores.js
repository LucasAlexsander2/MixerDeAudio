// Tela Monitores (Fase 15b, D14): projeção, apagar/acordar as telas, brilho por tela e manter a tela ligada.
// Mensagens (antigo/09-pc.js, 11-brilho.js, 16-controle.js): { t:'projecao', modo }, { t:'atualizar' }, { t:'telas', acao:'apagar'|'acordar' },
// { t:'brilho', tela, valor } e { t:'acordado', on }. O estado vem em estado.pc { projecao:{modo,telas}, brilho:[{tela,valor|null}], acordado }.
import { criarCabecalho } from '../componentes/cabecalho.js';
import { criarBloco } from '../componentes/bloco.js';
import { criarGrade } from '../componentes/faixa.js';
import { criarBotao } from '../componentes/botao.js';
import { criarBarra } from '../componentes/barra.js';
import { criarCartao } from '../componentes/cartao.js';
import { criarInterruptor } from '../componentes/interruptor.js';

export const MODOS = [
  { valor: 'principal', texto: 'Só a tela do PC', icone: 'monitor' },
  { valor: 'duplicar', texto: 'Duplicar', icone: 'monitors' },
  { valor: 'estender', texto: 'Estender', icone: 'ext' },
  { valor: 'segunda', texto: 'Só a segunda tela', icone: 'monitor' },
];

function rotulo(texto) {
  const d = document.createElement('div');
  d.className = 'rotulo';
  d.textContent = texto;
  return d;
}

function nota(texto) {
  const p = document.createElement('p');
  p.className = 'texto-sec';
  p.textContent = texto;
  return p;
}

export function criarTela(ctx) {
  const el = document.createElement('section');
  el.className = 'tela';
  el.dataset.tela = 'monitores';
  const cab = criarCabecalho({ titulo: 'Monitores', aoVoltar: () => ctx.voltar() });

  // Projeção
  let timerAtualizar = 0;
  const blocos = MODOS.map((m) => criarBloco({
    nome: m.texto, icone: m.icone,
    aoClicar: () => {
      ctx.enviar({ t: 'projecao', modo: m.valor });
      clearTimeout(timerAtualizar);
      timerAtualizar = setTimeout(() => ctx.enviar({ t: 'atualizar' }), 1500);
    },
  }));
  const grade = criarGrade({ colunas: 2, alta: true, blocos: blocos.map((b) => b.el) });
  const infoProj = nota('');
  infoProj.classList.add('monitores-info');

  // Telas
  const apagar = criarBotao({ texto: 'Apagar as telas', icone: 'moon', aoClicar: () => ctx.enviar({ t: 'telas', acao: 'apagar' }) });
  const acordar = criarBotao({ texto: 'Acordar', icone: 'sun', aoClicar: () => ctx.enviar({ t: 'telas', acao: 'acordar' }) });
  const linhaTelas = document.createElement('div');
  linhaTelas.className = 'linha-botoes';
  linhaTelas.append(apagar.el, acordar.el);

  // Brilho (uma barra por tela; 1 envio a cada 150 ms e o valor final ao soltar, como na página antiga)
  const secBrilho = document.createElement('div');
  const caixaBrilho = document.createElement('div');
  caixaBrilho.className = 'monitores-brilho';
  secBrilho.append(rotulo('Brilho'), caixaBrilho, nota('Brilho 0 não apaga a tela; para isso use Apagar as telas.'));
  secBrilho.hidden = true;
  let linhas = [];
  let assinatura = '';
  function desenharBrilho(lista) {
    secBrilho.hidden = lista.length === 0;
    const sig = lista.map((b) => (b.valor == null ? 'x' : 'o')).join('');
    if (sig !== assinatura || linhas.length !== lista.length) {
      assinatura = sig;
      linhas = [];
      caixaBrilho.replaceChildren();
      lista.forEach((b) => {
        const nome = lista.length > 1 ? 'Tela ' + (b.tela + 1) : 'Tela';
        if (b.valor == null) {
          const c = criarCartao({ filhos: [Object.assign(document.createElement('b'), { textContent: nome }), nota('Esta tela não aceita ajuste de brilho (ative o DDC/CI no menu do monitor).')] });
          c.el.classList.add('monitores-linha');
          caixaBrilho.append(c.el);
          linhas.push(null);
          return;
        }
        const barra = criarBarra({
          rotulo: 'Brilho da ' + nome, intervaloMs: 150,
          aoMudar: (v) => ctx.enviar({ t: 'brilho', tela: b.tela, valor: v }),
        });
        const c = criarCartao({ filhos: [Object.assign(document.createElement('b'), { textContent: nome }), barra.el] });
        c.el.classList.add('monitores-linha');
        caixaBrilho.append(c.el);
        linhas.push(barra);
      });
    }
    lista.forEach((b, i) => { if (linhas[i] && b.valor != null) linhas[i].definir(b.valor); });
  }

  // Manter a tela ligada (só se o PC mandar `acordado`, isto é, com o grupo de entrada ligado)
  const secAcordado = document.createElement('div');
  const acordado = criarInterruptor({ rotulo: 'Manter a tela ligada', aoMudar: (v) => ctx.enviar({ t: 'acordado', on: v }) });
  secAcordado.append(rotulo('Tela ligada'), acordado.el, nota('Desliga sozinho em 4 horas.'));
  secAcordado.hidden = true;

  el.append(cab.el, rotulo('Projeção'), grade.el, infoProj, rotulo('Telas'), linhaTelas, secBrilho, secAcordado);

  function aplicar(estado) {
    const f = (estado && estado.ferr) || {};
    const pc = (estado && estado.pc) || {};
    const pj = pc.projecao || {};
    const ok = pj.telas >= 2;
    MODOS.forEach((m, i) => { blocos[i].habilitar(ok); blocos[i].definirLigado(m.valor === pj.modo); });
    infoProj.textContent = ok ? pj.telas + ' telas ligadas ao PC.' : 'Só há uma tela ligada ao PC.';
    if (f.monitores !== false) desenharBrilho(pc.brilho || []);
    const tem = typeof pc.acordado === 'boolean';
    secAcordado.hidden = !tem;
    if (tem) acordado.definir(pc.acordado);
  }
  const soltarEstado = ctx.aoMudar(aplicar);
  aplicar(ctx.obterEstado());

  return {
    el,
    destruir() {
      soltarEstado();
      clearTimeout(timerAtualizar);
    },
  };
}
