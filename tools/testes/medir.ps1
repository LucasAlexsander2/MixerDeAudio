# Medição de memória e CPU do app publicado (Fase 1, caso 1.4; Fase 2, caso 2.9 com -ComSom).
# Uso: .\tools\testes\medir.ps1 [-SemAparar] [-ComSom]   (a janela abre e fecha só por linha de comando, sem foco)
# -ComSom: dois tocadores de teste (silêncio) tocam durante a rodada inteira; a janela passa a ler e soltar as sessões em cada ciclo.
# -Ciclos N (padrão 20; Fase 3 usa 50): quantidade de ciclos. Se N > 20, mede também após o ciclo N.
# -ComServidor (Fase 6): liga o servidor do celular em 127.0.0.1 (--celular-local) e mede como as outras rodadas (janela abre e fecha), sem celular conectado.
# -ComCelular (Fase 6): igual, mas cada ciclo conecta e desconecta o celular de teste (celular.cs) em vez de abrir e fechar a janela.
# -TodosGrupos (Fase 9d, caso 9.16): liga os 8 grupos de ferramentas do PC no config.json de teste (o app roda com --teste-pc, so grava no log).
# -NaoReabrir: no fim nao reabre o app do usuario (para encadear varias rodadas; reabra no fim).
#   As duas guardam e devolvem o config.json (BackupConfig/RestoreConfig) e nao combinam com -SemAparar.
# Cada rodada: 10 s parado -> memória; 30 s parado -> CPU média; N ciclos (abrir sem foco, 1 s, fechar, 3 s) -> memória e handles após os ciclos 5, 20 e N.
param([switch]$SemAparar, [switch]$ComSom, [int]$Ciclos = 20, [switch]$ComServidor, [switch]$ComCelular, [switch]$TodosGrupos, [switch]$NaoReabrir)
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'

function Mem {
  # Os nomes dos contadores do Windows são traduzidos (pt-BR); o WMI mantém os nomes em inglês.
  $p = MainProc; $p.Refresh()
  $raw = Get-CimInstance Win32_PerfRawData_PerfProc_Process -Filter "IDProcess=$($p.Id)"
  [pscustomobject]@{ WsPrivMB = [math]::Round($raw.WorkingSetPrivate / 1MB, 1); PrivBytesMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); WsMB = [math]::Round($p.WorkingSet64 / 1MB, 1); Handles = $p.HandleCount }
}

$backupFeito = $false; $porta = $null; $chave = ''
try {
  StopAll
  StopPlayers
  $label = $(if ($SemAparar) { 'sem-aparar' } else { 'normal' }) + $(if ($ComSom) { '+com-som' } else { '' }) + $(if ($ComCelular) { '+celular' } elseif ($ComServidor) { '+servidor' } else { '' }) + $(if ($TodosGrupos) { '+8grupos' } else { '' })
  if ($ComServidor -or $ComCelular) {
    if ($SemAparar) { throw '-ComServidor e -ComCelular nao combinam com -SemAparar' }
    $gruposMedir = if ($TodosGrupos) { @('energia', 'monitores', 'entrada', 'tela', 'janelas', 'atalhos', 'transferencia', 'estado') } else { @() }
    PrepararFerramentas
    BackupConfig
    $backupFeito = $true
    $porta = LigarCelularTeste '123456' $gruposMedir
    if (-not $porta) { throw 'o servidor do celular nao ligou' }
    if ($ComCelular) { $chave = (CelularParear $porta '123456').Chave; if (-not $chave) { throw 'o pareamento falhou' } }
  } elseif ($SemAparar) { StartApp @('--sem-aparar') } else { StartApp }
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
    if ($ComCelular) {
      CelularCiclos $porta $chave 1 | Out-Null   # conecta, espera o estado e desconecta
      Start-Sleep -Seconds 4
    } else {
      Cmd "--abrir-sem-foco"
      Start-Sleep -Seconds 1
      Cmd '--fechar'
      Start-Sleep -Seconds 3
    }
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
  if ($backupFeito) { $dif = @(RestoreConfig); "config.json e registro restaurados; diferencas=[$($dif -join '; ')]" }
  "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos)"
  if (-not $NaoReabrir) { ReabrirAppDoUsuario }
}
