// Tela Enviar: provisória (Fase 15.0). A parte 15d troca o conteúdo; o contrato do ctx está em LEIAME.md.
import { criarTelaProvisoria } from './provisoria.js';

// ctx: ver telas/LEIAME.md. Devolve { el, aoTopo?(sim), destruir?() }.
export function criarTela(ctx) {
  return { el: criarTelaProvisoria({ titulo: 'Enviar', icone: 'send', aoVoltar: () => ctx.voltar() }) };
}
