# Auxiliares dos roteiros de tela do Mixer de Áudio.
# Regras: nada de teclas nem cliques reais, nada de trazer janela para a frente (o usuário pode estar jogando).
# Só linha de comando do app, UI Automation, GetWindowRect e PrintWindow.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
Add-Type @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices; using System.Text;
public static class MW {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint a, uint b, out RECT r, uint f);
  public struct RECT { public int L, T, R, B; }
  // Janelas visíveis de nível superior de um processo.
  public static List<IntPtr> VisibleOf(uint pid) {
    var list = new List<IntPtr>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid && IsWindowVisible(h)) list.Add(h); return true; }, IntPtr.Zero);
    return list;
  }
}
"@
[MW]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null  # coordenadas em pixels físicos
$script:root = (Resolve-Path "$PSScriptRoot\..\..").Path
$script:exe = "$script:root\publicado\MixerDeAudio.exe"
$script:out = "$env:TEMP\mixer-check"
$script:log = "$env:LOCALAPPDATA\MixerDeAudio\logs\mixer.log"
$AE = [Windows.Automation.AutomationElement]
$TS = [Windows.Automation.TreeScope]
New-Item -ItemType Directory -Force $script:out | Out-Null

function AppProcs { @(Get-Process MixerDeAudio -ErrorAction SilentlyContinue) }

# Abre a primeira cópia (sem argumento por padrão) e espera o processo existir.
function StartApp([string[]]$argList = @()) {
  if ($argList.Count -gt 0) { Start-Process $script:exe -ArgumentList $argList | Out-Null } else { Start-Process $script:exe | Out-Null }
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt 10 -and (AppProcs).Count -eq 0) { Start-Sleep -Milliseconds 100 }
}
# Manda um comando por uma segunda cópia (ela redireciona e sai).
function Cmd([string]$arg) { Start-Process $script:exe -ArgumentList $arg -Wait | Out-Null }
function MainProc { AppProcs | Select-Object -First 1 }
function Windows { $p = MainProc; if ($p) { [MW]::VisibleOf([uint32]$p.Id) } else { @() } }
function WaitFor([scriptblock]$cond, [double]$seconds) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $seconds) { if (& $cond) { return $true }; Start-Sleep -Milliseconds 50 }
  [bool](& $cond)
}
# Encerra o app sempre: primeiro --sair, depois Stop-Process se sobrar algo.
function StopAll {
  if ((AppProcs).Count -gt 0) {
    try { Start-Process $script:exe -ArgumentList '--sair' -Wait | Out-Null } catch {}
    WaitFor { (AppProcs).Count -eq 0 } 5 | Out-Null
  }
  AppProcs | Stop-Process -Force -ErrorAction SilentlyContinue
}
function LogLines { if (Test-Path $script:log) { Get-Content $script:log -Encoding utf8 } else { @() } }
function LogCount([string]$pat) { @(LogLines | Where-Object { $_ -match $pat }).Count }
function FindById([IntPtr]$h, [string]$id) {
  $root = $AE::FromHandle($h)
  $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty), $id
  $root.FindFirst($TS::Descendants, $c)
}
# Captura sem trazer a janela para a frente (PrintWindow).
function Shot([IntPtr]$h, [string]$name) {
  $r = New-Object MW+RECT; [MW]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T); $g = [Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc(); [MW]::PrintWindow($h, $hdc, 2) | Out-Null; $g.ReleaseHdc($hdc)
  $path = "$script:out\$name.png"; $bmp.Save($path); $g.Dispose(); $bmp.Dispose(); $path
}

# ---------------- Fase 2: sessões de áudio, tocadores de teste e linhas da janela ----------------
# Segurança: o volume e o mudo só mudam em tocadores de teste (nome "MX-teste ...", tocar.cs). Os apps reais do usuário
# só são lidos. Os tocadores tocam silêncio.
$script:ferr = "$script:out\ferr"
$script:players = New-Object System.Collections.ArrayList

# Publica tocar.cs, a cópia tocar-b.cs (outro .exe) e sessoes.cs em %TEMP%\mixer-check\ferr (só se mudaram).
function PrepararFerramentas {
  New-Item -ItemType Directory -Force $script:ferr | Out-Null
  Copy-Item "$PSScriptRoot\tocar.cs" "$script:ferr\tocar-b.cs" -Force
  foreach ($nome in 'tocar', 'tocar-b', 'sessoes') {
    $fonte = if ($nome -eq 'tocar-b') { "$script:ferr\tocar-b.cs" } else { "$PSScriptRoot\$nome.cs" }
    $dest = "$script:ferr\$nome-bin"
    $exe = "$dest\$nome.exe"
    if ((Test-Path $exe) -and (Get-Item $exe).LastWriteTime -gt (Get-Item $fonte).LastWriteTime) { continue }
    if ($nome -ne 'tocar-b') { Copy-Item $fonte "$script:ferr\$nome.cs" -Force; $fonte = "$script:ferr\$nome.cs" }
    $r = & dotnet publish $fonte -o $dest -v q 2>&1
    if (-not (Test-Path $exe)) { throw "Falha ao publicar $nome`: $($r | Select-Object -Last 5)" }
  }
}

# Lista as sessões de todas as saídas ativas (pid, nome, estado, volume, mudo).
function Sessoes {
  & "$script:ferr\sessoes-bin\sessoes.exe" listar | ForEach-Object {
    $p = $_ -split '\|'
    if ($p.Count -ge 5) {
      [pscustomobject]@{ Pid = [int]$p[0]; Nome = $p[1]; Estado = $p[2]; Volume = [double]::Parse($p[3], [Globalization.CultureInfo]::InvariantCulture); Mudo = ($p[4] -eq 'sim') }
    }
  }
}
function SessionOf([int]$procId) { Sessoes | Where-Object { $_.Pid -eq $procId } | Select-Object -First 1 }
# Muda a sessão de um tocador de teste de fora do app (o sessoes.exe recusa qualquer outro processo).
function DefinirSessao([int]$procId, [string]$volume, [string]$mudo = '') {
  $a = @('definir', "$procId", $volume); if ($mudo) { $a += $mudo }
  & "$script:ferr\sessoes-bin\sessoes.exe" @a | Out-Null
  $LASTEXITCODE
}

# Inicia um tocador de teste (silêncio) e espera a sessão dele aparecer com o nome. -B usa o .exe alternativo (tocar-b).
function StartPlayer([string]$nome, [switch]$B, [int]$segundos = 900) {
  $exeName = if ($B) { 'tocar-b' } else { 'tocar' }
  $p = Start-Process "$script:ferr\$exeName-bin\$exeName.exe" -ArgumentList @('--nome', "`"$nome`"", '--segundos', "$segundos") -PassThru -WindowStyle Hidden
  [void]$script:players.Add($p)
  WaitFor { $s = SessionOf $p.Id; $s -and $s.Nome -eq $nome } 8 | Out-Null
  $p
}
# Encerra só os tocadores iniciados pelo roteiro.
function StopPlayers {
  foreach ($p in $script:players) { try { if (-not $p.HasExited) { $p.Kill() } } catch {} }
  $script:players.Clear()
}
function PlayersAbertos { @(Get-Process tocar, tocar-b -ErrorAction SilentlyContinue).Count }

# Linhas da janela (UI Automation, sem clique): AutomationElement de cada SessionRow.
function RowElements([IntPtr]$h) {
  $root = $AE::FromHandle($h)
  $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty), 'SessionRow'
  @($root.FindAll($TS::Descendants, $c))
}
function Rows([IntPtr]$h) { @(RowElements $h | ForEach-Object { $_.Current.Name }) }
function RowElement([IntPtr]$h, [string]$nome) { RowElements $h | Where-Object { $_.Current.Name -eq $nome } | Select-Object -First 1 }
function RowChild($row, [string]$id) {
  $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty), $id
  $row.FindFirst($TS::Descendants, $c)
}
function RowVolume([IntPtr]$h, [string]$nome) {
  $row = RowElement $h $nome; if (-not $row) { return $null }
  (RowChild $row 'SessionVolume').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value
}
function RowValueText([IntPtr]$h, [string]$nome) {
  $row = RowElement $h $nome; if (-not $row) { return $null }
  (RowChild $row 'SessionValue').Current.Name
}
function RowMuteName([IntPtr]$h, [string]$nome) {
  $row = RowElement $h $nome; if (-not $row) { return $null }
  (RowChild $row 'SessionMute').Current.Name
}
function AssertTocador([string]$nome) { if (-not $nome.StartsWith('MX-teste')) { throw "RECUSADO: '$nome' nao e um tocador de teste." } }
# RangeValuePattern.SetValue: sem clique. Só em tocador de teste.
function SetRowVolume([IntPtr]$h, [string]$nome, [double]$valor) {
  AssertTocador $nome
  $row = RowElement $h $nome; if (-not $row) { throw "linha '$nome' nao encontrada" }
  (RowChild $row 'SessionVolume').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue($valor)
}
# TogglePattern.Toggle: sem clique. Só em tocador de teste.
function ToggleRowMute([IntPtr]$h, [string]$nome) {
  AssertTocador $nome
  $row = RowElement $h $nome; if (-not $row) { throw "linha '$nome' nao encontrada" }
  (RowChild $row 'SessionMute').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
}

# ---------------- Fase 3: saídas de áudio e volume geral ----------------
# Segurança: nunca troca a saída padrão e nunca mexe no mudo geral. O volume geral só recebe o toque mínimo
# (até 2 pontos de diferença do valor atual), e quem muda tem que devolver o valor original num bloco finally.

# Saídas ativas pelo sessoes.exe: Id, Curto, Completo, Formato, Padrao, Volume (0 a 1), Mudo.
function Devices {
  & "$script:ferr\sessoes-bin\sessoes.exe" dispositivos | ForEach-Object {
    $p = $_ -split '\|'
    if ($p.Count -ge 7) {
      [pscustomobject]@{ Id = $p[0]; Curto = $p[1]; Completo = $p[2]; Formato = [int]$p[3]; Padrao = ($p[4] -eq 'sim')
        Volume = [double]::Parse($p[5], [Globalization.CultureInfo]::InvariantCulture); Mudo = ($p[6] -eq 'sim') }
    }
  }
}
# Muda o volume geral de fora do app (o sessoes.exe recusa mais de 0,02 de diferença). Devolve o código de saída.
function DefinirGeral([string]$volume) {
  & "$script:ferr\sessoes-bin\sessoes.exe" geral $volume | Out-Null
  $LASTEXITCODE
}
# Botões das saídas na janela (UI Automation, sem clique): Nome (completo), Texto (nome curto) e Ativo.
function DeviceButtons([IntPtr]$h) {
  $root = $AE::FromHandle($h)
  $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty), 'DeviceButton'
  @($root.FindAll($TS::Descendants, $c)) | ForEach-Object {
    $texto = $_.FindFirst($TS::Descendants, (New-Object Windows.Automation.PropertyCondition ($AE::ControlTypeProperty), ([Windows.Automation.ControlType]::Text)))
    [pscustomobject]@{
      Nome = $_.Current.Name
      Texto = if ($texto) { $texto.Current.Name } else { '' }
      Ativo = ($_.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On)
    }
  }
}
# Volume geral mostrado pela janela (0 a 100) e estado do mudo (só leitura).
function MasterValue([IntPtr]$h) {
  $e = FindById $h 'MasterVolume'; if (-not $e) { return $null }
  $e.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value
}
function MasterMuted([IntPtr]$h) {
  $e = FindById $h 'MasterMute'; if (-not $e) { return $null }
  $e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On
}
function MasterText([IntPtr]$h) { $e = FindById $h 'MasterValue'; if ($e) { $e.Current.Name } else { $null } }
# RangeValuePattern.SetValue no volume geral, sem clique. Só aceita até ±2 em relação ao valor atual (toque mínimo).
function SetMasterValue([IntPtr]$h, [double]$valor) {
  $e = FindById $h 'MasterVolume'; if (-not $e) { throw 'volume geral nao encontrado' }
  $pat = $e.GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern)
  if ([math]::Abs($valor - $pat.Current.Value) -gt 2) { throw "RECUSADO: SetMasterValue so aceita ate 2 pontos de diferenca (atual $($pat.Current.Value), pedido $valor)." }
  $pat.SetValue($valor)
}
