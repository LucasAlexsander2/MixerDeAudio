# Fase 17 — Troca e versão 2.0.0 (Controladora PC)

Status: análise pronta, aguardando aprovação do usuário (27/11/2026).

## 1. Objetivo

Fazer da Controladora PC o app de verdade:
- conferir que nada da página antiga se perdeu;
- servir o layout novo em `/` e tirar a página antiga;
- trocar o nome exibido para "Controladora PC" (Q1 da Fase 13);
- publicar a **2.0.0**, com as Novidades.

O pacote do Velopack (`MixerDeAudioApp`), a pasta de dados e o nome do executável não mudam, para a atualização continuar funcionando.

## 2. Perguntas ao usuário

| # | Pergunta | Recomendação | Resposta |
|---|---|---|---|
| P1 | A página antiga sai de vez? | Sim, sem rota de reserva. Ela continua no histórico do git. A cobertura foi conferida: os 29 tipos de mensagem que ela manda ao PC também saem do `/novo`, e você testou todas as áreas na 1.4.0. | |
| P2 | E a Galeria dos componentes (`/novo#galeria`)? | Sai da página (D26 da Fase 14). O arquivo some junto; os componentes continuam documentados no `LEIAME.md`. | |
| P3 | Nome no menu Iniciar | Passa a ser "Controladora PC" (`--packTitle` do Velopack). Se a atualização deixar o atalho antigo com o nome velho, o atalho continua funcionando e eu anoto. | |
| P4 | HTTPS (16c) | Fica para depois da 2.0, como um estudo no iPhone. | |
| P5 | Ícones dos apps no celular (`/icone/`) | Corrigir o cabeçalho de cache duplicado (pendência da Fase 14), para o ícone ficar guardado por 1 h no celular. | |

## 3. Levantamento (27/11/2026)

- `GET /` monta `antigo/` (18 partes, hash da D8 da Fase 14) e `GET /novo` serve `app/index.html`. O manifesto usa `start_url` `/novo`. O QR aponta para `/#c=`, e o `conexao.js` do novo já trata o `#c=`.
- **Tipos de mensagem da página antiga:** `acordado`, `assinar`, `atalho`, `atualizar`, `botao`, `brilho`, `clipGravar`, `clipLer`, `clique`, `desconectar`, `energia`, `energiaCancelar`, `geral`, `geralMudo`, `janela`, `janelas`, `midia`, `mouse`, `mouseAbs`, `mudo`, `padrao`, `projecao`, `rolar`, `rota`, `sono`, `tecla`, `telas`, `texto`, `volume`. Todos aparecem nos módulos de `app/js/`.
- O `app/index.html` não tem script nem estilo dentro do HTML, então a CSP pode tirar `'unsafe-inline'` do `script-src`. O JS usa `innerHTML` para os ícones SVG, e por isso o `style-src` continua com `'unsafe-inline'`.
- "Mixer de Áudio" aparece em `App.xaml.cs`, `Flyout/FlyoutWindow.xaml(.cs)`, `Flyout/PcPromptWindow.xaml(.cs)`, `Pc/PcController.cs` (balões), `Tray/TrayIconService.cs` e no `--packTitle` do `release.yml`.
- Testes e roteiros que dependem da página antiga:
  - o hash em `WebAssetsTests`;
  - os testes de página do `RemoteRulesTests`;
  - o caso 14.1 do `roteiro-f14`;
  - a CSP esperada no `roteiro-f16` (16a.3).

## 4. Decisões

| # | Decisão |
|---|---|
| D1 | `GET /` serve `app/index.html`, com `no-store` e a CSP. `GET /novo` e `/novo/` respondem 301 para `/`, o que mantém funcionando os atalhos de Tela de Início criados na prévia. O resto das rotas não muda. |
| D2 | A pasta `Remote/Web/antigo/` sai, junto com `WebAssets.OldPageParts`, o hash e os testes dela. |
| D3 | **Nada se perdeu:** um teste novo mantém a lista fixa dos 29 tipos da seção 3 e exige que cada um apareça num módulo de `app/js/`. Ele substitui a garantia do hash. |
| D4 | Manifesto com `start_url` `/` (o `scope` já é `/`). |
| D5 | CSP das páginas: `frame-ancestors 'none'; default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; font-src 'self'; connect-src 'self' ws://<Host>`. |
| D6 | A Galeria sai (P2): `telas/galeria.js`, o registro em `app.js` e os testes dela. O `criarGrupoMao` continua em `opcoes.js`. |
| D7 | **Nome exibido "Controladora PC":** dica da bandeja, títulos das janelas, balões, a janelinha do PC e o `--packTitle`. Não mudam: o `AssemblyName`, o pacote do Velopack, a pasta de dados, o valor `MixerDeAudio` da chave Run, o nome do log e o repositório. |
| D8 | `/icone/`: `WriteAsync` já aceita o cache como parâmetro (Fase 14). O ícone passa `private, max-age=3600` nesse parâmetro, em vez de um segundo `Cache-Control` (P5). |
| D9 | Versão **2.0.0** com Novidades de 3 a 5 itens: "A Controladora PC chegou" (layout novo no celular, abra o endereço de sempre), "Feita para as duas mãos", "Tela do PC com touchpad" e "Mais segurança". O texto não cita `/novo`. |
| D10 | Uma parte só, com um agente Sonnet `medium`: a fase é curta e os arquivos se cruzam (servidor, testes e roteiros). |

## 5. Arquivos

- **Servidor:** `Remote/RemoteServer.cs` (D1, D8) e `Remote/WebAssets.cs` (D2) e `Remote/RemoteRules.cs` (D5).
- **Celular:**
  - `Remote/Web/antigo/` sai;
  - `app/manifest.webmanifest` muda (D4);
  - `app/js/app.js` muda e `app/js/telas/galeria.js` sai (D6);
  - `app/js/componentes/LEIAME.md` deixa de citar a Galeria.
- **PC:** os arquivos da D7 e o `.github/workflows/release.yml` (`--packTitle`).
- **Testes:** `WebAssetsTests`, `RemoteRulesTests`, `WebFrontTests` e os `WebFront15*Tests` que citam a Galeria ou a página antiga. Teste novo da D3.
- **Roteiros:**
  - `tools/testes/roteiro-f17.ps1` novo (casos da seção 6.2);
  - `roteiro-f14` deixa de ter o 14.1, que fica marcado como "substituído pelo 17.1";
  - `roteiro-f16` passa a esperar a CSP nova.
- **Entrega:** `MixerDeAudio.csproj` com a versão 2.0.0 e o `Novidades.json`.

## 6. Testes

### 6.1 Unitários

A bateria inteira (1213 hoje) passa. Testes novos ou atualizados:
- rota `/` serve o novo e `/novo` dá 301;
- a D3;
- o manifesto com `start_url` `/`;
- a CSP sem `'unsafe-inline'` no `script-src`;
- o cache do ícone;
- nenhuma referência a `antigo/` nem à Galeria.

### 6.2 Roteiros sem janela (Opus roda depois da implementação)

| Caso | Por que precisa do app | Esperado |
|---|---|---|
| 17.1 | Recursos reais | `GET /` 200 com `/app/js/app.js` no HTML e a CSP da D5; `GET /novo` 301 com `Location: /` |
| 17.2 | Ícone real | `/icone/<id>` com um único `Cache-Control: private, max-age=3600` |
| 17.x | Sem regressão | `roteiro-f14` (14.2 a 14.4), `roteiro-f15a`, `roteiro-f15c`, `roteiro-f16 -Parte a` e `-Parte b` |

### 6.3 Conferência (Opus) e usuário

- **Opus:**
  - com o `servidor-teste.ps1`, pareamento pelo `/#c=` no navegador embutido, as áreas abrindo pelo `/` e nenhum erro de CSP no console;
  - pela UI Automation, o nome nas Configurações e na janela.
- **Usuário (2.0.0):**
  - U1: abrir `http://<PC>:8750/` no iPhone: é o layout novo.
  - U2: o atalho antigo da Tela de Início (o da prévia em `/novo`) ainda abre.
  - U3: no PC, aparece "Controladora PC" na bandeja, nos balões e no menu Iniciar depois de atualizar.
  - U4: as Novidades da 2.0.0 aparecem.

## 7. Resultados

(preenchido na implementação e na conferência)

## 8. Pendências

- 16c (HTTPS) depois da 2.0.0 (P4).
- O ambiente `release` ainda sem aprovador, o 2FA e o ruleset das tags (tarefas do usuário).
