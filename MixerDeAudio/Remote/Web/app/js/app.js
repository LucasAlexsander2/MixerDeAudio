// Entrada do novo front (/novo): liga a pilha de telas, a conexão com o PC e as telas provisórias (D10 a D14, D19).
import { lerMao, aoMudarMao, definirMao, ler, gravar } from './preferencias.js';
import { criarPilha } from './pilha.js';
import { iniciarConexao, enviar, parear, reconectarAgora, aoMensagem } from './conexao.js';
import { assinar } from './assinaturas.js';
import { aoMudar, obterEstado, limparEstado } from './estado.js';
import { criarToast } from './componentes/toast.js';
import { criarDica } from './componentes/dica.js';
import { criarAviso } from './componentes/aviso.js';
import { criarTelaAcesso } from './telas/acesso.js';
import { criarTelaInicio, AREAS } from './telas/inicio.js';
import { criarTelaGaleria } from './telas/galeria.js';
import { criarTelaOpcoes } from './telas/opcoes.js';
import { criarTela as criarTelaSom } from './telas/som.js';
import { criarTela as criarTelaEnergia } from './telas/energia.js';
import { criarTela as criarTelaMonitores } from './telas/monitores.js';
import { criarTela as criarTelaSono } from './telas/sono.js';
import { criarTela as criarTelaEstadoPc } from './telas/estado-pc.js';
import { criarTela as criarTelaControle } from './telas/controle.js';
import { criarTela as criarTelaTelaPc } from './telas/tela-pc.js';
import { criarTela as criarTelaJanelas } from './telas/janelas.js';
import { criarTela as criarTelaAtalhos } from './telas/atalhos.js';
import { criarTela as criarTelaEnviar } from './telas/enviar.js';

lerMao(); // aplica a mão salva (classe mao-esquerda) antes de desenhar

const pilhaEl = document.getElementById('pilha');
const camadas = document.getElementById('camadas');

const toast = criarToast({ container: camadas });
const dica = criarDica({ container: camadas });

// D14: a dica de voltar aparece uma vez só (localStorage com try/catch; sem ele, uma vez por carregamento)
let dicaVista = false;
function dicaJaVista() {
  if (dicaVista) return true;
  try { return localStorage.getItem('dica-voltar') === '1'; } catch { return false; }
}
function marcarDicaVista() {
  dicaVista = true;
  try { localStorage.setItem('dica-voltar', '1'); } catch { /* sem armazenamento */ }
}

const aviso = criarAviso({
  container: camadas,
  titulo: 'Desconectar deste PC?',
  texto: 'O celular deixa de controlar este computador.',
  rotuloAcao: 'Desconectar',
  perigo: true,
  aoConfirmar: () => enviar({ t: 'desconectar' }),
});

// D3: o que cada tela de área recebe (descrito em telas/LEIAME.md)
const ctx = {
  enviar,
  aoMudar,
  obterEstado,
  aoMensagem,
  assinar,
  toast: (texto, ms) => toast.mostrar(texto, ms),
  camadas,
  abrir: (nome) => pilha.abrir(nome),
  voltar: () => pilha.voltar(),
  preferencias: { lerMao, definirMao, aoMudarMao, ler, gravar },
};
// as chamadas a `pilha` só acontecem depois de ela existir
const inicio = criarTelaInicio({
  ctx,
  aoAbrirArea: (nome) => pilha.abrir(nome),
  aoAbrirOpcoes: () => pilha.abrir('opcoes'),
  aoDesconectar: () => aviso.abrir(),
});

const pilha = criarPilha({
  raiz: pilhaEl,
  inicial: inicio.el,
  inicialTopo: (sim) => { if (inicio.aoTopo) inicio.aoTopo(sim); }, // a 15a faz o Inicio devolver aoTopo(sim)
  bloqueado: () => !!document.querySelector('.aviso:not([hidden])'),
  aoAbrir(nome, profundidade) {
    if (profundidade === 1 && !dicaJaVista()) {
      marcarDicaVista();
      dica.mostrar('Arraste para o lado para voltar');
    }
  },
});

// cada área tem o seu módulo (D2); o nome é o de AREAS (inicio.js)
const MODULOS = {
  som: criarTelaSom,
  tela: criarTelaTelaPc,
  controle: criarTelaControle,
  energia: criarTelaEnergia,
  monitores: criarTelaMonitores,
  janelas: criarTelaJanelas,
  atalhos: criarTelaAtalhos,
  enviar: criarTelaEnviar,
  timer: criarTelaSono,
  estado: criarTelaEstadoPc,
};
for (const a of AREAS) pilha.registrar(a.nome, () => MODULOS[a.nome](ctx));

pilha.registrar('opcoes', () => criarTelaOpcoes({ ctx, aoVoltar: () => pilha.voltar() }));

// D26: /novo#galeria abre a Galeria dos componentes sem precisar do PC (sem conexão nem tela de acesso)
const modoGaleria = location.hash === '#galeria';
pilha.registrar('galeria', () => criarTelaGaleria({ container: camadas, toast, dica, aoVoltar: () => pilha.voltar() }));

const acesso = criarTelaAcesso({
  container: camadas,
  aoParear: (codigo) => parear(codigo),
  aoConectarDeNovo: () => reconectarAgora(),
});

aoMudar((estado) => inicio.atualizar(estado));

// mostra uma tela de acesso por cima e esconde (e zera) a pilha
function emAcesso(mostrar) {
  aviso.fechar();
  acesso.definirFaixa(false);
  pilhaEl.hidden = true;
  pilha.reiniciar();
  limparEstado();
  mostrar();
}

if (modoGaleria) {
  // ao recarregar numa tela interna a pilha ainda volta ao Início (popstate); abre a Galeria depois disso
  if (history.state && history.state.n > 0) window.addEventListener('popstate', () => pilha.abrir('galeria'), { once: true });
  else pilha.abrir('galeria');
} else iniciarConexao({
  aoConectado() {
    acesso.esconder();
    acesso.definirFaixa(false);
    pilhaEl.hidden = false;
    inicio.atualizar(obterEstado());
  },
  aoOcupado(por) { emAcesso(() => acesso.ocupado(por)); },
  aoDesconectado(mensagem) { emAcesso(() => acesso.desconectado(mensagem)); },
  aoPareamento(codigo, erro) {
    pilhaEl.hidden = true;
    acesso.definirFaixa(false);
    acesso.pareamento(codigo, erro);
  },
  aoReconectando(ligado, texto) {
    // primeira conexão (ainda sem estado): tela "Conectando…"; depois, só a faixa por cima do que já está aberto
    if (ligado && texto === 'Conectando…') {
      pilhaEl.hidden = true;
      acesso.conectando();
      acesso.definirFaixa(false);
    } else {
      acesso.definirFaixa(ligado, texto);
    }
  },
  aoAviso(texto) { toast.mostrar(texto, 4000); },
});
