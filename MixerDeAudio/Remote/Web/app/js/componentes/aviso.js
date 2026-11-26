// Aviso de confirmação (guia 4.5, D8 da Fase 13): cartão centralizado sobre o fundo escurecido; o foco começa em Cancelar.
// Enquanto aberto o elemento tem a classe "aviso" (a pilha usa isso para bloquear o gesto de voltar).
import { criarBotao } from './botao.js';

// opções: { container, titulo, texto, rotuloAcao, perigo, aoConfirmar, aoCancelar }.
// Devolve { el, abrir(), fechar(), aberto(), destruir() }.
export function criarAviso({ container, titulo, texto, rotuloAcao, perigo = false, aoConfirmar, aoCancelar }) {
  const el = document.createElement('div');
  el.className = 'aviso';
  el.hidden = true;

  const cartao = document.createElement('div');
  cartao.className = 'cartao';
  cartao.setAttribute('role', 'dialog');
  cartao.setAttribute('aria-modal', 'true');
  const h2 = document.createElement('h2');
  h2.textContent = titulo;
  const p = document.createElement('p');
  p.textContent = texto;

  const cancelar = criarBotao({ texto: 'Cancelar', aoClicar: () => { api.fechar(); if (aoCancelar) aoCancelar(); } });
  const acao = criarBotao({ texto: rotuloAcao, variante: perigo ? 'perigo' : 'principal', aoClicar: () => { api.fechar(); if (aoConfirmar) aoConfirmar(); } });
  const linha = document.createElement('div');
  linha.className = 'linha';
  linha.append(cancelar.el, acao.el);
  cartao.append(h2, p, linha);
  el.append(cartao);
  (container || document.body).append(el);

  el.addEventListener('click', (e) => { if (e.target === el) cancelar.el.click(); });
  el.addEventListener('keydown', (e) => { if (e.key === 'Escape') cancelar.el.click(); });

  const api = {
    el,
    abrir() { el.hidden = false; cancelar.el.focus(); },
    fechar() { el.hidden = true; },
    aberto() { return !el.hidden; },
    destruir() { el.remove(); },
  };
  return api;
}
