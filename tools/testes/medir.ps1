# Medição de memória e CPU do app publicado (Fase 1, caso 1.4).
# Uso: .\tools\testes\medir.ps1 [-SemAparar]   (a janela abre e fecha só por linha de comando, sem foco)
# Cada rodada: 10 s parado -> memória; 30 s parado -> CPU média; 20 ciclos (abrir sem foco, 1 s, fechar, 3 s) -> memória após os ciclos 5 e 20.
param([switch]$SemAparar)
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'

function Mem {
  # Os nomes dos contadores do Windows são traduzidos (pt-BR); o WMI mantém os nomes em inglês.
  $p = MainProc; $p.Refresh()
  $raw = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$($p.Id)"
  [pscustomobject]@{ WsPrivMB = [math]::Round($raw.WorkingSetPrivate / 1MB, 1); PrivBytesMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); WsMB = [math]::Round($p.WorkingSet64 / 1MB, 1) }
}

try {
  StopAll
  $label = if ($SemAparar) { 'sem-aparar' } else { 'normal' }
  if ($SemAparar) { StartApp @('--sem-aparar') } else { StartApp }
  $p = MainProc
  Start-Sleep -Seconds 10
  $m10 = Mem
  # CPU: 30 s parado
  $p.Refresh(); $cpu0 = $p.TotalProcessorTime.TotalSeconds; $t0 = Get-Date
  Start-Sleep -Seconds 30
  $p.Refresh(); $cpu1 = $p.TotalProcessorTime.TotalSeconds; $t1 = Get-Date
  $cpuPct = [math]::Round((($cpu1 - $cpu0) / (($t1 - $t0).TotalSeconds * [Environment]::ProcessorCount)) * 100, 3)
  # Ciclos
  $m5 = $null
  for ($i = 1; $i -le 20; $i++) {
    Cmd "--abrir-sem-foco"
    Start-Sleep -Seconds 1
    Cmd '--fechar'
    Start-Sleep -Seconds 3
    if ($i -eq 5) { $m5 = Mem }
  }
  $m20 = Mem
  [pscustomobject]@{
    Rodada = $label
    'Parado10s_WsPriv_MB' = $m10.WsPrivMB; 'Parado10s_PrivBytes_MB' = $m10.PrivBytesMB; 'Parado10s_Ws_MB' = $m10.WsMB
    'CPU_parado_%' = $cpuPct
    'Ciclo5_WsPriv_MB' = $m5.WsPrivMB; 'Ciclo5_PrivBytes_MB' = $m5.PrivBytesMB
    'Ciclo20_WsPriv_MB' = $m20.WsPrivMB; 'Ciclo20_PrivBytes_MB' = $m20.PrivBytesMB
    'Crescimento5a20_WsPriv_MB' = [math]::Round($m20.WsPrivMB - $m5.WsPrivMB, 1)
  } | Format-List
} finally {
  StopAll
}
