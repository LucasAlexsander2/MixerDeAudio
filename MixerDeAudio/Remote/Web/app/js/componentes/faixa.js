// Faixa de blocos (guia 4.5): rolagem para o lado em uma ou duas linhas; e grade de 2 ou 3 colunas.
// Os blocos são os elementos de criarBloco(...).el (ou qualquer botão de 100 x 76 px).

// criarFaixa({ linhas: 1 | 2, larga, blocos: [HTMLElement] }). Marcada com data-sem-gesto (a rolagem para o lado não volta a tela).
// `larga`: cada bloco tem a largura de um bloco da grade de 2 (duas colunas por vez na tela), como nos favoritos do Início.
// Devolve { el, definir([HTMLElement]), adicionar(HTMLElement), limpar() }.
export function criarFaixa({ linhas = 1, larga = false, blocos = [] } = {}) {
  const el = document.createElement('div');
  el.className = 'faixa' + (linhas === 2 ? ' duas' : '') + (larga ? ' larga' : '');
  el.setAttribute('data-sem-gesto', '');
  el.append(...blocos);
  return {
    el,
    definir(lista) { el.replaceChildren(...lista); },
    adicionar(b) { el.append(b); },
    limpar() { el.replaceChildren(); },
  };
}

// criarGrade({ colunas: 2 | 3, alta, blocos }). `alta` deixa os blocos com no mínimo 88 px (nomes longos, como atalhos).
// Devolve { el, definir([HTMLElement]), adicionar(HTMLElement), limpar() }.
export function criarGrade({ colunas = 3, alta = false, blocos = [] } = {}) {
  const el = document.createElement('div');
  el.className = (colunas === 2 ? 'grade2' : 'grade3') + (alta ? ' alta' : '');
  el.append(...blocos);
  return {
    el,
    definir(lista) { el.replaceChildren(...lista); },
    adicionar(b) { el.append(b); },
    limpar() { el.replaceChildren(); },
  };
}
