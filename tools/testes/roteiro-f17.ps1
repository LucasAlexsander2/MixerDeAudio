# Roteiro de tela da Fase 17 (Troca e versao 2.0.0): so o que depende do app publicado de verdade; as regras puras estao nos testes unitarios.
#   17.1 Pagina: GET / = 200, text/html, no-store, cita /app/js/app.js e traz a CSP da D5 (script-src sem 'unsafe-inline');
#        GET /novo e /novo/ = 301 com "Location: /" e sem corpo (substitui o 14.1 do roteiro-f14).
#   17.2 Icone: GET /icone/<id> de um tocador de teste = 200 image/png com UM unico Cache-Control: private, max-age=3600.
#   17.x Sem regressao: rodar tambem roteiro-f14 (14.2 a 14.4), roteiro-f15a, roteiro-f15c e roteiro-f16 (-Parte a e -Parte b).
# Usa o build publicado em publicado\ (publique antes). Sem teclas nem cliques reais, sem trazer janela para a frente.
# SEGURANCA: o app roda SEMPRE com --teste-oculto --celular-local --teste-pc (sem janela nem icone, sem aviso do firewall).
# BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro; nada mata o roteiro no meio (esperas por condicao).
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
# GET cru (celular.exe cabecalhos): status, tipo, cache, corpo, X-Frame-Options, CSP, Location, quantos Cache-Control vieram e se cita /app/js/app.js.
function Cabecalhos17([int]$porta, [string]$caminho, [string]$chave = '-') {
  $linha = (& (CelularExe) cabecalhos $porta $caminho '-' $chave) | Select-Object -First 1
  $p = "$linha" -split '\|'
  [pscustomobject]@{
    Status = [int]$p[0]; Tipo = $p[1]; Cache = $p[2]; Bytes = [int]$p[5]; Xfo = $(if ($p.Count -gt 7) { $p[7] } else { '' }); Csp = $(if ($p.Count -gt 8) { $p[8] } else { '' })
    Location = $(if ($p.Count -gt 9) { $p[9] } else { '' }); NCache = $(if ($p.Count -gt 10) { [int]$p[10] } else { -1 }); CitaApp = $(if ($p.Count -gt 11) { $p[11] -eq '1' } else { $false })
  }
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

  # 17.1 Pagina em / e redirecionamento do /novo
  $cspEsperada = "frame-ancestors 'none'; default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' blob: data:; font-src 'self'; connect-src 'self' ws://127.0.0.1:$porta"
  $raiz = Cabecalhos17 $porta '/'
  $n1 = Cabecalhos17 $porta '/novo'
  $n2 = Cabecalhos17 $porta '/novo/'
  $okRaiz = $raiz.Status -eq 200 -and $raiz.Tipo -eq 'text/html; charset=utf-8' -and $raiz.Cache -eq 'no-store' -and $raiz.CitaApp -and $raiz.Csp -eq $cspEsperada -and $raiz.Xfo -eq 'DENY'
  $okNovo = $true
  foreach ($n in @($n1, $n2)) { if (-not ($n.Status -eq 301 -and $n.Location -eq '/' -and $n.Bytes -eq 0)) { $okNovo = $false } }
  Resultado '17.1 Pagina em / e /novo redireciona' ([bool]($okRaiz -and $okNovo)) "/ status=$($raiz.Status) tipo=$($raiz.Tipo) cache=$($raiz.Cache) cita app.js=$($raiz.CitaApp) CSP igual=$($raiz.Csp -eq $cspEsperada) XFO=$($raiz.Xfo); /novo status=$($n1.Status) Location='$($n1.Location)' corpo=$($n1.Bytes); /novo/ status=$($n2.Status) Location='$($n2.Location)' corpo=$($n2.Bytes)"
  if ($raiz.Csp -ne $cspEsperada) { "     CSP recebida: $($raiz.Csp)" }

  # 17.2 Icone com um unico Cache-Control (D8)
  $pa = CelularParear $porta '123456'
  if ($pa.Status -ne 200 -or -not $pa.Chave) { throw "o pareamento falhou (status $($pa.Status))" }
  $nomeTocador = 'MX-teste-f17'
  AssertTocador $nomeTocador
  $tocador = StartPlayer $nomeTocador
  $idIcone = $null
  WaitFor { $est = CelularEstado $porta $pa.Chave; $a = AppDoEstado $est $nomeTocador; if ($a -and $a.icone) { $script:idIcone = "$($a.icone)"; $true } else { $false } } 8 | Out-Null
  if (-not $script:idIcone) { Resultado '17.2 Icone com um Cache-Control' $false "o estado nao trouxe o icone do tocador '$nomeTocador'" }
  else {
    # o PC so serve icones com o hub ativo, ou seja, com um celular conectado: segura uma conexao durante o pedido
    $arqSeg = "$script:out\f17-segurar.txt"
    $seg = CelularSegurar $porta $pa.Chave $arqSeg 60
    WaitFor { (Test-Path $arqSeg) -and ((Get-Content $arqSeg -ErrorAction SilentlyContinue) -match "PRONTO") } 8 | Out-Null
    $ic = Cabecalhos17 $porta "/icone/$($script:idIcone)" $pa.Chave
    if ($ic.Status -ne 200) {
      # o tocador de teste pode nao ter icone (404): usa o icone de qualquer app do estado (so leitura, nada muda no app)
      $est = CelularEstado $porta $pa.Chave
      foreach ($a in @($est.apps | Where-Object { $_.icone })) {
        $t = Cabecalhos17 $porta "/icone/$($a.icone)" $pa.Chave
        if ($t.Status -eq 200) { $ic = $t; break }
      }
    }
    CelularSoltar $seg $arqSeg
    Resultado '17.2 Icone com um Cache-Control' ([bool]($ic.Status -eq 200 -and $ic.Tipo -eq 'image/png' -and $ic.NCache -eq 1 -and $ic.Cache -eq 'private, max-age=3600' -and $ic.Bytes -gt 0)) "status=$($ic.Status) tipo=$($ic.Tipo) Cache-Control='$($ic.Cache)' quantidade=$($ic.NCache) (esperado 1) bytes=$($ic.Bytes)"
  }
  StopPlayers

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
  StopPlayers
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
  if ((AppProcs).Count -ne 0) { $script:falhas++ }
  ReabrirAppDoUsuario
  'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
}
if ($falhas -gt 0) { exit 1 }
