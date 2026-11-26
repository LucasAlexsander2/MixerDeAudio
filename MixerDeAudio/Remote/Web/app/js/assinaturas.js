// Assinaturas do PC por grupo (D4): contagem por grupo, para duas telas dividirem o mesmo grupo (ex.: `info` no Início e no Estado do PC).
// Manda { t:'assinar', g, on:true } na primeira assinatura e { t:'assinar', g, on:false } na última; depois de reconectar manda de novo
// os grupos ligados; com a página escondida (visibilitychange) desliga todos e, ao voltar a aparecer, religa.
import { enviar, aoAbrir } from './conexao.js';

const contagem = new Map(); // grupo -> quantidade de assinaturas
let escondida = document.visibilityState === 'hidden';

function mandar(g, on) { enviar({ t: 'assinar', g, on }); }

// assinar(grupo) -> soltar(). Chamar soltar() mais de uma vez não tem efeito além da primeira.
export function assinar(grupo) {
  const n = contagem.get(grupo) || 0;
  contagem.set(grupo, n + 1);
  if (n === 0 && !escondida) mandar(grupo, true);
  let solto = false;
  return function soltar() {
    if (solto) return;
    solto = true;
    const k = (contagem.get(grupo) || 1) - 1;
    if (k <= 0) {
      contagem.delete(grupo);
      if (!escondida) mandar(grupo, false);
    } else contagem.set(grupo, k);
  };
}

// grupos hoje ligados (só para testes e diagnóstico)
export function gruposLigados() { return [...contagem.keys()]; }

// reconexão: o PC não guarda as assinaturas da conexão anterior
aoAbrir(() => { if (!escondida) for (const g of contagem.keys()) mandar(g, true); });

document.addEventListener('visibilitychange', () => {
  const agora = document.visibilityState === 'hidden';
  if (agora === escondida) return;
  escondida = agora;
  for (const g of contagem.keys()) mandar(g, !agora);
});
