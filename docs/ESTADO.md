# Estado do projeto

**Fase atual:** Fases 1 a 4 concluídas (Fase 4, saída por app: teste do usuário aprovado em 19/11/2026). Próxima: Fase 5 (configurações e favoritos). Detalhes em `docs/fases/fase-1.md` a `fase-4.md`; plano em `docs/PLANO.md`.

## Comandos

Na raiz do repositório (`C:\Projetos\Mixer de Audio`):

```
# Build (sem erros e sem avisos)
dotnet build MixerDeAudio.slnx -c Debug -p:Platform=x64 -v q

# Testes unitários (bateria inteira)
dotnet test MixerDeAudio.Tests -v q

# Publicar (Release, ReadyToRun, self-contained) em publicado\
dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publicado -v q

# Roteiro de tela e medição (usam publicado\)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\roteiro-f1.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\medir.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\medir.ps1 -SemAparar
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\roteiro-f2.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\medir.ps1 -ComSom
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\roteiro-f3.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\roteiro-f4.ps1   # -SemMedicao pula o 4.6

# Regerar o ícone
dotnet run tools\icone\gerar-icone.cs
```

## Linha de comando do app

`MixerDeAudio.exe` sem argumento: abre (segunda cópia) ou só cria o ícone (primeira cópia). `--abrir`, `--abrir-sem-foco` (roteiros), `--fechar`, `--sair`, `--sem-aparar` (desliga a devolução de memória, para medição).

## Roteiro da Fase 1

Casos 1.1 a 1.5 em `docs/fases/fase-1.md`, seção 6.2 (automáticos), e teste do usuário na seção 6.3 (clique real, Esc, Alt+Tab, menu do botão direito, Gerenciador de Tarefas).

## Medição (build publicado, 18/11/2026)

Memória em MB; "WS privado" é o conjunto de trabalho privado (a coluna "Memória" do Gerenciador de Tarefas).

| Rodada | Parado 10 s: WS privado / bytes privados | CPU parada (30 s) | Ciclo 5: WS privado / bytes privados | Ciclo 20: WS privado / bytes privados | Crescimento 5 a 20 (WS privado) |
|---|---|---|---|---|---|
| Normal | 2,8 / 75,4 | 0,007% | 1,5 / 103,7 | 0,9 / 106,2 | -0,6 |
| `--sem-aparar` | 23,5 / 75,9 | 0% | 38,1 / 104,5 | 42,1 / 106,0 | +4,0 |

Conjunto de trabalho total (parado 10 s): 10,2 MB normal, 92,5 MB sem aparar. Os bytes privados (memória confirmada, não necessariamente residente) ficam em ~75 a 106 MB e não são afetados pela devolução de memória: ponto a observar na Fase 2 e na Fase 6.

## Fase 2: comandos e medição (19/11/2026)

Ferramentas novas em `tools/testes/` (detalhes no `README.md`): `tocar.cs` (tocador de teste, toca silêncio), `sessoes.cs` (`listar` e `definir`, só em tocador `MX-teste`), `roteiro-f2.ps1` (casos 2.1 a 2.8, passaram) e `medir.ps1 -ComSom` (caso 2.9). A janela só observa o áudio enquanto está aberta (sem objetos COM de áudio em repouso). Tempo de abertura: 145 a 259 ms (meta < 300 ms).

Medição com dois tocadores tocando (build publicado; MB):

| Parado 10 s: WS privado / bytes privados | CPU parada (30 s) | Ciclo 5: WS privado / bytes privados | Ciclo 20: WS privado / bytes privados | Crescimento 5 a 20 (WS privado / bytes privados) |
|---|---|---|---|---|
| 2,8 / 74,7 | 0,003% | 2,5 / 115,8 | 1,4 / 124,8 | -1,1 / +9,0 |

Os bytes privados passam de 5 MB entre os ciclos 5 e 20, mas ficam estáveis depois (série de 50 ciclos: 120 → 127 até o ciclo 20, 126 no ciclo 50). A contagem de handles sobe ~4 por abertura mesmo sem áudio (WinUI): ponto para a Fase 6.

## Fase 3: comandos e medição (19/11/2026)

Novos: `roteiro-f3.ps1` (casos 3.1 a 3.6, o 3.5 roda o roteiro-f2; `-SemMedicao` pula o 3.6), `medir.ps1 -Ciclos N` (também registra handles), `sessoes.exe dispositivos` e `sessoes.exe geral <volume>` (trava de 0,02). Os roteiros nunca trocam a saída padrão nem mexem no mudo geral. Testes unitários: 95. Abertura: 137 a 150 ms. O `IMMNotificationClient` é interno no NAudio 3.3.0: usa-se `CreateNotificationClient(false)`. Detalhes em `docs/fases/fase-3.md`, seção 7.

Medição com dois tocadores, 50 ciclos (MB):

| Parado 10 s: WS priv / bytes priv | CPU parada | Ciclo 5: WS priv / bytes priv | Ciclo 20: WS priv / bytes priv | Ciclo 50: WS priv / bytes priv | Handles parado / 5 / 20 / 50 |
|---|---|---|---|---|---|
| 2,9 / 75,8 | 0% | 2,6 / 120,3 | 1,7 / 128,4 | 1,4 / 131,3 | 614 / 1273 / 1318 / 1406 |

WS privado 5 a 20: -0,9 MB; bytes privados 20 a 50: +2,9 MB (limite 3). Handles só registrados, para a Fase 6.

## Fase 4: comandos e medição (19/11/2026)

Novos: `roteiro-f4.ps1` (casos 4.1 a 4.6; o 4.5 roda o f3 sem medição, o 4.6 roda `medir.ps1 -ComSom -Ciclos 50`; `-SemMedicao` pula o 4.6), `sessoes.exe rota-ler <pid>` (só leitura, qualquer app) e `sessoes.exe rota <pid> <id|padrao>` (**só** processos `tocar` e `tocar-b`; recusa os outros com código 3). `lib.ps1`: `RouteButtons`, `ClickRoute` (só linhas `MX-teste`), `RotaLer`, `Rota`, `SessionDevice`. O roteiro lê a escolha dos apps reais antes e depois (têm que ser iguais) e apaga a escolha dos tocadores vivos no `finally`. Testes unitários: 118. Abertura: ~146 ms.

Na janela, cada app tem um botão (ícone) por saída; clicar numa que não é a padrão grava a escolha no Windows, e clicar na padrão apaga a escolha (o app volta a seguir a padrão). Com o tocador de teste a sessão só passou para a outra saída depois de reiniciar o tocador (D14).

Medição com dois tocadores, 50 ciclos (MB):

| Parado 10 s: WS priv / bytes priv | CPU parada | Ciclo 5: WS priv / bytes priv | Ciclo 20: WS priv / bytes priv | Ciclo 50: WS priv / bytes priv | Handles parado / 5 / 20 / 50 |
|---|---|---|---|---|---|
| 2,8 / 76,9 | 0% | 2,9 / 125,7 | 1,9 / 132,9 | 1,8 / 132,0 | 614 / 1249 / 1319 / 1425 |

WS privado 5 a 20: -1,0 MB; bytes privados 20 a 50: -0,9 MB (limite 3). Uma rodada anterior, antes de compartilhar o `StrategyBasedComWrappers`, deu +4,6 MB (ver desvios na seção 7 da fase).
