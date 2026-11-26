// Telas de acesso (D19): conectando, pareamento com 6 dígitos, ocupado, desconectado, e a faixa "Reconectando…".
// Textos e regras iguais aos da página antiga (antigo/03-corpo.html e 06-pareamento.js).
import { criarBotao } from '../componentes/botao.js';

// opções: { container, aoParear(codigo) -> Promise<erro|null>, aoConectarDeNovo }.
// Devolve { el, faixa, conectando(), pareamento(codigo?, erro?), ocupado(por?), desconectado(mensagem), esconder(), definirFaixa(ligado, texto) }.
export function criarTelaAcesso({ container, aoParear, aoConectarDeNovo }) {
  const el = document.createElement('main');
  el.className = 'acesso';
  el.hidden = true;

  const faixa = document.createElement('div');
  faixa.className = 'faixa-conexao';
  faixa.setAttribute('role', 'status');
  faixa.hidden = true;

  const titulo = document.createElement('h1');
  const texto = document.createElement('p');
  const codigo = document.createElement('input');
  codigo.inputMode = 'numeric';
  codigo.pattern = '[0-9]*';
  codigo.maxLength = 6;
  codigo.autocomplete = 'one-time-code';
  codigo.placeholder = '000000';
  codigo.setAttribute('aria-label', 'Código de 6 dígitos');
  const conectar = criarBotao({ texto: 'Conectar', variante: 'principal' });
  const erro = document.createElement('p');
  erro.className = 'erro';
  erro.setAttribute('role', 'alert');
  // Fase 16 (D4): enquanto o PC pergunta se permite este celular
  const espera = document.createElement('p');
  espera.setAttribute('role', 'status');
  espera.hidden = true;
  const nota = document.createElement('p');
  const denovo = criarBotao({ texto: 'Conectar', variante: 'principal', aoClicar: () => aoConectarDeNovo() });
  el.append(titulo, texto, codigo, conectar.el, erro, espera, nota, denovo.el);

  function mostrar(modo) {
    el.hidden = false;
    codigo.hidden = conectar.el.hidden = erro.hidden = modo !== 'pareamento';
    nota.hidden = modo !== 'ocupado';
    if (modo !== 'pareamento') espera.hidden = true;
    denovo.el.hidden = modo !== 'desconectado';
    texto.hidden = modo === 'conectando';
  }

  async function enviarCodigo() {
    if (codigo.value.length !== 6) return;
    conectar.desativar(true);
    erro.textContent = '';
    // resposta rápida (código errado) não pisca o aviso; a confirmação no PC demora até 60 s
    const aviso = setTimeout(() => { espera.textContent = 'Confirme no PC'; espera.hidden = false; }, 800);
    const e = await aoParear(codigo.value);
    clearTimeout(aviso);
    espera.hidden = true;
    if (e) erro.textContent = e; else codigo.value = '';
    conectar.desativar(codigo.value.length !== 6);
  }
  codigo.addEventListener('input', () => {
    codigo.value = codigo.value.replace(/\D/g, '').slice(0, 6);
    conectar.desativar(codigo.value.length !== 6);
  });
  codigo.addEventListener('keydown', (e) => { if (e.key === 'Enter') enviarCodigo(); });
  conectar.el.addEventListener('click', enviarCodigo);

  container.append(el, faixa);

  return {
    el,
    faixa,
    conectando() {
      titulo.textContent = 'Controladora PC';
      mostrar('conectando');
    },
    // sem argumentos: pede o código; com código: preenche (vindo do endereço); com erro: mostra o motivo
    pareamento(cod, msg) {
      titulo.textContent = 'Controladora PC';
      texto.textContent = 'Digite o código de 6 dígitos que aparece nas Configurações do app, no PC.';
      mostrar('pareamento');
      if (cod) codigo.value = cod;
      erro.textContent = msg || '';
      conectar.desativar(codigo.value.length !== 6);
      if (!cod) codigo.focus();
    },
    ocupado(por) {
      titulo.textContent = 'Controladora PC';
      texto.textContent = 'Outro celular está no controle' + (por ? ' (' + por + ')' : '') + '. Desconecte-o pelo PC ou por ele.';
      nota.textContent = 'Tentando de novo a cada 5 segundos…';
      mostrar('ocupado');
    },
    desconectado(mensagem) {
      titulo.textContent = 'Desconectado';
      texto.textContent = mensagem;
      mostrar('desconectado');
    },
    esconder() { el.hidden = true; },
    definirFaixa(ligado, msg) {
      if (msg) faixa.textContent = msg;
      faixa.hidden = !ligado;
    },
  };
}
