# Roteiro de tela da Fase 15, parte 15c (Controle e Tela): so o que depende do app de verdade (o resto fica nos testes unitarios).
#   15c.1 Ponteiro no canal real: /tela/ws com cur:2 e p:1 manda {"p":[x,y]} (0 a 1) antes de cada quadro.
#   15c.2 Sem regressao da pagina antiga: /tela/ws sem cur e p manda so quadros binarios, como sempre.
#   15c.3 Teclas com modificadores (--teste-pc): k "c" com mods ["ctrl"] vira ctrl+c no log; k "q" com mods e recusado.
# Usa o build publicado em publicado\ (publique antes). Sem teclas nem cliques reais, sem trazer janela para a frente: so linha de comando do app
# e o celular de teste (celular.cs) em 127.0.0.1. A captura da tela e real, so de leitura, e a imagem nunca e gravada em disco.
# SEGURANCA: o app roda SEMPRE com --teste-oculto --celular-local --teste-pc (sem janela nem icone). Nenhuma tecla, clique ou acao real no PC.
# BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro; nada mata o roteiro no meio (esperas por condicao, com prazo folgado).
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function Pronto([string]$arquivo) { WaitFor { (Test-Path $arquivo) -and ((Get-Content $arquivo -Encoding utf8 -ErrorAction SilentlyContinue) -contains 'PRONTO') } 5 }
function Env([string]$json) { CelularFerramenta $porta $pa.Chave $json }   # um celular de teste por comando (conecta, envia, desconecta)

$seg = $null; $arq = "$script:out\f15c-segurar.txt"
$difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false
try {
  StopAll
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"

  $porta = LigarFerramentas @('tela', 'entrada')
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  AssertModoTestePc
  $pa = CelularParear $porta '123456'
  if ($pa.Status -ne 200 -or -not $pa.Chave) { throw "o pareamento falhou ($($pa.Status))" }

  # 15c.3 Teclas com modificadores (sem ninguem segurando a sessao: cada comando conecta e desconecta, como no 9.6)
  $codigos = @(
    (Env '{"t":"tecla","k":"c","mods":["ctrl"]}'),
    (Env '{"t":"tecla","k":"left","mods":["shift","ctrl"]}'),
    (Env '{"t":"tecla","k":"q","mods":["ctrl"]}'),            # letra fora da lista: recusada
    (Env '{"t":"tecla","k":"c","mods":["meta"]}'),            # modificador desconhecido: recusado
    (Env '{"t":"tecla","k":"delete","mods":["ctrl","alt"]}'), # nunca Ctrl+Alt+Delete: recusado
    (Env '{"t":"tecla","k":"alt+tab"}'),                      # a forma antiga continua igual
    (Env '{"t":"tecla","k":"win+r"}'))                        # e fora da lista continua recusada
  WaitFor { @(LogTestePc '^entrada tecla alt\+tab 4 eventos$').Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 400
  $tx = @(LogTestePc '^entrada tecla ' | ForEach-Object { $_.Texto })
  $conta = { param($pat) @($tx | Where-Object { $_ -match $pat }).Count }
  $enviou = [bool]((@($codigos | Where-Object { $_ -ne 0 })).Count -eq 0)
  $okCtrlC = (& $conta '^entrada tecla ctrl\+c 4 eventos$') -eq 1
  $okCombo = (& $conta '^entrada tecla ctrl\+shift\+left 6 eventos$') -eq 1
  $okAntiga = (& $conta '^entrada tecla alt\+tab 4 eventos$') -eq 1
  $semRecusadas = (@($tx | Where-Object { $_ -match 'ctrl\+q|meta|ctrl\+alt\+delete|win\+r' }).Count -eq 0)
  Resultado '15c.3 Teclas com modificadores' ([bool]($enviou -and $okCtrlC -and $okCombo -and $okAntiga -and $semRecusadas -and $tx.Count -eq 3)) "comandos enviados (codigos: $($codigos -join ','))=$enviou; 'ctrl+c' (k c, mods ctrl)=$okCtrlC; 'ctrl+shift+left'=$okCombo; 'alt+tab' sem mods=$okAntiga; q/meta/delete com ctrl+alt/win+r recusados (nada no log)=$semRecusadas; linhas de tecla no log=$($tx.Count) (esperado 3)"

  # o celular A fica conectado (sessao de controle) durante os casos da tela: so ele pode abrir o canal /tela/ws
  $seg = CelularSegurar $porta $pa.Chave $arq 300
  $pronto = Pronto $arq
  if (-not $pronto) { throw 'o celular de teste nao conectou' }

  # 15c.1 Ponteiro no canal real: cur 2 e p 1
  $r1 = CelularTela $porta $pa.Chave 'normal:0:960:4:ok:2:p'
  $p1 = $r1.Passos | Select-Object -First 1
  $ok1 = [bool]($r1.Fim -and $null -eq $r1.Fechou -and $null -ne $p1 -and $p1.Quadros -ge 1 -and $p1.Jpeg -and $p1.Pares -eq $p1.Quadros -and $p1.SemTexto -eq 0 -and $p1.Fora -eq 0)
  Resultado '15c.1 Ponteiro no canal real' $ok1 "cur 2 e p 1 por 4 s: $($p1.Quadros) quadros JPEG=$($p1.Jpeg); textos {p:[x,y]} recebidos=$($p1.Textos); quadros com texto logo antes=$($p1.Pares) (esperado $($p1.Quadros)); quadros sem texto=$($p1.SemTexto) (esperado 0); textos fora de 0..1=$($p1.Fora) (esperado 0); fechou=$($r1.Fechou)"

  # 15c.2 Sem regressao: sem cur e sem p, so quadros binarios
  $r2 = CelularTela $porta $pa.Chave 'normal:0:960:4:ok'
  $p2 = $r2.Passos | Select-Object -First 1
  $ok2 = [bool]($r2.Fim -and $null -eq $r2.Fechou -and $null -ne $p2 -and $p2.Quadros -ge 1 -and $p2.Jpeg -and $p2.Textos -eq 0)
  Resultado '15c.2 Pagina antiga sem regressao' $ok2 "sem cur e p por 4 s: $($p2.Quadros) quadros JPEG=$($p2.Jpeg); textos recebidos=$($p2.Textos) (esperado 0); fechou=$($r2.Fechou)"

  # cur invalido (4) e p 1 num canal so: o PC nao fecha o canal e continua mandando o ponteiro
  $r3 = CelularTela $porta $pa.Chave 'eco:0:640:3:ok:4:p'
  $p3 = $r3.Passos | Select-Object -First 1
  Resultado '15c.1b cur invalido' ([bool]($r3.Fim -and $null -eq $r3.Fechou -and $null -ne $p3 -and $p3.Quadros -ge 1 -and $p3.Pares -eq $p3.Quadros -and $p3.Fora -eq 0)) "cur 4 (vale 1) e p 1 em eco: $($p3.Quadros) quadros; com texto antes=$($p3.Pares); fora de 0..1=$($p3.Fora); fechou=$($r3.Fechou)"

  $errosLog = @(LogLines | Where-Object { $_ -match 'ERRO' })
  "     erros no log: $($errosLog.Count)"
  $errosLog | ForEach-Object { "     $_" }
  if ($errosLog.Count -gt 0) { $script:falhas++ }
  Cmd '--fechar'
}
catch {
  $script:falhas++
  "FALHA (excecao): $($_.Exception.Message)"
  "     em: $($_.InvocationInfo.PositionMessage -replace '\s+', ' ')"
}
finally {
  # nunca mata o celular de teste: pede para soltar e espera
  if ($seg) { try { CelularSoltar $seg $arq } catch {} }
  Remove-Item $arq, "$arq.parar", "$arq.err" -Force -ErrorAction SilentlyContinue
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
  if ((AppProcs).Count -ne 0) { $script:falhas++ }
  ReabrirAppDoUsuario
  'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
}
if ($falhas -gt 0) { exit 1 }
