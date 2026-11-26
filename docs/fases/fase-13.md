# Fase 13 — Identidade e design (Controladora PC)

Status: aprovada pelo usuário (25/11/2026). Fase sem código do app.

## 1. Objetivo

Fechar as decisões da Controladora PC (Q1 a Q7 e os pontos em aberto do protótipo) e registrar o guia de design (cores, fontes, ícones e componentes). Este documento é a base da Fase 14 em diante.

Fontes: `00 - Roadmap` (Obsidian), o protótipo aprovado `docs/prototipo/controladora.html` (commit 9021883), o desenho `Design Controladora` e a nota `Tela do PC com touchpad`.

## 2. Perguntas ao usuário

Cada uma com a recomendação. A coluna "Resposta" é preenchida depois da conversa.

| # | Pergunta | Recomendação | Resposta |
|---|---|---|---|
| Q1 | Nome exibido "Controladora PC"? | Sim, a partir da 2.0.0 (Fase 19). O pacote do Velopack (`MixerDeAudioApp`), a pasta de dados e o nome do executável não mudam, para não quebrar a atualização de quem já tem o app. | Aprovado. |
| Q2 | O repositório continua o mesmo? | Sim, com a versão 2.0.0 na troca. Renomear no GitHub fica para depois (o GitHub redireciona). | Aprovado. |
| Q3 | Muda só o celular ou também a janelinha do PC? | Só o celular. A janelinha fica na Fase 20, opcional. | Aprovado. |
| Q4 | Estilo do layout | **Já tem rumo pelo protótipo:** painel de blocos com pilha de telas. O Início tem favoritos, estado do PC e "Todas as funções" em blocos; cada área abre uma tela própria por cima, com voltar. Confirmar. | Confirmado. |
| Q5 | Em pé, deitado ou os dois? | **Já tem rumo pelo protótipo:** em pé em todas as telas; deitado só na Tela do PC (tela cheia com touchpad translúcido). O Controle fica em pé. Confirmar. | Confirmado. |
| Q6 | App da tela inicial (PWA)? | Sim: manifesto, ícone e abrir em tela cheia (Fase 14). Sem modo offline, porque o service worker exige HTTPS. | Aprovado. |
| Q7 | Tema claro (nova) | O app de hoje segue o tema claro/escuro do celular; o protótipo só tem o escuro. Recomendo **só o escuro** na 2.0: é a cara de controladora e o touchpad e o vidro translúcido foram ajustados para ele. O tema claro vai para "Depois da 2.0". Os tokens ficam prontos para receber um tema claro sem mudar os componentes. | Sem preferência: fica a recomendação (só escuro). |

### Pontos em aberto do protótipo

| # | Ponto | Recomendação | Resposta |
|---|---|---|---|
| A1 | Volume geral (não estava no desenho) | Manter, no Som, entre "Dispositivos conectados" e "Aplicações", como no protótipo. A regra de ouro pede (o app de hoje tem volume geral e mudo). | Aprovado. |
| A2 | GPU %, que o PC ainda não envia | Entra na **Fase 15**, porque o topo do Início é o primeiro lugar que a mostra. Parte no PC: contador de desempenho do Windows "GPU Engine / Utilization Percentage" (soma dos motores 3D, maior adaptador), lido junto com a CPU. Campo opcional `gpu` no estado; sem leitura, o celular mostra "GPU —". | Fase 15. |
| A3 | Texto do cartão de app | Nome do app em destaque, música embaixo e o dispositivo de saída num botãozinho (como no protótipo). O "Nome do dispositivo" do desenho vira esse botãozinho. | Aprovado. |
| A4 | Gesto de voltar | Arrastar para a esquerda, como pedido, em qualquer ponto da tela, menos nas áreas que já usam o arrasto: faixas com rolagem para o lado, barras de volume e brilho, campos de texto, touchpad, imagem do PC e avisos abertos. Volta com 30% da largura ou um arrasto rápido. Também ficam a seta no cabeçalho e o "voltar" do navegador/Android (a pilha de telas usa o histórico do navegador). O Início não tem voltar. | Aprovado. |
| A5 | Fonte | **Embutir** Manrope e JetBrains Mono no app do PC (arquivos WOFF2 só com letras latinas, cerca de 70 KB juntos), servidos pelo próprio PC com cache longo. Nada vem da internet. As duas são de licença livre (SIL OFL), e o texto da licença vai junto. A fonte do sistema mudaria a cara entre iPhone e Android. | Sem preferência: fica a recomendação (embutir). |

## 3. Decisões

| # | Decisão |
|---|---|
| D1 | A página nova é só tema escuro (`color-scheme: dark`) e `theme-color` `#0f1218` (Q7). |
| D2 | Cores só por tokens CSS em `:root`; nenhum componente usa cor fixa. |
| D3 | Fontes embutidas como recurso do executável, como o `index.html` hoje (A5). A Fase 14 cria a rota delas. |
| D4 | Ícones em SVG inline, traço fino, nada de fonte de ícones nem arquivos externos. |
| D5 | Coluna de no máximo 440 px centralizada; em pé é o padrão; deitado só na Tela do PC (Q5). |
| D6 | Alvo de toque mínimo de 44 × 44 px; respeita as bordas seguras do iPhone (`env(safe-area-inset-*)`). |
| D7 | Respeita "reduzir movimento" do sistema (sem transições). |
| D8 | Ações perigosas (Desligar, Reiniciar, Suspender, Bloquear, Fechar janela, Desconectar) sempre pedem confirmação. |
| D9 | Vibração curta (8 ms) no toque dos blocos e botões, onde o celular aceitar (o iPhone ignora). |

## 4. Guia de design

### 4.1 Cores (tokens)

| Token | Valor | Uso |
|---|---|---|
| `--bg` | `#0f1218` | Fundo das telas |
| `--surface` | `#171b23` | Blocos, cartões, chips |
| `--surface-2` | `#1f2430` | Botões dentro de cartão, bloco ligado, avisos |
| `--line` | `#2a3040` | Bordas e trilho das barras |
| `--fg` | `#e8ebf2` | Texto e botão da barra |
| `--muted` | `#8b93a7` | Texto secundário, rótulos |
| `--accent` | `#8fb4ff` | Destaque, item ligado, parte cheia da barra, foco |
| `--accent-ink` | `#0f1218` | Texto sobre o destaque |
| `--danger` | `#ff7a7a` | Ações perigosas, mudo |
| `--ok` | `#6fd3a0` | Conectado, sucesso |
| `--scrim` | `rgb(5 7 10 / .62)` | Fundo atrás dos avisos |
| `--glass` | `rgb(23 27 35 / .7)` | Touchpad translúcido (deitado) |
| `--sel` | `rgb(143 180 255 / .25)` | Seleção e realce do toque |

As cores `--desk-*`, `--paper*`, `--title`, `--task`, `--win-line` e `--ink-fake` do protótipo só desenham a tela falsa do PC e não entram no app.

Contraste conferido sobre `--bg`: texto `--fg` cerca de 15:1, `--muted` cerca de 6:1 e `--accent` cerca de 9:1 (todos acima de 4,5:1).

### 4.2 Tipografia

- **Manrope** (400, 500, 600, 700) para o texto; **JetBrains Mono** (500) só para números que mudam (volume, porcentagens, relógios, CPU/GPU/RAM), para não "pularem".
- Reserva: `system-ui, -apple-system, "Segoe UI", sans-serif` e `ui-monospace, Consolas, monospace`.
- Tamanhos: título da tela 18/600; seção 15/600; texto 14/400; nome no cartão 15/700; rótulo 12/600 em `--muted`; secundário 11 a 13; número grande 28.

### 4.3 Espaços e formas

- Margem lateral 16 px; espaço entre blocos e cartões 10 px.
- Cantos: botão 12 px; bloco 14 px; cartão e botão grande 16 px; chip em pílula.
- Borda de 1 px em `--line` em todas as superfícies; sem sombras.
- Toque: `scale(.97)` no bloco e no botão grande; troca de fundo nos botões menores.

### 4.4 Ícones

- Grade 24 × 24, traço 1,75, pontas e cantos arredondados, `currentColor`, sem preenchimento.
- Tamanhos: 24 px normal, 20 px em botão, 32 px em botão grande, 14 px no botãozinho de saída.
- Conjunto do protótipo (cerca de 45): voltar, volume, mudo, fone, alto-falante, monitor(es), sol, cadeado, play, pausa, anterior, próxima, controle, energia, janelas, atalho, enviar, lua, teclado, zoom, deitar, alça, setas, touchpad, reiniciar, fechar, voltar/avançar 10 s, tela cheia, abrir fora, atividade, área de transferência, arquivo, gravar, calculadora, nota, baixar, música, engrenagem, tarefas.

### 4.5 Componentes

| Componente | Como é | Onde |
|---|---|---|
| **Cabeçalho** | 52 px; seta de voltar 44 px, título 18/600 e ações à direita (chips). | Toda tela, menos o Início |
| **Bloco** | 100 × 76 px, ícone 24 + nome curto; ligado = borda e texto `--accent`; ponto verde no canto = ativo. Em faixa com rolagem para o lado (uma ou duas linhas) ou em grade de 2 ou 3 colunas. | Favoritos, Todas as funções, saídas de áudio, projeção, atalhos |
| **Botão grande** | Mínimo 96 px de altura, ícone 32 e texto 16/700; variantes perigo (borda vermelha) e principal (fundo `--accent`). | Energia, Acordar |
| **Botão** | 48 px de altura, `--surface-2`; variantes perigo e principal; em linhas de 2 ou 3. | Ações em geral |
| **Cartão** | `--surface`, canto 16, espaço interno 12. | Som (apps), Monitores, Estado |
| **Cartão de app** | Avatar redondo de 48 px (tocar = mudo, com risco vermelho), nome 15/700, música em `--muted`, botãozinho de saída; barra de volume com número; linha Anterior/Pausa/Próxima. Mudo deixa o cartão a 55%. | Som |
| **Barra (slider)** | Trilho de 4 px em `--line`, parte cheia em `--accent`, botão de 22 px em `--fg`; área de toque de 32 px; número em mono à direita. | Volume, brilho |
| **Chip** | Pílula de 32 px (40 px em grupo de escolha); marcado = fundo `--accent`. | Tempos do timer, modos, ações do cabeçalho |
| **Interruptor** | 50 × 30 px; ligado = `--accent`. | Manter a tela ligada, opções |
| **Medidor** | Barra de 8 px, cheia em `--accent`. | Estado do PC |
| **Touchpad** | Área `--surface` com borda, botões Esquerdo/Direito na base e faixa de rolagem na lateral; deitado vira painel `--glass` que arrasta e esconde. | Controle, Tela do PC |
| **Aviso de confirmação** | Cartão `--surface-2` centralizado sobre `--scrim`, título 17, texto em `--muted`, botões Cancelar e a ação (perigo em vermelho). O foco começa em Cancelar. | D8 |
| **Folha de opções** | Igual ao aviso, mas presa embaixo, com a lista de opções (marcada = `--accent`). | Escolher saída do app |
| **Toast** | Pílula no topo, fundo `--fg` e texto `--bg`, some sozinha. | Confirmações rápidas |
| **Dica** | Pílula embaixo, `--surface-2`, texto `--muted`; aparece uma vez ("Arraste para a esquerda para voltar"). | Primeira tela interna |

### 4.6 Navegação

- **Início** (raiz): favoritos, estado (CPU/GPU/RAM, toque abre Estado do PC), Todas as funções e Desconectar.
- **Telas internas** entram por cima e voltam pelo gesto (A4), pela seta ou pelo voltar do sistema.
- Áreas: Som, Tela do PC, Controle, Energia, Monitores, Janelas, Atalhos, Enviar, Timer de sono e Estado do PC. Cada uma aparece só se o grupo de ferramentas dela estiver ligado no PC (regra que já existe).

## 5. O que muda no roadmap

- Seção 3: coluna de respostas de Q1 a Q7.
- Fase 13: marcada como concluída, com link para este documento.
- Fase 15: acrescentar a GPU % (parte no PC) (A2).
- Fase 14: fontes embutidas (A5) e histórico do navegador na pilha de telas (A4).
- "Depois da 2.0": tema claro (Q7).

## 6. Roteiro de verificação

Fase sem código: não há build nem teste de tela. A bateria de testes unitários (904) não muda.

| Caso | Como | Quem |
|---|---|---|
| Respostas registradas | Q1 a Q7 e A1 a A5 respondidas aqui e no roadmap | Opus |
| Guia confere com o protótipo | Tokens e medidas da seção 4 iguais ao CSS de `docs/prototipo/controladora.html` | Opus |
| Aprovação | O usuário lê este documento e aprova | Usuário |

## 7. Resultado

Respostas registradas aqui e no roadmap; guia conferido com o CSS do protótipo; documento aprovado pelo usuário em 25/11/2026. Próximo passo: Fase 14, numa sessão nova.
