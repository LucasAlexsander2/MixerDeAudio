# Estado do projeto

**Fase atual:** Fase 1 (bandeja e janelinha) concluída. Fase 2 (apps com volume e mudo) concluída (teste do usuário aprovado em 19/11/2026). Detalhes em `docs/fases/fase-1.md` e `docs/fases/fase-2.md`; plano em `docs/PLANO.md`.

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
