# Testes de tela

Scripts PowerShell que usam o build publicado (`publicado\MixerDeAudio.exe`). Nada de teclas nem cliques reais, nada de trazer janela para a frente: só linha de comando do app, UI Automation, `GetWindowRect` e `PrintWindow`. Arquivos temporários e capturas vão para `%TEMP%\mixer-check`.

| Arquivo | Para quê |
|---|---|
| `lib.ps1` | Auxiliares: abrir/encerrar o app, enviar comandos (`Cmd '--fechar'`), janelas visíveis do processo, UI Automation, captura por `PrintWindow`, leitura do log. |
| `roteiro-f1.ps1` | Fase 1, casos 1.1, 1.2, 1.3 e 1.5. Encerra o app no fim, também com erro. |
| `medir.ps1` | Fase 1, caso 1.4: memória e CPU. `-SemAparar` mede sem a devolução de memória (D17). `-ComSom` (Fase 2, caso 2.9) mede com dois tocadores de teste tocando durante a rodada inteira. |
| `roteiro-f2.ps1` | Fase 2, casos 2.1 a 2.8 (lista, slider, mudo, mudança de fora, app novo e que fecha, mesmo `.exe`, fechar libera tudo, sem regressão). O 2.8 roda o `roteiro-f1.ps1`. Encerra o app e os tocadores no fim, também com erro. |
| `tocar.cs` | Tocador de teste (app de arquivo único, NAudio): toca **silêncio** na saída padrão e dá nome à própria sessão. `dotnet run tocar.cs -- --nome "MX-teste A" --segundos 120`. O roteiro o publica como `tocar.exe` (e a cópia `tocar-b.exe`, outro `.exe`, para testar apps diferentes). |
| `sessoes.cs` | `listar` mostra uma linha por sessão de todas as saídas ativas (`pid\|nome\|estado\|volume\|mudo`). `definir <pid> <volume 0-1> [mudo\|som]` muda a sessão de fora do app, para testar os eventos; **recusa** qualquer processo que não seja tocador de teste (`DisplayName` começando com `MX-teste`). |

Funções da Fase 2 em `lib.ps1`: `PrepararFerramentas` (publica `tocar`, `tocar-b` e `sessoes` em `%TEMP%\mixer-check\ferr`), `StartPlayer`, `StopPlayers` (só os tocadores iniciados pelo roteiro), `PlayersAbertos`, `Sessoes`, `SessionOf <pid>`, `DefinirSessao`, `Rows`, `RowVolume`, `SetRowVolume` (`RangeValuePattern.SetValue`, sem clique), `ToggleRowMute` (`TogglePattern`), `RowValueText`, `RowMuteName`. **`SetRowVolume` e `ToggleRowMute` só aceitam linhas `MX-teste ...`**: os apps reais do usuário são apenas lidos, nunca alterados.

Antes de rodar, publicar: `dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publicado`.

Rodar: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\roteiro-f1.ps1` (idem para `roteiro-f2.ps1` e `medir.ps1 -ComSom`).

Observação: os nomes dos contadores de desempenho do Windows são traduzidos (pt-BR); por isso o `medir.ps1` lê o conjunto de trabalho privado pelo WMI (`Win32_PerfRawData_PerfProc_Process.WorkingSetPrivate`).
