# Roteiro de tela da Fase 16 (Seguranca): so o que depende do servidor real; as regras puras estao nos testes unitarios.
#   -Parte a (16a, Pareamento e rede):
#     16a.1 Host: GET /, /novo, /sessao e /ws com "Host: mal.exemplo:<porta>" (e com a porta errada) => 421; com o Host certo, como antes.
#     16a.3 Cabecalhos: X-Frame-Options: DENY e a CSP da D6 em / e /novo (e o X-Frame-Options tambem num 404).
#     16a.2 Corrida do limitador: 32 /parear errados ao mesmo tempo, segurando o corpo => no maximo 5 contam, o resto recebe 429.
#           (roda por ultimo: deixa o 127.0.0.1 bloqueado por 1 min no limitador do app, que e fechado no fim)
#   -Parte b (16b, Avisos e celulares):
#     16b.1 Arquivo real: enviar "foto<U+202E>gpj.exe" => chega como "fotogpj.exe.txt" (sem o caractere Cf), com o aviso do balao.
#     16b.2 Energia "agora" com --teste-pc: "desligar" com seg 0 vira 10 s (janelinha com Cancelar, pc.energia com ~10 s); cancelar =>
#           nada de "teste-pc: energia desligar" no log (nem depois dos 10 s) e a linha "energia cancelada" aparece.
# Usa o build publicado em publicado\ (publique antes). Sem teclas nem cliques reais, sem trazer janela para a frente.
# SEGURANCA: o app roda SEMPRE com --teste-oculto --celular-local --teste-pc (sem janela nem icone, sem aviso do firewall).
#   So o --celular-local liga os atalhos de teste da D8 (confirmacao automatica, janela de pareamento sempre aberta, codigo fixo).
# BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro; nada mata o roteiro no meio (esperas por condicao).
param([ValidateSet('a', 'b')][string]$Parte = 'a')
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
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

  # a parte b liga os grupos transferencia (16b.1) e energia (16b.2); a parte a nao precisa de nenhum
  $porta = LigarFerramentas $(if ($Parte -eq 'b') { @('transferencia', 'energia') } else { @() })
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  $pa = CelularParear $porta '123456'
  if ($pa.Status -ne 200 -or -not $pa.Chave) { throw "o pareamento falhou (status $($pa.Status))" }
  $chave = $pa.Chave

  if ($Parte -eq 'a') {
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

  }

  if ($Parte -eq 'b') {
    AssertModoTestePc
    $dirR = "$script:out\recebidos"
    Remove-Item $dirR -Recurse -Force -ErrorAction SilentlyContinue

    # 16b.1 Arquivo real (D12): o RLO (U+202E) some, e a extensao .exe ganha .txt, com o aviso no balao
    $src = "$script:out\f16-envio.bin"
    [IO.File]::WriteAllBytes($src, [byte[]](1..200 | ForEach-Object { $_ % 256 }))
    $rlo = [string][char]0x202E
    $e1 = CelularArquivo $porta $chave $src ('foto' + $rlo + 'gpj.exe')
    $nomeGravado = if ($e1.Json) { "$($e1.Json.nome)" } else { '' }
    $semCf = -not ($nomeGravado -match '\p{Cf}')
    $arquivos = @(Get-ChildItem $dirR -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    $soUm = ($arquivos.Count -eq 1) -and ($arquivos[0] -ceq 'fotogpj.exe.txt')
    $balao = LogCount 'celular: balao: Arquivo recebido'
    $ok1b = $e1.Status -eq 200 -and $nomeGravado -ceq 'fotogpj.exe.txt' -and $semCf -and $soUm -and $balao -ge 1
    Resultado '16b.1 Arquivo real' ([bool]$ok1b) "status=$($e1.Status) (200); nome devolvido='$nomeGravado' (esperado fotogpj.exe.txt) sem Cf=$semCf; arquivos na pasta de recebidos=[$($arquivos -join ', ')] (so fotogpj.exe.txt)=$soUm; balao 'Arquivo recebido' no log x$balao"
    Remove-Item $src -Force -ErrorAction SilentlyContinue
    Remove-Item $dirR -Recurse -Force -ErrorAction SilentlyContinue

    # 16b.2 Energia "agora" (D13): seg 0 vira 10 s; cancelar antes disso nao executa nada, nem depois dos 10 s
    $t0 = EpochMs
    $rc = CelularFerramenta $porta $chave '{"t":"energia","acao":"desligar","seg":0}'
    $okAgenda = WaitFor { (LogCount 'celular: energia desligar agora \(em 10 s\)') -ge 1 } 3
    $es = CelularEstado $porta $chave
    $en = if ($es -and $es.pc) { $es.pc.energia } else { $null }
    $okEstado = [bool]($null -ne $en -and $en.acao -eq 'desligar' -and [double]$en.rest -ge 5 -and [double]$en.rest -le 10)
    $okJanela = WaitFor { (LogCount 'janelinha: pergunta mostrada') -ge 1 } 3
    $antesDeCancelar = @(LogTestePc '^energia desligar$').Count
    $rc2 = CelularFerramenta $porta $chave '{"t":"energiaCancelar"}'
    $ec = CelularEstado $porta $chave
    $okSumiu = [bool]($null -ne $ec.pc -and $null -eq $ec.pc.energia)
    $okCancel = WaitFor { (LogCount 'celular: energia cancelada') -ge 1 } 3
    Start-Sleep -Seconds 11   # passa dos 10 s do "agora": o plano cancelado nao pode executar
    $depois = @(LogTestePc '^energia desligar$').Count
    $ok2b = $rc -eq 0 -and $rc2 -eq 0 -and $okAgenda -and $okEstado -and $okJanela -and $antesDeCancelar -eq 0 -and $okSumiu -and $okCancel -and $depois -eq 0
    Resultado '16b.2 Energia agora (teste-pc)' ([bool]$ok2b) "desligar com seg 0: enviado=$($rc -eq 0), log 'energia desligar agora (em 10 s)'=$okAgenda, pc.energia desligar com rest de 5 a 10 s=$okEstado (rest=$(if ($en) { $en.rest } else { '?'})), janelinha mostrada=$okJanela; 'teste-pc: energia desligar' antes de cancelar=$antesDeCancelar (0); cancelamento enviado=$($rc2 -eq 0), pc.energia sumiu=$okSumiu, log 'energia cancelada'=$okCancel; 11 s depois, 'teste-pc: energia desligar' x$depois (0)"
  }

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
