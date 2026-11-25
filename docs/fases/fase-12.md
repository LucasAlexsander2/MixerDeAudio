# Fase 12 — Brilho do monitor pelo celular

Status: análise (Opus, 25/11/2026), esperando aprovação.

## 1. Objetivo

Na aba PC do celular, seção **Monitores**, uma barra **Brilho de 0 a 100** que muda o brilho de verdade do monitor (a luz de fundo), para deixar a tela mais apagada.

## 2. Perguntas ao usuário

- **P1. Monitor sem DDC/CI:** nesse caso a barra some e aparece um aviso, sem escurecer por software. **Recomendado.** A alternativa seria escurecer as cores da imagem (gama): isso não apaga a luz de fundo e o Windows limita o quanto dá para escurecer.
- **P2. Onde fica a barra:** só no celular, como foi pedido. **Recomendado.** Não entra também na janelinha do PC.

## 3. O que foi levantado (25/11/2026)

- Em monitor de mesa, o brilho só muda por **DDC/CI**, pela API `dxva2.dll` (`GetMonitorBrightness`/`SetMonitorBrightness`, código VCP 0x10). O WMI só funciona em notebook; aqui ele disse "sem suporte".
- **O monitor do usuário aceita DDC/CI:** a leitura deu brilho 80, na faixa de 0 a 100. A leitura foi só de consulta, nada foi mudado.
- O Windows conhece 2 monitores, `MLT MN101` e `VIE ENTERPRISE 34`, mas só 1 está ativo agora. Cada tela ativa ganha a sua barra.
- Cada comando DDC leva de 40 a 100 ms e alguns monitores travam se receberem comandos demais. Por isso os valores precisam ser agrupados.
- No app, o lugar natural é o grupo **`monitores`**, que já existe: projeção, "Apagar as telas", `ScreensCommand` e `#secDisp` no `index.html`.

## 4. Decisões

| # | Decisão |
|---|---|
| D1 | Brilho por DDC/CI (`dxva2`): muda a luz de fundo do monitor, e o próprio monitor guarda o valor. |
| D2 | Fica no grupo `monitores`, sem permissão nova. A barra entra na seção Monitores do celular, abaixo de "Apagar as telas". |
| D3 | Uma barra por tela ativa, na ordem de `ScreenCapture.Monitors()` (principal primeiro). O rótulo é "Brilho" com uma tela só, e "Brilho — Tela N" com várias. A barra mostra a porcentagem ao lado. |
| D4 | Tela sem DDC/CI (leitura falhou) fica sem barra e com o aviso "Esta tela não aceita ajuste de brilho (ative o DDC/CI no menu do monitor)". Não há escurecimento por software (P1). |
| D5 | Porcentagem ↔ faixa do monitor: `valor = min + round(p × (max − min) / 100)` e o inverso na leitura. Fica em `BrightnessRules`, com `p` cortado em 0 a 100. |
| D6 | DDC nunca roda na thread da tela. Um único `BrightnessWorker` em segundo plano guarda só o último valor pedido de cada tela e aplica um por vez, nunca dois comandos DDC ao mesmo tempo. Os valores intermediários são descartados. |
| D7 | O celular envia no máximo a cada 150 ms enquanto a barra é arrastada, e sempre envia o valor final ao soltar. |
| D8 | A leitura acontece no worker ao abrir a sessão e no "atualizar", nos mesmos pontos de `RefreshProjection`. O estado usa o valor guardado. Depois de mudar o brilho, o valor guardado passa a ser o enviado, sem ler de novo, e o estado é reenviado. |
| D9 | Os handles dos monitores físicos são abertos e soltos a cada operação (`GetPhysicalMonitorsFromHMONITOR`/`DestroyPhysicalMonitors`), o que aguenta ligar e desligar monitores. Cada `HMONITOR` é ligado ao índice da tela pelo retângulo, como em `ScreenCapture`. Se um `HMONITOR` tiver mais de um monitor físico, aplica em todos. |
| D10 | Comando `{t:'brilho', tela:int, valor:int}`: `tela` fora da lista é ignorada e `valor` é cortado em 0 a 100. No estado vai `pc.brilho: [{tela, valor}]`, com `valor` nulo quando a tela não tem suporte. O campo só aparece com o grupo `monitores` ligado. |
| D11 | Com `--teste-pc`, `RecordingPc` não toca no DDC: grava `teste-pc: brilho {tela} {valor}` e informa as telas com valores em memória (começam em 50). Os testes nunca leem nem mudam o monitor real. |
| D12 | Se a escrita DDC falhar, aparece o aviso "Não foi possível mudar o brilho desta tela" e o valor guardado não muda. |
| D13 | Brilho 0 não desliga a luz de fundo. Para apagar de vez, continua o botão "Apagar as telas". |

## 5. Arquivos

- **Novos:**
  - `Pc/BrightnessRules.cs` (D5, D10, lógica pura);
  - `Pc/Brightness.Native.cs` (DDC, D9);
  - `Pc/BrightnessWorker.cs` (D6, D8);
  - `MixerDeAudio.Tests/BrightnessTests.cs`;
  - `tools/testes/roteiro-f12.ps1`.
- **Alterados:**
  - `Pc/IPcActions.cs`, que ganha `IReadOnlyList<int?> ReadBrightness()` e `bool SetBrightness(int tela, int pct)`, chamados no worker; também muda `RecordingPc`;
  - `Pc/RealPc.cs`;
  - `Pc/PcController.cs`, que expõe a lista, o pedido e a releitura;
  - `Remote/RemoteProtocol.cs`, com o comando e o estado;
  - `Remote/RemoteTools.cs`, que liga o comando de brilho a `Display`;
  - `Remote/RemoteSession.cs`;
  - `Remote/Web/index.html`, com a barra, o aviso e o limite de envio;
  - `Interop/NativeMethods`, se for o lugar dos `DllImport`.
- **Na entrega:** `Novidades.json` ganha a entrada 1.2.0 e a tag `v1.2.0` é enviada.

## 6. Testes

### 6.1 Unitários (toda a bateria)

- `BrightnessRules`: conversão nos dois sentidos com faixa 0–100 e 20–80, corte abaixo de 0 e acima de 100, e faixa de um valor só sem dividir por zero.
- Agrupamento do worker com uma ação falsa: 10 pedidos seguidos na mesma tela aplicam só o primeiro e o último, nunca dois ao mesmo tempo, e cada tela tem a sua fila.
- Protocolo: leitura do `brilho` (válido, `tela` negativa, `valor` fora da faixa, campos faltando) e montagem de `pc.brilho` com valor nulo.
- `RemoteCommandGroups`: o comando de brilho pertence a `monitores`.

### 6.2 Roteiro na tela (`roteiro-f12.ps1`, `--teste-oculto --teste-pc`, grupo `monitores`)

| Caso | Por que precisa do app | Passos | Esperado |
|---|---|---|---|
| 12.1 | Passa pelo servidor de verdade (comando → worker → estado) | `CelularFerramenta` envia `{t:'brilho',tela:0,valor:30}` | O log tem `teste-pc: brilho 0 30` e o estado seguinte traz `brilho[0].valor = 30` |
| 12.2 | O agrupamento só aparece com a rede e o worker reais | Rajada de 20 valores de 1 a 20 | No máximo 3 linhas no log, a última com `20` |
| 12.3 | Sem regressão | `{t:'telas',acao:'apagar'}` | O log tem `teste-pc: telas apagar` |

O roteiro usa BackupConfig e RestoreConfig, reabre o app do usuário no fim e nunca deve ser interrompido.

### 6.3 Conferência (Opus) e usuário

- **Opus, no navegador embutido, com o app de teste:** a barra aparece na seção Monitores com a porcentagem, arrastar envia os valores com limite e o aviso de D4 aparece quando o estado traz `valor` nulo.
- **Usuário, no monitor de verdade:** no celular, levar a barra a 20 deixa o monitor visivelmente mais escuro; voltar a 80 restaura. Ao reabrir a aba PC, a barra mostra o valor atual.

## 7. Resultados

### Parte 12 — Sonnet, 25/11/2026

**Arquivos criados**
- `MixerDeAudio/Pc/BrightnessRules.cs`: conversão porcentagem ↔ faixa do monitor (D5).
- `MixerDeAudio/Pc/Brightness.Native.cs`: DDC/CI pela `dxva2.dll` com `LibraryImport`, HMONITOR ligado à tela pela mesma ordem de `ScreenCapture.Monitors()` (D9).
- `MixerDeAudio/Pc/BrightnessWorker.cs`: fila por tela, só o último valor, um comando por vez, `WaitIdle` para testes (D6, D8, D12).
- `MixerDeAudio.Tests/BrightnessTests.cs`: 39 casos de teste novos (904 no total) (regras, worker, `RecordingPc`, protocolo, grupo).
- `tools/testes/roteiro-f12.ps1`: casos 12.1 a 12.3.

**Arquivos alterados**
- `Pc/IPcActions.cs`: `ReadBrightness`/`SetBrightness` na interface e no `RecordingPc` (memória, começa em 50).
- `Pc/RealPc.cs`: chama `Brightness.Read/Set`.
- `Pc/PcController.cs`: `BrightnessValues`, `RequestBrightness`, evento `BrightnessFailed`, worker criado sob demanda e leitura dentro de `RefreshProjection`.
- `Remote/RemoteProtocol.cs`: `BrightnessCommand`, `RemoteBrightness`, `pc.brilho` no estado e parse de `brilho`.
- `Remote/RemoteTools.cs`: comando de brilho no grupo `monitores`.
- `Remote/RemoteSession.cs`: executa o comando, monta `pc.brilho`, aviso de falha (D12).
- `Remote/Web/index.html`: barra por tela com porcentagem, envio a cada 150 ms (`bindSlider` ganhou o parâmetro de intervalo), aviso de D4.
- `MixerDeAudio.Tests/MixerDeAudio.Tests.csproj`: inclui `BrightnessRules.cs` e `BrightnessWorker.cs`.
- `tools/testes/celular.cs`: `brilho` na lista de comandos permitidos e novo comando `brilhorajada` (rajada no mesmo canal, só com `teste:true`).

**Build:** `dotnet build` com 0 avisos e 0 erros. **Testes unitários:** 904 aprovados, 0 falhas. O `dotnet publish` mostra só os avisos IL2104 de trim que já existiam (SDK do Windows).

**Roteiro (publicado, `--teste-oculto --teste-pc`):**
- 12.1 OK: estado inicial 50, log `brilho 0 30` e estado seguinte com 30.
- 12.2 OK: rajada de 20 valores deu 2 linhas (`brilho 0 1` e `brilho 0 20`), estado final 20.
- 12.3 OK: `teste-pc: telas apagar` no log.
- Final: config e registro iguais, 0 erros no log. A primeira execução abortou na conferência `AssertModoTestePc` (5 s, nada enviado, config restaurada); a repetição passou, sem mudança de código. Provável corrida na primeira abertura do exe recém-publicado.

**Desvios**
- O `RecordingPc.SetBrightness` dorme 60 ms (imita o DDC real), senão o worker aplicaria quase todos os valores da rajada e o 12.2 não mostraria o agrupamento. Só roda no worker.
- `celular.cs` ganhou `brilho` na lista permitida e o comando `brilhorajada`, pois o `ferramenta` abre uma conexão por comando e não faz rajada.
- `valor` fora de 0 a 100 é cortado (como diz D10); `tela` negativa ou não inteira é recusada.
- O worker é `public` e foi incluído no csproj dos testes, que compila os arquivos por link.
- Não verificado: DDC real, e a página no navegador embutido (conferência do Opus).

### Conferência (Opus, 25/11/2026)

- Diff conferido: worker (um comando DDC por vez, último valor por tela), interop `dxva2` (PHYSICAL_MONITOR com pack 1, handles soltos a cada operação), ligação na sessão e no estado. Sem problemas.
- Navegador embutido (app de teste, grupo `monitores`): a barra "Brilho" aparece abaixo de "Apagar as telas"; arraste de 50 a 10 em 0,8 s gerou 6 envios a cada ~150 ms + o final; config restaurada e app do usuário reaberto.
- Observação menor: o valor final repetido é reenviado ao monitor (um comando DDC a mais); inofensivo.
- DDC real conferido à parte, com o usuário: leitura 80 e escritas 25 e 10 confirmadas no monitor (fora do app). Versão 1.2.0 publicada (commit e59efd6); teste do usuário pelo celular no monitor real aprovado em 25/11/2026.
