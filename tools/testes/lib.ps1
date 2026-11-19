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
