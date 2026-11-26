# Auxiliares dos roteiros de tela do Mixer de Áudio.
# Regras: nada de teclas nem cliques reais, nada de trazer janela para a frente (o usuário pode estar jogando).
# Só linha de comando do app, UI Automation, GetWindowRect e PrintWindow.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
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

# Modo de teste oculto (Fase 7): StartApp sempre passa --teste-oculto, entao nenhuma janela nem icone do app aparece nas telas
# (a janela abre abaixo e a esquerda de todos os monitores, sem ativacao, e a posicao calculada vai para o log).
# Se o app do usuario (publicado\MixerDeAudio.exe) estava rodando quando o script comecou, ele e reaberto normalmente, sem
# argumentos, quando o roteiro termina (ReabrirAppDoUsuario, chamada no fim de cada roteiro de fora): o usuario nao fica sem o mixer.
# Fase 8 (D20): guarda o caminho do MixerDeAudio.exe que estava rodando, seja qual for a pasta (publicado\ ou o instalado), e reabre esse.
$script:exeUsuario = @(Get-Process MixerDeAudio -ErrorAction SilentlyContinue | ForEach-Object { $_.Path } | Where-Object { $_ } | Select-Object -First 1)[0]
$script:usuarioRodava = [bool]$script:exeUsuario
# Chamar no fim do roteiro de fora (depois de StopAll e das conferencias de processos restantes).
function ReabrirAppDoUsuario {
  if ($script:usuarioRodava -and @(Get-Process MixerDeAudio -ErrorAction SilentlyContinue).Count -eq 0 -and (Test-Path $script:exeUsuario)) { Start-Process $script:exeUsuario | Out-Null; 'app do usuario reaberto (sem argumentos)' }
}
# Abre a primeira cópia (sempre com --teste-oculto) e espera o processo existir.
function StartApp([string[]]$argList = @()) {
  Start-Process $script:exe -ArgumentList (@('--teste-oculto') + $argList) | Out-Null
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
# Encerra o app sempre: --sair pelo próprio caminho de cada cópia aberta (a instância única vale por caminho do .exe:
# o --sair de publicado\ não fecha a cópia instalada). Stop-Process só sobra para cópias de publicado\, nunca para a
# cópia instalada do usuário (matar à força pode perder o config.json).
function StopAll {
  $caminhos = @(AppProcs | ForEach-Object { $_.Path } | Where-Object { $_ } | Select-Object -Unique)
  foreach ($c in $caminhos) { try { Start-Process $c -ArgumentList '--sair' -Wait | Out-Null } catch {} }
  if ($caminhos.Count -gt 0) { WaitFor { (AppProcs).Count -eq 0 } 5 | Out-Null }
  AppProcs | Where-Object { $_.Path -eq $script:exe } | Stop-Process -Force -ErrorAction SilentlyContinue
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
  foreach ($nome in 'tocar', 'tocar-b', 'sessoes', 'celular', 'janela-teste') {
    $fonte = if ($nome -eq 'tocar-b') { "$script:ferr\tocar-b.cs" } else { "$PSScriptRoot\$nome.cs" }
    $dest = "$script:ferr\$nome-bin"
    $exe = "$dest\$nome.exe"
    if ((Test-Path $exe) -and (Get-Item $exe).LastWriteTime -gt (Get-Item $fonte).LastWriteTime) { continue }
    if ($nome -ne 'tocar-b') { Copy-Item $fonte "$script:ferr\$nome.cs" -Force; $fonte = "$script:ferr\$nome.cs" }
    $r = & dotnet publish $fonte -o $dest -v q 2>&1
    if (-not (Test-Path $exe) -or (Get-Item $exe).LastWriteTime -lt (Get-Item $fonte).LastWriteTime) { throw "Falha ao publicar $nome`: $($r | Select-Object -Last 5)" }
  }
}

# Lista as sessões de todas as saídas ativas (pid, nome, estado, volume, mudo).
function Sessoes {
  & "$script:ferr\sessoes-bin\sessoes.exe" listar | ForEach-Object {
    $p = $_ -split '\|'
    if ($p.Count -ge 5) {
      [pscustomobject]@{ Pid = [int]$p[0]; Nome = $p[1]; Estado = $p[2]; Volume = [double]::Parse($p[3], [Globalization.CultureInfo]::InvariantCulture); Mudo = ($p[4] -eq 'sim')
        SaidaId = $(if ($p.Count -ge 6) { $p[5] } else { '' }) }
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
function StartPlayer([string]$nome, [switch]$B, [int]$segundos = 900, [string[]]$Extra = @()) {
  $exeName = if ($B) { 'tocar-b' } else { 'tocar' }
  $p = Start-Process "$script:ferr\$exeName-bin\$exeName.exe" -ArgumentList (@('--nome', "`"$nome`"", '--segundos', "$segundos") + $Extra) -PassThru -WindowStyle Hidden
  [void]$script:players.Add($p)
  WaitFor { $s = SessionOf $p.Id; $s -and $s.Nome -eq $nome } 8 | Out-Null
  # O Windows guarda volume e mudo por app: se um roteiro anterior deixou o tocador mudo, ele já nasce mudo. Tira do mudo (só tocador de teste).
  $s0 = SessionOf $p.Id
  if ($s0 -and $s0.Mudo) { DefinirSessao $p.Id ($s0.Volume.ToString('0.00', [Globalization.CultureInfo]::InvariantCulture)) 'som' | Out-Null }
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

# ---------------- Fase 4: saída por app ----------------
# Segurança: só os tocadores de teste (processos tocar e tocar-b, linhas "MX-teste ...") têm a saída trocada.
# O sessoes.exe recusa (código 3) qualquer outro processo e ClickRoute recusa qualquer outra linha. Os apps reais só são lidos.
# Nunca existe chamada que apague a escolha de todos os apps.

# Id da saída (MMDevice) em que a sessão do processo está tocando agora (coluna nova do "listar"), ou $null.
function SessionDevice([int]$procId) { $s = SessionOf $procId; if ($s) { $s.SaidaId } else { $null } }
# Escolha de saída gravada no Windows para o processo (só leitura): id do MMDevice ou 'padrao'.
function RotaLer([int]$procId) { ((& "$script:ferr\sessoes-bin\sessoes.exe" rota-ler "$procId") | Select-Object -First 1) }
# Grava a escolha (id do MMDevice, ou 'padrao' para apagar). O sessoes.exe recusa o que não for tocador. Devolve o código de saída.
function Rota([int]$procId, [string]$destino) {
  & "$script:ferr\sessoes-bin\sessoes.exe" rota "$procId" $destino | Out-Null
  $LASTEXITCODE
}
# Botões de saída de uma linha da janela (UI Automation, sem clique): Nome e Ativo.
function RouteButtons([IntPtr]$h, [string]$app) {
  $row = RowElement $h $app; if (-not $row) { return @() }
  $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty), 'RouteButton'
  @($row.FindAll($TS::Descendants, $c)) | ForEach-Object {
    [pscustomobject]@{
      Nome = $_.Current.Name
      Ativo = ($_.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On)
    }
  }
}
# TogglePattern.Toggle no botão "Enviar <app> para <curto>", sem clique real. SÓ em linhas MX-teste.
function ClickRoute([IntPtr]$h, [string]$app, [string]$curto) {
  AssertTocador $app
  $row = RowElement $h $app; if (-not $row) { throw "linha '$app' nao encontrada" }
  $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty), 'RouteButton'
  $alvo = @($row.FindAll($TS::Descendants, $c)) | Where-Object { $_.Current.Name -eq "Enviar $app para $curto" } | Select-Object -First 1
  if (-not $alvo) { throw "botao 'Enviar $app para $curto' nao encontrado" }
  $alvo.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
}

# ---------------- Fase 5: configurações, favoritas, tema e iniciar com o Windows ----------------
# Segurança (P3): o roteiro guarda o config.json e os valores MixerDeAudio das chaves Run e StartupApproved\Run
# (inclusive "não existia") em BackupConfig e devolve tudo em RestoreConfig (no finally). Só o valor MixerDeAudio é escrito
# ou apagado; nenhum outro valor dessas chaves é tocado. Nada muda o som.
$script:RunPath = 'Software\Microsoft\Windows\CurrentVersion\Run'
$script:ApprovedPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'
$script:cfgFile = "$env:LOCALAPPDATA\MixerDeAudio\config.json"
$script:bkDir = "$script:out\backup-config"
$script:backup = $null

# Lê um valor do registro (HKCU): Existe, Tipo, Valor (sem expandir variáveis).
function RegLer([string]$sub, [string]$nome) {
  $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub)
  if (-not $k) { return [pscustomobject]@{ Existe = $false; Tipo = $null; Valor = $null } }
  try {
    if ($k.GetValueNames() -notcontains $nome) { return [pscustomobject]@{ Existe = $false; Tipo = $null; Valor = $null } }
    $tipo = $k.GetValueKind($nome)
    $v = $k.GetValue($nome, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
    [pscustomobject]@{ Existe = $true; Tipo = $tipo; Valor = $v }
  } finally { $k.Close() }
}
function RegTexto($o) {
  if (-not $o.Existe) { return '(nao existia)' }
  if ($o.Valor -is [byte[]]) { return "$($o.Tipo):" + (($o.Valor | ForEach-Object { $_.ToString('x2') }) -join '') }
  "$($o.Tipo):$($o.Valor)"
}
# Escreve (ou apaga, se não existia) SÓ o valor informado.
function RegEscrever([string]$sub, [string]$nome, $o) {
  if ($o.Existe) {
    $k = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($sub)
    try { $k.SetValue($nome, $o.Valor, $o.Tipo) } finally { $k.Close() }
  } else {
    $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub, $true)
    if ($k) { try { $k.DeleteValue($nome, $false) } finally { $k.Close() } }
  }
}
# Todos os valores da chave, menos MixerDeAudio, como texto (para provar que nada mais foi tocado).
function RegOutros([string]$sub) {
  $k = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($sub)
  if (-not $k) { return '(chave inexistente)' }
  try {
    $nomes = @($k.GetValueNames() | Where-Object { $_ -ne 'MixerDeAudio' } | Sort-Object)
  } finally { $k.Close() }
  (@($nomes | ForEach-Object { $o = RegLer $sub $_; "$_=" + (RegTexto $o) })) -join "`n"
}
function CfgBytes { if (Test-Path $script:cfgFile) { , [IO.File]::ReadAllBytes($script:cfgFile) } else { $null } }

# Guarda o estado atual. Também grava uma cópia em disco (%TEMP%\mixer-check\backup-config) como segurança extra.
function BackupConfig {
  $bytes = CfgBytes
  $script:backup = [pscustomobject]@{
    ConfigExiste = ($null -ne $bytes); ConfigBytes = $bytes
    Run = RegLer $script:RunPath 'MixerDeAudio'; Aprovado = RegLer $script:ApprovedPath 'MixerDeAudio'
    RunOutros = RegOutros $script:RunPath; AprovadoOutros = RegOutros $script:ApprovedPath
  }
  New-Item -ItemType Directory -Force $script:bkDir | Out-Null
  # Copia em disco com data e hora, nunca sobrescrita nem apagada por outro BackupConfig (Fase 7: um BackupConfig aninhado
  # apagou a copia do usuario). Guarda as 30 mais recentes; copia igual a ultima nao e repetida.
  if ($bytes) {
    $ultima = Get-ChildItem "$script:bkDir\config-*.json" -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $ultima -or -not [Linq.Enumerable]::SequenceEqual([byte[]][IO.File]::ReadAllBytes($ultima.FullName), [byte[]]$bytes)) {
      [IO.File]::WriteAllBytes("$script:bkDir\config-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff').json", $bytes)
    }
    Get-ChildItem "$script:bkDir\config-*.json" | Sort-Object Name -Descending | Select-Object -Skip 30 | Remove-Item -Force
  }
  @("config.json existia: $($script:backup.ConfigExiste)", "Run MixerDeAudio: $(RegTexto $script:backup.Run)", "StartupApproved MixerDeAudio: $(RegTexto $script:backup.Aprovado)") |
    Set-Content "$script:bkDir\estado.txt" -Encoding utf8
}
# Compara o estado atual com o do BackupConfig. Devolve a lista de diferenças (vazia = igual).
function ConfigDiferencas {
  $d = New-Object System.Collections.ArrayList
  $b = $script:backup; if (-not $b) { return @('sem backup') }
  $bytes = CfgBytes
  if ($b.ConfigExiste -ne ($null -ne $bytes)) { [void]$d.Add("config.json existia=$($b.ConfigExiste) agora=$($null -ne $bytes)") }
  elseif ($bytes -and -not [Linq.Enumerable]::SequenceEqual([byte[]]$b.ConfigBytes, [byte[]]$bytes)) { [void]$d.Add('config.json com conteudo diferente') }
  if (Test-Path "$script:cfgFile.tmp") { [void]$d.Add('sobrou config.json.tmp') }
  $r = RegTexto (RegLer $script:RunPath 'MixerDeAudio'); $r0 = RegTexto $b.Run
  if ($r -ne $r0) { [void]$d.Add("Run MixerDeAudio: antes=$r0 agora=$r") }
  $a = RegTexto (RegLer $script:ApprovedPath 'MixerDeAudio'); $a0 = RegTexto $b.Aprovado
  if ($a -ne $a0) { [void]$d.Add("StartupApproved MixerDeAudio: antes=$a0 agora=$a") }
  if ((RegOutros $script:RunPath) -ne $b.RunOutros) { [void]$d.Add('outros valores da Run mudaram') }
  if ((RegOutros $script:ApprovedPath) -ne $b.AprovadoOutros) { [void]$d.Add('outros valores do StartupApproved\Run mudaram') }
  @($d)
}
# Devolve tudo como estava (chamar com o app encerrado) e confere. Devolve as diferenças que sobraram.
function RestoreConfig {
  $b = $script:backup; if (-not $b) { return @('sem backup') }
  if ($b.ConfigExiste) {
    New-Item -ItemType Directory -Force (Split-Path $script:cfgFile) | Out-Null
    [IO.File]::WriteAllBytes($script:cfgFile, [byte[]]$b.ConfigBytes)
  } else { Remove-Item $script:cfgFile -Force -ErrorAction SilentlyContinue }
  Remove-Item "$script:cfgFile.tmp" -Force -ErrorAction SilentlyContinue
  RegEscrever $script:RunPath 'MixerDeAudio' $b.Run
  RegEscrever $script:ApprovedPath 'MixerDeAudio' $b.Aprovado
  ConfigDiferencas
}
# "Iniciar com o Windows" como o app calcula (D3): valor da Run aponta para este .exe e não está desligado no StartupApproved.
function StartupRegLigado {
  $run = RegLer $script:RunPath 'MixerDeAudio'
  if (-not $run.Existe -or $run.Valor -isnot [string]) { return $false }
  $v = $run.Valor.Trim(); if ($v.StartsWith('"')) { $i = $v.IndexOf('"', 1); if ($i -gt 0) { $v = $v.Substring(1, $i - 1) } }
  if ($v.Trim('"').Trim() -ne $script:exe) { return $false }
  $ap = RegLer $script:ApprovedPath 'MixerDeAudio'
  if ($ap.Existe -and $ap.Valor -is [byte[]] -and $ap.Valor.Length -gt 0) {
    $b0 = $ap.Valor[0]; if ($b0 -ne 2 -and $b0 -ne 6 -and ($b0 -band 1) -eq 1) { return $false }
  }
  $true
}

# --- UI Automation da página de configurações (só Invoke, Toggle, SelectionItem; sem clique real) ---
function InvokeById([IntPtr]$h, [string]$id) {
  $e = FindById $h $id; if (-not $e) { throw "elemento '$id' nao encontrado" }
  $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function OpenSettings([IntPtr]$h) {
  InvokeById $h 'SettingsButton'
  if (-not (WaitFor { $null -ne (FindById $h 'SettingsBack') } 3)) { throw 'a pagina de configuracoes nao abriu' }
  Start-Sleep -Milliseconds 300
}
function SettingsBack([IntPtr]$h) {
  InvokeById $h 'SettingsBack'
  if (-not (WaitFor { $null -ne (FindById $h 'DeviceList') } 3)) { throw 'nao voltou a pagina principal' }
  Start-Sleep -Milliseconds 300
}
# Caixas de favoritas: Nome, Marcado e Habilitado.
function FavoriteChecks([IntPtr]$h) {
  $root = $AE::FromHandle($h)
  $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty), 'FavoriteCheck'
  @($root.FindAll($TS::Descendants, $c)) | ForEach-Object {
    [pscustomobject]@{
      Nome = $_.Current.Name
      Marcado = ($_.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On)
      Habilitado = $_.Current.IsEnabled
    }
  }
}
function ToggleFavorite([IntPtr]$h, [string]$nome) {
  $c = New-Object Windows.Automation.PropertyCondition ($AE::AutomationIdProperty), 'FavoriteCheck'
  $alvo = @($AE::FromHandle($h).FindAll($TS::Descendants, $c)) | Where-Object { $_.Current.Name -eq $nome } | Select-Object -First 1
  if (-not $alvo) { throw "favorita '$nome' nao encontrada" }
  $alvo.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
  Start-Sleep -Milliseconds 250
}
function SelectTheme([IntPtr]$h, [string]$tema) {
  $id = switch ($tema) { 'auto' { 'ThemeAuto' } 'claro' { 'ThemeLight' } 'escuro' { 'ThemeDark' } default { throw "tema desconhecido: $tema" } }
  $e = FindById $h $id; if (-not $e) { throw "elemento '$id' nao encontrado" }
  $e.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
  Start-Sleep -Milliseconds 600
}
# Tema marcado na página (auto|claro|escuro) ou ''.
function ThemeSelected([IntPtr]$h) {
  foreach ($par in @(@('ThemeAuto', 'auto'), @('ThemeLight', 'claro'), @('ThemeDark', 'escuro'))) {
    $e = FindById $h $par[0]
    if ($e -and $e.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected) { return $par[1] }
  }
  ''
}
function StartupSwitchState([IntPtr]$h) {
  $e = FindById $h 'StartupSwitch'; if (-not $e) { return $null }
  $e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On
}
function ToggleStartup([IntPtr]$h) {
  $e = FindById $h 'StartupSwitch'; if (-not $e) { throw 'StartupSwitch nao encontrado' }
  $e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
  Start-Sleep -Milliseconds 400
}
# Brilho médio (0 a 255) de uma captura PNG, amostrando 1 pixel a cada 4.
function BrilhoMedio([string]$png) {
  $bmp = New-Object Drawing.Bitmap $png
  try {
    $soma = 0.0; $n = 0
    for ($y = 0; $y -lt $bmp.Height; $y += 4) { for ($x = 0; $x -lt $bmp.Width; $x += 4) {
      $c = $bmp.GetPixel($x, $y); $soma += 0.299 * $c.R + 0.587 * $c.G + 0.114 * $c.B; $n++ } }
    [math]::Round($soma / $n, 1)
  } finally { $bmp.Dispose() }
}

# ---------------- Fase 6: controle pelo celular ----------------
# Segurança (P3): o servidor do app só é ligado em 127.0.0.1 (--celular-local), então nunca aparece aviso do firewall e nada
# fica exposto na rede. O celular de teste (celular.cs) só mexe nos tocadores MX-teste e recusa geral e padrao de verdade.
# Quem chama LigarCelularTeste precisa ter feito BackupConfig antes (o config.json de teste sobrescreve o do usuário).
$script:celularCodigo = '123456'

# Grava no config.json o interruptor ligado e um código conhecido, inicia o app com --celular-local e devolve a porta lida do log
# (ou $null se o servidor não ligou em 5 s). O app não pode estar rodando.
function LigarCelularTeste([string]$codigo = '123456', [string[]]$grupos = @()) {
  $script:celularCodigo = $codigo
  New-Item -ItemType Directory -Force (Split-Path $script:cfgFile) | Out-Null
  $ferr = if ($grupos.Count) { ", `"FerramentasCelular`": [" + (($grupos | ForEach-Object { "`"$_`"" }) -join ", ") + "]" } else { "" }
  [IO.File]::WriteAllText($script:cfgFile, "{`"Versao`": 1, `"CelularLigado`": true, `"CodigoCelular`": `"$codigo`"$ferr}", (New-Object Text.UTF8Encoding $false))
  Remove-Item "$env:LOCALAPPDATA\MixerDeAudio\logs" -Recurse -Force -ErrorAction SilentlyContinue
  StartApp @('--celular-local', '--teste-pc')
  $porta = $null
  WaitFor { $script:celularPorta = CelularPorta; $null -ne $script:celularPorta } 5 | Out-Null
  $script:celularPorta
}
# Porta lida da última linha "celular: servidor ligado em <ip>:<porta>" do log.
function CelularPorta {
  $m = @(LogLines | ForEach-Object { [regex]::Match($_, 'celular: servidor ligado em [\d.]+:(\d+)') } | Where-Object { $_.Success })
  if ($m.Count -eq 0) { return $null }
  [int]$m[$m.Count - 1].Groups[1].Value
}
# Portas de 8750 a 8759 em que o processo do app está escutando (endereço e porta).
function PortasDoApp {
  $p = MainProc; if (-not $p) { return @() }
  @(Get-NetTCPConnection -State Listen -OwningProcess $p.Id -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -ge 8750 -and $_.LocalPort -le 8759 })
}
function CelularExe { "$script:ferr\celular-bin\celular.exe" }
# Pareia com o código. Devolve Status (HTTP) e Chave (vazia se não veio cookie).
function CelularParear([int]$porta, [string]$codigo) {
  $linha = (& (CelularExe) parear $porta $codigo) | Select-Object -First 1
  $p = "$linha" -split '\|'
  [pscustomobject]@{ Status = [int]$p[0]; Chave = $(if ($p.Count -gt 1) { $p[1] } else { '' }) }
}
# Primeiro estado recebido pelo WebSocket, já como objeto (ou $null).
function CelularEstado([int]$porta, [string]$chave) {
  $linha = (& (CelularExe) estado $porta $chave) | Select-Object -First 1
  if ($linha) { $linha | ConvertFrom-Json } else { $null }
}
# Envia um comando (JSON). O celular.exe recusa o que não for volume, mudo ou rota de um MX-teste. Devolve o código de saída (3 = recusado).
function CelularEnviar([int]$porta, [string]$chave, [string]$json) {
  $arg = $json.Replace([string][char]34, [string][char]92 + [char]34)  # o PowerShell 5.1 perde as aspas dos argumentos de programas nativos
  $antes = $ErrorActionPreference; $ErrorActionPreference = 'Continue'  # a recusa vai para o stderr, que no PowerShell 5.1 viraria excecao
  try { & (CelularExe) enviar $porta $chave $arg 2>$null | Out-Null; $codigo = $LASTEXITCODE } finally { $ErrorActionPreference = $antes }
  $codigo
}
function CelularCiclos([int]$porta, [string]$chave, [int]$n) {
  & (CelularExe) ciclos $porta $chave $n | Out-Null
  $LASTEXITCODE
}
# GET cru; devolve só o código HTTP. -Chave e -Origem opcionais.
function CelularGet([int]$porta, [string]$caminho, [string]$chave = '-', [string]$origem = '-') {
  [int](& (CelularExe) get $porta $caminho $chave $origem | Select-Object -First 1)
}
# Mantém um celular conectado em segundo plano. A saída (PRONTO e uma linha "epoch ms<TAB>estado" por mensagem) vai para $arquivo.
# Para encerrar com educação, crie o arquivo "$arquivo.parar" (CelularSoltar). Devolve o processo.
function CelularSegurar([int]$porta, [string]$chave, [string]$arquivo, [int]$segundos = 300) {
  Remove-Item $arquivo, "$arquivo.parar", "$arquivo.err" -Force -ErrorAction SilentlyContinue
  Start-Process (CelularExe) -ArgumentList @('segurar', "$porta", $chave, "$segundos", "`"$arquivo.parar`"") -RedirectStandardOutput $arquivo -RedirectStandardError "$arquivo.err" -PassThru -WindowStyle Hidden
}
function CelularSoltar($proc, [string]$arquivo) {
  New-Item -ItemType File -Force "$arquivo.parar" | Out-Null
  if ($proc) { try { $proc.WaitForExit(5000) | Out-Null; if (-not $proc.HasExited) { $proc.Kill() } } catch {} }
  Remove-Item "$arquivo.parar" -Force -ErrorAction SilentlyContinue
}
# Estados lidos do arquivo do CelularSegurar: Epoch (ms) e Estado (objeto).
function CelularLinhas([string]$arquivo) {
  if (-not (Test-Path $arquivo)) { return @() }
  @(Get-Content $arquivo -Encoding utf8 -ErrorAction SilentlyContinue | Where-Object { $_ -match "`t" } | ForEach-Object {
    $i = $_.IndexOf("`t")
    try { [pscustomobject]@{ Epoch = [int64]$_.Substring(0, $i); Estado = ($_.Substring($i + 1) | ConvertFrom-Json) } } catch {}
  })
}
function EpochMs { [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
# Espera, no arquivo do celular, um estado depois de $desde (epoch ms) em que $teste devolva verdadeiro. Devolve a latência (ms) ou $null.
function CelularEsperar([string]$arquivo, [int64]$desde, [scriptblock]$teste, [double]$segundos = 2) {
  $sw = [Diagnostics.Stopwatch]::StartNew()
  while ($sw.Elapsed.TotalSeconds -lt $segundos) {
    foreach ($l in (CelularLinhas $arquivo)) { if ($l.Epoch -ge $desde -and (& $teste $l.Estado)) { return ($l.Epoch - $desde) } }
    Start-Sleep -Milliseconds 25
  }
  $null
}
function AppDoEstado($estado, [string]$nome) { if ($estado) { @($estado.apps) | Where-Object { $_.nome -eq $nome } | Select-Object -First 1 } }

# ---------------- Fase 7: controles de musica por app ----------------
# Seguranca (P3): os botoes de midia so sao acionados (UIA Invoke, sem clique) em linhas "MX-teste*" ou "tocar*", que sao as
# fontes de teste (tocar.cs com --midia). Qualquer outra linha (Opera, Spotify...) e recusada com throw: as sessoes de midia
# reais so sao lidas, nunca recebem botao nem Try*.
function AssertFonteMidia([string]$nome) {
  if (-not ($nome.StartsWith('MX-teste') -or $nome.StartsWith('tocar'))) { throw "RECUSADO: '$nome' nao e uma fonte de midia de teste." }
}
# Texto "Titulo <ponto medio> Artista" como o app monta (o ponto medio e o caractere U+00B7).
function MidiaTexto([string]$titulo, [string]$artista) { if ($artista) { "$titulo $([char]0x00B7) $artista" } else { $titulo } }
# Inicia uma fonte de midia de teste. -SemAudio nao toca nada (so o controle de midia) e nao espera sessao de audio.
# -Log: arquivo onde a fonte anota "botao: <Botao>" a cada botao recebido (apagado antes de iniciar).
function StartMidia([string]$nome, [string]$log, [switch]$B, [switch]$SemAudio, [int]$segundos = 900) {
  Remove-Item $log -Force -ErrorAction SilentlyContinue
  $extra = @('--midia', '--log', "`"$log`"")
  if ($SemAudio) { $extra += '--sem-audio' }
  $exeName = if ($B) { 'tocar-b' } else { 'tocar' }
  if ($SemAudio) {
    $p = Start-Process "$script:ferr\$exeName-bin\$exeName.exe" -ArgumentList (@('--nome', "`"$nome`"", '--segundos', "$segundos") + $extra) -PassThru -WindowStyle Hidden
    [void]$script:players.Add($p)
    $p
  } else {
    StartPlayer $nome -B:$B -segundos $segundos -Extra $extra
  }
}
# Linhas "botao: X" anotadas pela fonte (so o nome do botao).
function MidiaLogLer([string]$log) {
  if (-not (Test-Path $log)) { return @() }
  @(Get-Content $log -Encoding utf8 -ErrorAction SilentlyContinue | Where-Object { $_ -match '^botao: ' } | ForEach-Object { $_.Substring(7).Trim() })
}
# Texto da 3a linha (MediaTitle) da linha do app, ou $null se nao existe (sem midia). So leitura.
function MidiaTitulo([IntPtr]$h, [string]$app) {
  $row = RowElement $h $app; if (-not $row) { return $null }
  $e = RowChild $row 'MediaTitle'; if ($e) { $e.Current.Name } else { $null }
}
# Botoes de midia da linha: Id, Nome (acessibilidade) e Habilitado. So leitura.
function MidiaBotoes([IntPtr]$h, [string]$app) {
  $row = RowElement $h $app; if (-not $row) { return @() }
  foreach ($id in 'MediaPrev', 'MediaPlayPause', 'MediaNext') {
    $e = RowChild $row $id
    if ($e) { [pscustomobject]@{ Id = $id; Nome = $e.Current.Name; Habilitado = $e.Current.IsEnabled } }
  }
}
# A linha tem um controle com esse AutomationId? (ex.: 'SessionVolume', 'SessionMute', 'RouteButton'). So leitura.
function RowTem([IntPtr]$h, [string]$app, [string]$id) {
  $row = RowElement $h $app; if (-not $row) { return $false }
  $null -ne (RowChild $row $id)
}
# InvokePattern.Invoke no botao de midia ('MediaPrev', 'MediaPlayPause' ou 'MediaNext'). SO em linhas MX-teste* ou tocar*.
function MidiaInvoke([IntPtr]$h, [string]$app, [string]$botao) {
  AssertFonteMidia $app
  if ($botao -notin 'MediaPrev', 'MediaPlayPause', 'MediaNext') { throw "botao de midia desconhecido: $botao" }
  $row = RowElement $h $app; if (-not $row) { throw "linha '$app' nao encontrada" }
  $e = RowChild $row $botao; if (-not $e) { throw "botao '$botao' nao encontrado em '$app'" }
  $e.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}

# ---------------- Fase 9: controle do PC pelo celular (modo --teste-pc) ----------------
# Seguranca (D7, D30): o app so roda com --teste-pc nos roteiros (LigarCelularTeste sempre passa o argumento). Nesse modo toda acao do PC
# (energia, projecao, telas...) so grava "teste-pc: <acao>" no log; o RealPc nunca e criado. Antes de qualquer comando de ferramenta o roteiro
# confere a linha "teste-pc: modo de teste ligado" (AssertModoTestePc) e o celular.exe confere "teste":true no estado (recusa sem isso).

# Liga o celular de teste com os grupos de ferramentas dados (ids de RemoteTools) gravados no config.json de teste.
# Exige BackupConfig antes; o app nao pode estar rodando. Devolve a porta.
function LigarFerramentas([string[]]$grupos = @(), [string]$codigo = '123456') { LigarCelularTeste $codigo $grupos }
# Aborta (throw) sem enviar nada se o log nao tiver a linha de modo de teste.
function AssertModoTestePc {
  if ((LogCount 'teste-pc: modo de teste ligado') -lt 1) { throw 'ABORTADO: o log nao tem "teste-pc: modo de teste ligado"; nenhum comando de ferramenta foi enviado.' }
}
# Linhas "teste-pc: ..." do log que casam com o padrao (regex sobre o texto depois de "teste-pc: "). Devolve Hora (DateTime) e Texto.
function LogTestePc([string]$padrao = '.') {
  @(LogLines | ForEach-Object { [regex]::Match($_, '^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3}) teste-pc: (.*)$') } |
    Where-Object { $_.Success -and $_.Groups[2].Value -match $padrao } |
    ForEach-Object { [pscustomobject]@{ Hora = [datetime]::ParseExact($_.Groups[1].Value, 'yyyy-MM-dd HH:mm:ss.fff', [Globalization.CultureInfo]::InvariantCulture); Texto = $_.Groups[2].Value } })
}
# Hora (DateTime) da primeira linha do log que casa com o padrao completo; $null se nao houver.
function LogHora([string]$padrao, [int]$indice = 0) {
  $l = @(LogLines | Where-Object { $_ -match $padrao })
  if ($l.Count -le $indice) { return $null }
  [datetime]::ParseExact($l[$indice].Substring(0, 23), 'yyyy-MM-dd HH:mm:ss.fff', [Globalization.CultureInfo]::InvariantCulture)
}
# Envia um comando de ferramenta (JSON) como o celular de teste. O celular.exe recusa (codigo 3) se o estado nao trouxer "teste":true.
function CelularFerramenta([int]$porta, [string]$chave, [string]$json) {
  $arg = $json.Replace([string][char]34, [string][char]92 + [char]34)
  $antes = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { & (CelularExe) ferramenta $porta $chave $arg 2>$null | Out-Null; $codigo = $LASTEXITCODE } finally { $ErrorActionPreference = $antes }
  $codigo
}
# Conecta como outro celular (outra chave) e devolve as mensagens (Json) e o codigo de fechamento (Codigo, ou $null).
function CelularSegundo([int]$porta, [string]$chave) {
  $msgs = @(); $codigo = $null
  foreach ($l in @(& (CelularExe) segundo $porta $chave)) {
    if ("$l" -like "MSG`t*") { $msgs += ("$l".Substring(4) | ConvertFrom-Json) }
    elseif ("$l" -like "FECHOU`t*") { $v = "$l".Substring(7); if ($v -ne '-') { $codigo = [int]$v } }
  }
  [pscustomobject]@{ Msgs = $msgs; Codigo = $codigo }
}
# Mensagens avulsas (MSG) e fechamento (FECHOU) escritos pelo "segurar" no arquivo.
function CelularAvulsas([string]$arquivo) {
  if (-not (Test-Path $arquivo)) { return [pscustomobject]@{ Msgs = @(); Codigo = $null } }
  $msgs = @(); $codigo = $null
  foreach ($l in @(Get-Content $arquivo -Encoding utf8 -ErrorAction SilentlyContinue)) {
    if ("$l" -like "MSG`t*") { try { $msgs += ("$l".Substring(4) | ConvertFrom-Json) } catch {} }
    elseif ("$l" -like "FECHOU`t*") { $codigo = [int]"$l".Substring(7) }
  }
  [pscustomobject]@{ Msgs = $msgs; Codigo = $codigo }
}
# Fase 10: canal /tela/ws como o celular de teste. $passos = "q:mon:w:segundos:ok|semok;..." no MESMO canal (ver celular.cs). Chave '-' = sem cookie.
# So le o cabecalho e as dimensoes do JPEG: a imagem nunca e gravada em disco. Devolve Passos (I, Q, Quadros, Bytes, W, H, Seg, Jpeg), Fechou (codigo ou $null),
# Recusado (texto ou $null), Fim ($true se terminou) e Codigo (saida do celular.exe).
function CelularTelaLer($linhas, $codigo) {
  $passos = @(); $fechou = $null; $recusado = $null; $fim = $false
  foreach ($l in @($linhas)) {
    $p = "$l" -split "`t"
    if ($p[0] -eq 'PASSO' -and $p.Count -ge 8) {
      $d = $p[5] -split 'x'
      $passos += [pscustomobject]@{ I = [int]$p[1]; Q = $p[2]; Quadros = [int]$p[3]; Bytes = [int]$p[4]; W = [int]$d[0]; H = [int]$d[1]; Seg = [double]::Parse($p[6], [Globalization.CultureInfo]::InvariantCulture); Jpeg = ($p[7] -eq 'jpeg')
        Textos = $(if ($p.Count -ge 12) { [int]$p[8] } else { 0 }); Pares = $(if ($p.Count -ge 12) { [int]$p[9] } else { 0 }); Fora = $(if ($p.Count -ge 12) { [int]$p[10] } else { 0 }); SemTexto = $(if ($p.Count -ge 12) { [int]$p[11] } else { 0 }) }
    }
    elseif ($p[0] -eq 'FECHOU') { $fechou = [int]$p[1] }
    elseif ($p[0] -eq 'RECUSADO') { $recusado = ($p[1..($p.Count - 1)] -join ' ') }
    elseif ($p[0] -eq 'FIM') { $fim = $true }
  }
  [pscustomobject]@{ Passos = $passos; Fechou = $fechou; Recusado = $recusado; Fim = $fim; Codigo = $codigo }
}
function CelularTela([int]$porta, [string]$chave, [string]$passos) {
  $antes = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { $saida = @(& (CelularExe) tela $porta $chave $passos 2>$null); $codigo = $LASTEXITCODE } finally { $ErrorActionPreference = $antes }
  CelularTelaLer $saida $codigo
}
# O mesmo em segundo plano (para medir a CPU do app enquanto o canal roda): a saida vai para $arquivo; devolve o processo. Espere ele terminar
# (nunca mate) e leia com CelularTelaArquivo.
function CelularTelaBg([int]$porta, [string]$chave, [string]$passos, [string]$arquivo) {
  Remove-Item $arquivo, "$arquivo.err" -Force -ErrorAction SilentlyContinue
  Start-Process (CelularExe) -ArgumentList @('tela', "$porta", $chave, "`"$passos`"") -RedirectStandardOutput $arquivo -RedirectStandardError "$arquivo.err" -PassThru -WindowStyle Hidden
}
function CelularTelaArquivo([string]$arquivo, $proc) {
  $linhas = if (Test-Path $arquivo) { @(Get-Content $arquivo -Encoding utf8 -ErrorAction SilentlyContinue) } else { @() }
  CelularTelaLer $linhas $(if ($proc -and $proc.HasExited) { $proc.ExitCode } else { $null })
}
# Como CelularFerramenta, mas devolve tambem as mensagens avulsas (MSG) recebidas logo depois do envio (ex.: a lista de janelas).
# Devolve Codigo (0 = enviado, 3 = recusado) e Msgs (objetos).
function CelularFerramentaMsgs([int]$porta, [string]$chave, [string]$json) {
  $arg = $json.Replace([string][char]34, [string][char]92 + [char]34)
  $antes = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  $msgs = @()
  try {
    $saida = @(& (CelularExe) ferramenta $porta $chave $arg 2>$null); $codigo = $LASTEXITCODE
    foreach ($l in $saida) { if ("$l" -like "MSG`t*") { try { $msgs += ("$l".Substring(4) | ConvertFrom-Json) } catch {} } }
  } finally { $ErrorActionPreference = $antes }
  [pscustomobject]@{ Codigo = $codigo; Msgs = $msgs }
}

# ---------------- Fase 9d: arquivo e estado do PC ----------------
# Envia um arquivo de %TEMP%\mixer-check\ como o celular de teste (POST /arquivo). O celular.exe recusa (codigo 3) sem "teste":true no estado.
# Devolve Status (HTTP; 0 se deu erro de rede), Corpo (texto da resposta; objeto em Json quando for JSON) e Codigo (3 = recusado).
function CelularArquivo([int]$porta, [string]$chave, [string]$caminho, [string]$nome = '') {
  $antes = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try {
    $args2 = @('arquivo', $porta, $chave, $caminho); if ($nome) { $args2 += $nome }
    $saida = @(& (CelularExe) @args2 2>$null); $codigo = $LASTEXITCODE
  } finally { $ErrorActionPreference = $antes }
  $status = 0; $corpo = ''; $json = $null
  foreach ($l in $saida) {
    $p = "$l" -split "`t", 2
    if ($p[0] -match '^\d+$') { $status = [int]$p[0]; $corpo = if ($p.Count -gt 1) { $p[1] } else { '' } }
  }
  if ($corpo) { try { $json = $corpo | ConvertFrom-Json } catch {} }
  [pscustomobject]@{ Status = $status; Corpo = $corpo; Json = $json; Codigo = $codigo }
}
# Assina o estado do PC por $segLigado s, cancela e escuta mais $segDepois s (celular.exe info). Devolve Info (mensagens recebidas ligado),
# Depois (mensagens recebidas depois do off) e Codigo.
function CelularInfo([int]$porta, [string]$chave, [int]$segLigado, [int]$segDepois) {
  $antes = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  $info = @(); $depois = @()
  try {
    $saida = @(& (CelularExe) info $porta $chave $segLigado $segDepois 2>$null); $codigo = $LASTEXITCODE
    foreach ($l in $saida) {
      if ("$l" -like "INFO`t*") { try { $info += ("$l".Substring(5) | ConvertFrom-Json) } catch {} }
      elseif ("$l" -like "DEPOIS`t*") { try { $depois += ("$l".Substring(7) | ConvertFrom-Json) } catch {} }
    }
  } finally { $ErrorActionPreference = $antes }
  [pscustomobject]@{ Info = $info; Depois = $depois; Codigo = $codigo }
}

# ---------------- Fase 9c: janela de teste (janela-teste.cs) ----------------
# Seguranca (D21, D22, D30): as unicas janelas que o roteiro minimiza ou fecha de verdade sao as da janela-teste.exe (titulo "MX-teste janela",
# fora de todas as telas, sem ativar, sem barra de tarefas). O processo e guardado em $script:janelaTeste e so ele e encerrado.
$script:janelaTeste = $null
# Inicia a janela de teste com o log em $log; espera ate 5 s pela linha "PRONTO <hwnd>". Devolve o processo.
function StartJanelaTeste([string]$log, [int]$segundos = 300) {
  $exeJ = "$script:ferr\janela-teste-bin\janela-teste.exe"
  if (-not (Test-Path $exeJ)) { throw 'janela-teste.exe nao foi publicado (PrepararFerramentas)' }
  Remove-Item $log -Force -ErrorAction SilentlyContinue
  $p = Start-Process $exeJ -ArgumentList @('--log', "`"$log`"", '--segundos', "$segundos") -PassThru -WindowStyle Hidden
  $script:janelaTeste = $p
  WaitFor { (Test-Path $log) -and @(Get-Content $log -Encoding utf8 -ErrorAction SilentlyContinue | Where-Object { $_ -like 'PRONTO *' }).Count -ge 1 } 5 | Out-Null
  $p
}
function JanelaTesteLog([string]$log) { if (Test-Path $log) { @(Get-Content $log -Encoding utf8 -ErrorAction SilentlyContinue) } else { @() } }
# Encerra a janela de teste que o roteiro iniciou (nunca outro processo): pede o fim pelo tempo; so mata se ainda estiver viva.
function StopJanelaTeste {
  if ($script:janelaTeste) {
    try { if (-not $script:janelaTeste.HasExited) { $script:janelaTeste.Kill() } } catch {}
    $script:janelaTeste = $null
  }
}

# Liga ou desliga um grupo pelo ToggleSwitch das Configuracoes (AutomationId Tool...); a janela de Configuracoes tem que estar aberta.
function ToggleFerramenta([IntPtr]$h, [string]$id) {
  $e = FindById $h $id; if (-not $e) { throw "interruptor '$id' nao encontrado" }
  $e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
}
function FerramentaLigada([IntPtr]$h, [string]$id) {
  $e = FindById $h $id; if (-not $e) { return $null }
  $e.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::On
}
