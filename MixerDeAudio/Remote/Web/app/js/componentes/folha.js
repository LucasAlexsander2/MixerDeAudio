// Folha de opções (guia 4.5): igual ao aviso, mas presa embaixo, com a lista de opções (marcada = --accent).
// Enquanto aberta o elemento tem as classes "aviso folha" (a pilha bloqueia o gesto de voltar, como no aviso).
import { icone } from '../icones.js';
import { vibrar } from '../util.js';

// opções: { container (use o `camadas` da página), titulo, opcoes: [{ valor, texto, icone }], valor (marcada),
//           aoEscolher(valor, indice), aoFechar() }.
// Eventos: aoEscolher quando o usuário toca numa opção (a folha fecha); aoFechar depois de qualquer fechamento
// (escolha, toque fora, Esc, fechar()).
// Devolve { el, abrir(), fechar(), aberto(), marcar(valor), definirOpcoes(opcoes, valor), destruir() }.
// Crie quando precisar e chame destruir() em aoFechar se não for reutilizar (remove o elemento).
export function criarFolha({ container, titulo, opcoes = [], valor, aoEscolher, aoFechar } = {}) {
  const el = document.createElement('div');
  el.className = 'aviso folha';
  el.hidden = true;

  const cartao = document.createElement('div');
  cartao.className = 'cartao';
  cartao.setAttribute('role', 'dialog');
  cartao.setAttribute('aria-modal', 'true');
  const h2 = document.createElement('h2');
  h2.textContent = titulo || '';
  const lista = document.createElement('div');
  lista.className = 'opcoes';
  cartao.append(h2, lista);
  el.append(cartao);
  (container || document.body).append(el);

  let atual = valor;
  let itens = [];

  function marcar(v) {
    atual = v;
    itens.forEach((b) => {
      const on = b.dataset.valor === String(v);
      b.classList.toggle('marcada', on);
      b.setAttribute('aria-pressed', String(on));
    });
  }
  function definirOpcoes(novas, v) {
    opcoes = novas;
    itens = novas.map((o, i) => {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'opcao';
      b.dataset.valor = String(o.valor);
      if (o.icone) {
        const ic = document.createElement('i');
        ic.className = 'ic';
        ic.innerHTML = icone(o.icone);
        b.append(ic);
      }
      const t = document.createElement('span');
      t.textContent = o.texto;
      b.append(t);
      b.addEventListener('click', () => {
        vibrar();
        marcar(o.valor);
        if (aoEscolher) aoEscolher(o.valor, i);
        api.fechar();
      });
      return b;
    });
    lista.replaceChildren(...itens);
    marcar(v === undefined ? atual : v);
  }

  el.addEventListener('click', (e) => { if (e.target === el) api.fechar(); });
  el.addEventListener('keydown', (e) => { if (e.key === 'Escape') api.fechar(); });

  const api = {
    el,
    abrir() {
      el.hidden = false;
      const alvo = itens.find((b) => b.classList.contains('marcada')) || itens[0];
      if (alvo) alvo.focus();
    },
    fechar() {
      if (el.hidden) return;
      el.hidden = true;
      if (aoFechar) aoFechar();
    },
    aberto() { return !el.hidden; },
    marcar,
    definirOpcoes,
    destruir() { el.remove(); },
  };
  definirOpcoes(opcoes, valor);
  return api;
}
