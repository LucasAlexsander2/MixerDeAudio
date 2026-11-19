# Testes de tela

Scripts PowerShell que usam o build publicado (`publicado\MixerDeAudio.exe`). Nada de teclas nem cliques reais, nada de trazer janela para a frente: só linha de comando do app, UI Automation, `GetWindowRect` e `PrintWindow`. Arquivos temporários e capturas vão para `%TEMP%\mixer-check`.

| Arquivo | Para quê |
|---|---|
| `lib.ps1` | Auxiliares: abrir/encerrar o app, enviar comandos (`Cmd '--fechar'`), janelas visíveis do processo, UI Automation, captura por `PrintWindow`, leitura do log. |
| `roteiro-f1.ps1` | Fase 1, casos 1.1, 1.2, 1.3 e 1.5. Encerra o app no fim, também com erro. |
| `medir.ps1` | Fase 1, caso 1.4: memória e CPU. `-SemAparar` mede sem a devolução de memória (D17). `-ComSom` (Fase 2, caso 2.9) mede com dois tocadores de teste tocando durante a rodada inteira. `-Ciclos N` (Fase 3; padrão 20) muda a quantidade de ciclos; com N > 20 mede também após o ciclo N. Registra também a contagem de handles (parado, ciclos 5, 20 e N). |
| `roteiro-f2.ps1` | Fase 2, casos 2.1 a 2.8 (lista, slider, mudo, mudança de fora, app novo e que fecha, mesmo `.exe`, fechar libera tudo, sem regressão). O 2.8 roda o `roteiro-f1.ps1`. Encerra o app e os tocadores no fim, também com erro. |
| `roteiro-f3.ps1` | Fase 3, casos 3.1 a 3.6 (saídas, volume geral lido e gravado, mudança de fora, sem regressão, medição). O 3.5 roda o `roteiro-f2.ps1` e o 3.6 roda `medir.ps1 -ComSom -Ciclos 50`; `-SemMedicao` pula o 3.6 (~5 min). **Nunca troca a saída padrão nem mexe no mudo geral**; o volume geral só leva o toque de ±1% e volta ao valor original num `finally`. Encerra o app e os tocadores no fim, também com erro. |
| `tocar.cs` | Tocador de teste (app de arquivo único, NAudio): toca **silêncio** na saída padrão e dá nome à própria sessão. `dotnet run tocar.cs -- --nome "MX-teste A" --segundos 120`. O roteiro o publica como `tocar.exe` (e a cópia `tocar-b.exe`, outro `.exe`, para testar apps diferentes). |
| `sessoes.cs` | `listar` mostra uma linha por sessão de todas as saídas ativas (`pid\|nome\|estado\|volume\|mudo`). `definir <pid> <volume 0-1> [mudo\|som]` muda a sessão de fora do app, para testar os eventos; **recusa** qualquer processo que não seja tocador de teste (`DisplayName` começando com `MX-teste`). Fase 3: `dispositivos` lista uma linha por saída ativa (`id\|nome curto\|nome completo\|formato\|padrao\|volume\|mudo`); `geral <volume 0-1>` muda o volume geral da saída padrão, mas **recusa** um valor a mais de 0,02 do atual (trava do toque mínimo). Nenhum comando troca a saída padrão ou mexe no mudo geral. |

Funções da Fase 2 em `lib.ps1`: `PrepararFerramentas` (publica `tocar`, `tocar-b` e `sessoes` em `%TEMP%\mixer-check\ferr`), `StartPlayer`, `StopPlayers` (só os tocadores iniciados pelo roteiro), `PlayersAbertos`, `Sessoes`, `SessionOf <pid>`, `DefinirSessao`, `Rows`, `RowVolume`, `SetRowVolume` (`RangeValuePattern.SetValue`, sem clique), `ToggleRowMute` (`TogglePattern`), `RowValueText`, `RowMuteName`. **`SetRowVolume` e `ToggleRowMute` só aceitam linhas `MX-teste ...`**: os apps reais do usuário são apenas lidos, nunca alterados.

Funções da Fase 3 em `lib.ps1`: `Devices` (saídas pelo `sessoes.exe dispositivos`), `DefinirGeral` (volume geral de fora, com a trava de 0,02), `DeviceButtons` (nome, texto e estado de cada botão da janela, pela UI Automation), `MasterValue`, `MasterMuted`, `MasterText` (só leitura) e `SetMasterValue` (`RangeValuePattern`, sem clique; **recusa** mais de ±2 em relação ao valor atual).

Antes de rodar, publicar: `dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publicado`.

Rodar: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\roteiro-f1.ps1` (idem para `roteiro-f2.ps1`, `roteiro-f3.ps1` e `medir.ps1 -ComSom`).

Observação: os nomes dos contadores de desempenho do Windows são traduzidos (pt-BR); por isso o `medir.ps1` lê o conjunto de trabalho privado pelo WMI (`Win32_PerfRawData_PerfProc_Process.WorkingSetPrivate`).
