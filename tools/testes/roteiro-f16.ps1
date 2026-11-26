# Roteiro de tela da Fase 16 (Seguranca): so o que depende do servidor real; as regras puras estao nos testes unitarios.
#   -Parte a (16a, Pareamento e rede):
#     16a.1 Host: GET /, /novo, /sessao e /ws com "Host: mal.exemplo:<porta>" (e com a porta errada) => 421; com o Host certo, como antes.
#     16a.3 Cabecalhos: X-Frame-Options: DENY e a CSP da D6 em / e /novo (e o X-Frame-Options tambem num 404).
#     16a.2 Corrida do limitador: 32 /parear errados ao mesmo tempo, segurando o corpo => no maximo 5 contam, o resto recebe 429.
#           (roda por ultimo: deixa o 127.0.0.1 bloqueado por 1 min no limitador do app, que e fechado no fim)
#   A parte b (16b) e escrita junto com a 16b.
# Usa o build publicado em publicado\ (publique antes). Sem teclas nem cliques reais, sem trazer janela para a frente.
# SEGURANCA: o app roda SEMPRE com --teste-oculto --celular-local --teste-pc (sem janela nem icone, sem aviso do firewall).
#   So o --celular-local liga os atalhos de teste da D8 (confirmacao automatica, janela de pareamento sempre aberta, codigo fixo).
# BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro; nada mata o roteiro no meio (esperas por condicao).
param([ValidateSet('a', 'b')][string]$Parte = 'a')
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
if ($Parte -ne 'a') { 'A parte b ainda nao foi escrita (vem com a 16b).'; exit 2 }
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
# GET cru com o Host escolhido (celular.exe hostget); devolve so o codigo HTTP.
function HostGet([int]$porta, [string]$caminho, [string]$hostHeader, [string]$chave = '-') {
  [int](& (CelularExe) hostget $porta $caminho $hostHeader $chave | Select-Object -First 1)
}
# status, X-Frame-Options e CSP de um GET cru (celular.exe cabecalhos, campos 0, 7 e 8).
function CabecalhosSeg([int]$porta, [string]$caminho) {
  $linha = (& (CelularExe) cabecalhos $porta $caminho '-') | Select-Object -First 1
  $p = "$linha" -split '\|'
  [pscustomobject]@{ Status = [int]$p[0]; Xfo = $(if ($p.Count -gt 7) { $p[7] } else { '' }); Csp = $(if ($p.Count -gt 8) { $p[8] } else { '' }) }
}

$difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false
try {
  StopAll
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"

  $porta = LigarFerramentas @()
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  $pa = CelularParear $porta '123456'
  if ($pa.Status -ne 200 -or -not $pa.Chave) { throw "o pareamento falhou (status $($pa.Status))" }
  $chave = $pa.Chave

  # 16a.1 Host (D1)
  $mal = "mal.exemplo:$porta"
  $caminhos = @('/', '/novo', '/sessao', '/ws')
  $recusados = @($caminhos | ForEach-Object { HostGet $porta $_ $mal $chave })
  $portaErrada = HostGet $porta '/' "127.0.0.1:$($porta + 1)"
  $certo = @($caminhos | ForEach-Object { HostGet $porta $_ "127.0.0.1:$porta" $chave })
  $local = HostGet $porta '/' "localhost:$porta"
  $esperadoCerto = @(200, 200, 204, 101)
  $ok1 = (@($recusados | Where-Object { $_ -eq 421 }).Count -eq 4) -and $portaErrada -eq 421 -and
         (($certo -join ',') -eq ($esperadoCerto -join ',')) -and $local -eq 200
  Resultado '16a.1 Host' $ok1 "Host mal.exemplo em /, /novo, /sessao e /ws=[$($recusados -join ',')] (esperado 421 nos quatro); porta errada=$portaErrada; Host 127.0.0.1=[$($certo -join ',')] (esperado $($esperadoCerto -join ',')); localhost=$local (200); log de recusa=$(LogCount 'Host recusado')"

  # 16a.3 Cabecalhos (D6)
  $cspEsperada = "frame-ancestors 'none'; default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; font-src 'self'; connect-src 'self' ws://127.0.0.1:$porta"
  $raiz = CabecalhosSeg $porta '/'
  $novo = CabecalhosSeg $porta '/novo'
  $nada = CabecalhosSeg $porta '/nao-existe'
  $ok3 = $raiz.Status -eq 200 -and $raiz.Xfo -eq 'DENY' -and $raiz.Csp -eq $cspEsperada -and
         $novo.Status -eq 200 -and $novo.Xfo -eq 'DENY' -and $novo.Csp -eq $cspEsperada -and
         $nada.Status -eq 404 -and $nada.Xfo -eq 'DENY'
  Resultado '16a.3 Cabecalhos' ([bool]$ok3) "/ status=$($raiz.Status) XFO=$($raiz.Xfo) CSP igual=$($raiz.Csp -eq $cspEsperada); /novo status=$($novo.Status) XFO=$($novo.Xfo) CSP igual=$($novo.Csp -eq $cspEsperada); 404 status=$($nada.Status) XFO=$($nada.Xfo)"
  if ($raiz.Csp -ne $cspEsperada) { "     CSP recebida: $($raiz.Csp)" }

  # 16a.2 Corrida do limitador (D2): 32 pedidos errados ao mesmo tempo, corpo seguro ate todos conectarem (por ultimo)
  $saida = (& (CelularExe) rajada $porta 32 '000000' | Select-Object -First 1)
  $m = [regex]::Match("$saida", '401=(\d+) 429=(\d+) outros=(\d+)')
  $a = if ($m.Success) { [int]$m.Groups[1].Value } else { -1 }
  $b = if ($m.Success) { [int]$m.Groups[2].Value } else { -1 }
  $o = if ($m.Success) { [int]$m.Groups[3].Value } else { -1 }
  $ok2 = $m.Success -and $a -ge 1 -and $a -le 5 -and $o -eq 0 -and ($a + $b) -eq 32
  $bloq = (LogCount 'celular: pareamento bloqueado por 60 s') -ge 1
  Resultado '16a.2 Corrida do limitador' ([bool]($ok2 -and $bloq)) "32 pedidos errados em paralelo: 401=$a (conta; esperado ate 5) 429=$b (esperado o resto) outros=$o; log de bloqueio=$bloq"

  $errosLog = @(LogLines | Where-Object { $_ -match 'ERRO' })
  "     erros no log: $($errosLog.Count)"
  $errosLog | ForEach-Object { "     $_" }
  if ($errosLog.Count -gt 0) { $script:falhas++ }
}
catch {
  $script:falhas++
  "FALHA (excecao): $($_.Exception.Message)"
  "     em: $($_.InvocationInfo.PositionMessage -replace '\s+', ' ')"
}
finally {
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
  if ((AppProcs).Count -ne 0) { $script:falhas++ }
  ReabrirAppDoUsuario
  'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
}
if ($falhas -gt 0) { exit 1 }
