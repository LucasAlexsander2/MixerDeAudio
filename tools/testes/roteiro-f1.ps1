# Roteiro de tela da Fase 1 (casos 1.1, 1.2, 1.3 e 1.5; o 1.4 é o medir.ps1).
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente.
# Termina o app sempre, também em caso de erro.
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}

try {
  StopAll
  Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue

  # 1.1 Repouso
  StartApp
  Start-Sleep -Seconds 3
  $vivo = (AppProcs).Count -eq 1
  $janelas = @(Windows).Count
  Resultado '1.1 Repouso' ($vivo -and $janelas -eq 0 -and (LogCount 'iniciado') -ge 1) "processos=$((AppProcs).Count) janelas visiveis=$janelas log iniciado=$(LogCount 'iniciado')"

  # 1.2 Abrir
  $fgAntes = [MW]::GetForegroundWindow()
  Cmd '--abrir-sem-foco'
  $achou = WaitFor { @(Windows).Count -ge 1 } 2
  Start-Sleep -Milliseconds 700
  $h = (Windows | Select-Object -First 1)
  $fgDepois = [MW]::GetForegroundWindow()
  $procs = (AppProcs).Count
  $r = New-Object MW+RECT; [MW]::GetWindowRect($h, [ref]$r) | Out-Null
  $w = $r.R - $r.L; $ht = $r.B - $r.T
  $scale = [MW]::GetDpiForWindow($h) / 96.0
  $wa = New-Object MW+RECT; [MW]::SystemParametersInfo(0x30, 0, [ref]$wa, 0) | Out-Null
  $margem = [math]::Round(12 * $scale)
  $dentro = $r.L -ge $wa.L -and $r.T -ge $wa.T -and $r.R -le $wa.R -and $r.B -le $wa.B
  $encostado = [math]::Abs($wa.R - $margem - $r.R) -le 12 -and [math]::Abs($wa.B - $margem - $r.B) -le 12
  $larguraOk = [math]::Abs($w - 420 * $scale) -le 2
  $root = FindById $h 'FlyoutRoot'; $title = FindById $h 'DeviceList'  # Fase 3: o título saiu (D14); o topo agora é a fileira de saídas
  $semFoco = $fgAntes -eq $fgDepois
  $shot = Shot $h 'f1-janela'
  $ok = $achou -and $procs -eq 1 -and $root -and $title -and $larguraOk -and $dentro -and $encostado -and $semFoco -and (LogCount 'janela aberta') -ge 1
  Resultado '1.2 Abrir' ([bool]$ok) ("achou=$achou processos=$procs FlyoutRoot=$([bool]$root) DeviceList=$([bool]$title) rect=($($r.L),$($r.T))-($($r.R),$($r.B)) ${w}x${ht} escala=$scale larguraOk=$larguraOk dentro=$dentro encostado=$encostado areaTrabalho=($($wa.L),$($wa.T))-($($wa.R),$($wa.B)) foco igual=$semFoco log=$(LogCount 'janela aberta') captura=$shot")

  # 1.3 Fechar
  Cmd '--fechar'
  $fechou = WaitFor { @(Windows).Count -eq 0 } 1
  Resultado '1.3 Fechar' ($fechou -and (LogCount 'janela fechada \(comando\)') -ge 1) "fechou em ate 1 s=$fechou log=$(LogCount 'janela fechada \(comando\)')"

  # 1.5 Sair
  $sw = [Diagnostics.Stopwatch]::StartNew()
  Cmd '--sair'
  $saiu = WaitFor { (AppProcs).Count -eq 0 } 5
  Resultado '1.5 Sair' ($saiu -and (LogCount 'saindo') -ge 1) "terminou=$saiu em $([math]::Round($sw.Elapsed.TotalSeconds,1)) s log saindo=$(LogCount 'saindo')"
}
finally {
  StopAll
  "processos restantes: $((AppProcs).Count)"
}
if ($falhas -gt 0) { "FALHAS: $falhas"; exit 1 } else { 'Todos os casos passaram.' }
