// Cartão (guia 4.5): --surface, canto 16, espaço interno 12. Só um contêiner.

// opções: { filhos: [HTMLElement], classe }. Devolve { el, definir([HTMLElement]), adicionar(HTMLElement), limpar() }.
export function criarCartao({ filhos = [], classe = '' } = {}) {
  const el = document.createElement('div');
  el.className = 'cartao-base' + (classe ? ' ' + classe : '');
  el.append(...filhos);
  return {
    el,
    definir(lista) { el.replaceChildren(...lista); },
    adicionar(f) { el.append(f); },
    limpar() { el.replaceChildren(); },
  };
}
