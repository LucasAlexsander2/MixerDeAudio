# Testes de tela

Scripts PowerShell que usam o build publicado (`publicado\MixerDeAudio.exe`). Nada de teclas nem cliques reais, nada de trazer janela para a frente: só linha de comando do app, UI Automation, `GetWindowRect` e `PrintWindow`. Arquivos temporários e capturas vão para `%TEMP%\mixer-check`.

| Arquivo | Para quê |
|---|---|
| `lib.ps1` | Auxiliares: abrir/encerrar o app, enviar comandos (`Cmd '--fechar'`), janelas visíveis do processo, UI Automation, captura por `PrintWindow`, leitura do log. |
| `roteiro-f1.ps1` | Fase 1, casos 1.1, 1.2, 1.3 e 1.5. Encerra o app no fim, também com erro. |
| `medir.ps1` | Fase 1, caso 1.4: memória e CPU. `-SemAparar` mede sem a devolução de memória (D17). |

Antes de rodar, publicar: `dotnet publish MixerDeAudio\MixerDeAudio.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained -o publicado`.

Rodar: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\testes\roteiro-f1.ps1`.

Observação: os nomes dos contadores de desempenho do Windows são traduzidos (pt-BR); por isso o `medir.ps1` lê o conjunto de trabalho privado pelo WMI (`Win32_PerfRawData_PerfProc_Process.WorkingSetPrivate`).
