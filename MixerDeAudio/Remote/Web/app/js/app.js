// Entrada do novo front (/novo): liga a pilha de telas, a conexão com o PC e as telas provisórias (D10 a D14, D19).
import { criarPilha } from './pilha.js';
import { iniciarConexao, enviar, parear, reconectarAgora } from './conexao.js';
import { aoMudar, obterEstado, limparEstado } from './estado.js';
import { criarToast } from './componentes/toast.js';
import { criarDica } from './componentes/dica.js';
import { criarAviso } from './componentes/aviso.js';
import { criarTelaAcesso } from './telas/acesso.js';
import { criarTelaInicio, AREAS } from './telas/inicio.js';
import { criarTelaProvisoria } from './telas/provisoria.js';
import { criarTelaGaleria } from './telas/galeria.js';

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

// as chamadas a `pilha` só acontecem depois de ela existir
const inicio = criarTelaInicio({
  aoAbrirArea: (nome) => pilha.abrir(nome),
  aoDesconectar: () => aviso.abrir(),
});

const pilha = criarPilha({
  raiz: pilhaEl,
  inicial: inicio.el,
  bloqueado: () => !!document.querySelector('.aviso:not([hidden])'),
  aoAbrir(nome, profundidade) {
    if (profundidade === 1 && !dicaJaVista()) {
      marcarDicaVista();
      dica.mostrar('Arraste para a esquerda para voltar');
    }
  },
});
for (const a of AREAS) {
  pilha.registrar(a.nome, () => criarTelaProvisoria({ titulo: a.rotulo, icone: a.icone, aoVoltar: () => pilha.voltar() }));
}

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
