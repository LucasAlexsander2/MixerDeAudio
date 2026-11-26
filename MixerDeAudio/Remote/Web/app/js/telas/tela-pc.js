// Tela Tela do PC: provisória (Fase 15.0). A parte 15c troca o conteúdo; o contrato do ctx está em LEIAME.md.
import { criarTelaProvisoria } from './provisoria.js';

// ctx: ver telas/LEIAME.md. Devolve { el, aoTopo?(sim), destruir?() }.
export function criarTela(ctx) {
  return { el: criarTelaProvisoria({ titulo: 'Tela do PC', icone: 'monitor', aoVoltar: () => ctx.voltar() }) };
}
