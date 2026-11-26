// Cartão de app (guia 4.5): avatar de 48 px (tocar = mudo, com risco vermelho), nome 15/700, música em --muted,
// botãozinho de saída, barra de volume com número e a linha Anterior / Pausa / Próxima. Mudo deixa o cartão a 55%.
import { criarBarra } from './barra.js';
import { icone } from '../icones.js';
import { vibrar } from '../util.js';

// opções: { nome, musica (texto ou null/'' = sem música: a linha de mídia some), saida: { nome, icone },
//           volume = 0, mudo = false, tocando = false, intervaloMs, esperaMs,
//           aoVolume(valor, final), aoMudo(mudo), aoSaida(), aoAnterior(), aoPausa(tocando), aoProxima() }.
// Eventos (só quando o usuário toca): aoMudo e aoPausa trazem o NOVO estado e o cartão já o mostra; se o PC discordar,
// chame definirMudo / definirTocando com o estado real. aoVolume segue a regra da barra (50 ms, final ao soltar, espera de 500 ms).
// Devolve { el, barra, definirNome(texto), definirMusica(texto|null), definirSaida(nome, icone), definirVolume(valor),
//           definirMudo(bool), definirTocando(bool) }.
export function criarCartaoApp({ nome, musica = null, saida, volume = 0, mudo = false, tocando = false, intervaloMs, esperaMs,
  aoVolume, aoMudo, aoSaida, aoAnterior, aoPausa, aoProxima } = {}) {
  const el = document.createElement('article');
  el.className = 'cartao-base cartao-app';

  // linha 1: avatar e textos
  const avatar = document.createElement('button');
  avatar.type = 'button';
  avatar.className = 'avatar';
  avatar.addEventListener('click', () => { vibrar(); api.definirMudo(!estadoMudo); if (aoMudo) aoMudo(estadoMudo); });

  const textos = document.createElement('div');
  textos.className = 'textos';
  const titulo = document.createElement('b');
  const cancao = document.createElement('span');
  cancao.className = 'musica';
  const chipSaida = document.createElement('button');
  chipSaida.type = 'button';
  chipSaida.className = 'chip-saida';
  const chipIc = document.createElement('i');
  chipIc.className = 'ic';
  const chipTxt = document.createElement('span');
  chipSaida.append(chipIc, chipTxt);
  chipSaida.addEventListener('click', () => { vibrar(); if (aoSaida) aoSaida(); });
  textos.append(titulo, cancao, chipSaida);

  const l1 = document.createElement('div');
  l1.className = 'l1';
  l1.append(avatar, textos);

  // linha 2: barra de volume
  const barra = criarBarra({ valor: volume, rotulo: 'Volume', intervaloMs, esperaMs, aoMudar: aoVolume });

  // linha 3: mídia
  const midia = document.createElement('div');
  midia.className = 'midia';
  function botaoMidia(rotuloAria, nomeIcone, acao) {
    const b = document.createElement('button');
    b.type = 'button';
    b.className = 'botao-midia';
    b.setAttribute('aria-label', rotuloAria);
    const ic = document.createElement('i');
    ic.className = 'ic';
    ic.innerHTML = icone(nomeIcone);
    b.append(ic);
    b.addEventListener('click', () => { vibrar(); acao(); });
    return { b, ic };
  }
  const ant = botaoMidia('Anterior', 'prev', () => { if (aoAnterior) aoAnterior(); });
  const pp = botaoMidia('Tocar ou pausar', 'pause', () => { api.definirTocando(!estadoTocando); if (aoPausa) aoPausa(estadoTocando); });
  const prox = botaoMidia('Próxima', 'next', () => { if (aoProxima) aoProxima(); });
  midia.append(ant.b, pp.b, prox.b);

  el.append(l1, barra.el, midia);

  let estadoMudo = false;
  let estadoTocando = false;
  const api = {
    el,
    barra,
    definirNome(t) {
      titulo.textContent = t;
      avatar.textContent = (t || '?').trim().charAt(0).toUpperCase();
      avatar.setAttribute('aria-label', 'Mudo ' + t);
    },
    definirMusica(t) {
      cancao.textContent = t || 'Sem música';
      midia.hidden = !t;
    },
    definirSaida(n, nomeIcone) {
      chipTxt.textContent = n;
      chipIc.innerHTML = icone(nomeIcone || 'speaker');
      chipSaida.setAttribute('aria-label', 'Saída de áudio: ' + n);
    },
    definirVolume(v) { barra.definir(v); },
    definirMudo(v) {
      estadoMudo = !!v;
      el.classList.toggle('mudo', estadoMudo);
      avatar.classList.toggle('mudo', estadoMudo);
      avatar.setAttribute('aria-pressed', String(estadoMudo));
    },
    definirTocando(v) {
      estadoTocando = !!v;
      pp.ic.innerHTML = icone(estadoTocando ? 'pause' : 'play');
    },
  };
  api.definirNome(nome || '');
  api.definirMusica(musica);
  api.definirSaida((saida && saida.nome) || '', saida && saida.icone);
  api.definirMudo(mudo);
  api.definirTocando(tocando);
  return api;
}
