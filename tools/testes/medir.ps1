# Medição de memória e CPU do app publicado (Fase 1, caso 1.4; Fase 2, caso 2.9 com -ComSom).
# Uso: .\tools\testes\medir.ps1 [-SemAparar] [-ComSom]   (a janela abre e fecha só por linha de comando, sem foco)
# -ComSom: dois tocadores de teste (silêncio) tocam durante a rodada inteira; a janela passa a ler e soltar as sessões em cada ciclo.
# -Ciclos N (padrão 20; Fase 3 usa 50): quantidade de ciclos. Se N > 20, mede também após o ciclo N.
# Cada rodada: 10 s parado -> memória; 30 s parado -> CPU média; N ciclos (abrir sem foco, 1 s, fechar, 3 s) -> memória e handles após os ciclos 5, 20 e N.
param([switch]$SemAparar, [switch]$ComSom, [int]$Ciclos = 20)
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'

function Mem {
  # Os nomes dos contadores do Windows são traduzidos (pt-BR); o WMI mantém os nomes em inglês.
  $p = MainProc; $p.Refresh()
  $raw = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$($p.Id)"
  [pscustomobject]@{ WsPrivMB = [math]::Round($raw.WorkingSetPrivate / 1MB, 1); PrivBytesMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); WsMB = [math]::Round($p.WorkingSet64 / 1MB, 1); Handles = $p.HandleCount }
}

try {
  StopAll
  StopPlayers
  $label = $(if ($SemAparar) { 'sem-aparar' } else { 'normal' }) + $(if ($ComSom) { '+com-som' } else { '' })
  if ($SemAparar) { StartApp @('--sem-aparar') } else { StartApp }
  $p = MainProc
  if ($ComSom) { PrepararFerramentas; StartPlayer 'MX-teste A' | Out-Null; StartPlayer 'MX-teste B' -B | Out-Null }
  Start-Sleep -Seconds 10
  $m10 = Mem
  # CPU: 30 s parado
  $p.Refresh(); $cpu0 = $p.TotalProcessorTime.TotalSeconds; $t0 = Get-Date
  Start-Sleep -Seconds 30
  $p.Refresh(); $cpu1 = $p.TotalProcessorTime.TotalSeconds; $t1 = Get-Date
  $cpuPct = [math]::Round((($cpu1 - $cpu0) / (($t1 - $t0).TotalSeconds * [Environment]::ProcessorCount)) * 100, 3)
  # Ciclos
  $m5 = $null; $mN = $null
  for ($i = 1; $i -le $Ciclos; $i++) {
    Cmd "--abrir-sem-foco"
    Start-Sleep -Seconds 1
    Cmd '--fechar'
    Start-Sleep -Seconds 3
    if ($i -eq 5) { $m5 = Mem }
    if ($i -eq 20) { $m20 = Mem }
    if ($i -eq $Ciclos -and $Ciclos -gt 20) { $mN = Mem }
  }
  if (-not $m20) { $m20 = Mem }
  $saida = [ordered]@{
    Rodada = $label
    'Parado10s_WsPriv_MB' = $m10.WsPrivMB; 'Parado10s_PrivBytes_MB' = $m10.PrivBytesMB; 'Parado10s_Ws_MB' = $m10.WsMB
    'CPU_parado_%' = $cpuPct
    'Ciclo5_WsPriv_MB' = $m5.WsPrivMB; 'Ciclo5_PrivBytes_MB' = $m5.PrivBytesMB
    'Ciclo20_WsPriv_MB' = $m20.WsPrivMB; 'Ciclo20_PrivBytes_MB' = $m20.PrivBytesMB
    'Crescimento5a20_WsPriv_MB' = [math]::Round($m20.WsPrivMB - $m5.WsPrivMB, 1)
    'Handles_parado' = $m10.Handles; 'Handles_ciclo5' = $m5.Handles; 'Handles_ciclo20' = $m20.Handles
  }
  if ($mN) {
    $saida["Ciclo${Ciclos}_WsPriv_MB"] = $mN.WsPrivMB; $saida["Ciclo${Ciclos}_PrivBytes_MB"] = $mN.PrivBytesMB; $saida["Handles_ciclo$Ciclos"] = $mN.Handles
    $saida["Crescimento20a${Ciclos}_PrivBytes_MB"] = [math]::Round($mN.PrivBytesMB - $m20.PrivBytesMB, 1)
  }
  [pscustomobject]$saida | Format-List
} finally {
  StopPlayers
  StopAll
  "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
}
