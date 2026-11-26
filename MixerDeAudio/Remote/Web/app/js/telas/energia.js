// Tela Energia: provisória (Fase 15.0). A parte 15b troca o conteúdo; o contrato do ctx está em LEIAME.md.
import { criarTelaProvisoria } from './provisoria.js';

// ctx: ver telas/LEIAME.md. Devolve { el, aoTopo?(sim), destruir?() }.
export function criarTela(ctx) {
  return { el: criarTelaProvisoria({ titulo: 'Energia', icone: 'power', aoVoltar: () => ctx.voltar() }) };
}
