# Roteiro de tela da Fase 9: controle do PC pelo celular.
#   -Parte a : casos 9.1 a 9.5 (um celular por vez, desconectar pelo PC, interruptores, energia e monitores em modo de teste).
#   -Parte b : casos 9.6 a 9.9 (entrada, manter acordado, ver a tela, leveza da tela). -SemMedicao pula o 9.9 (cerca de 2 min).
#   -Parte c : casos 9.10 a 9.12 (janelas, atalhos, timer de sono), cerca de 1 min. Usa a janela-teste.exe (titulo "MX-teste janela", fora da tela,
#              sem ativar): e a UNICA janela que o roteiro minimiza ou fecha de verdade (e so por mensagem a ela); "frente" so grava no log.
#              Os atalhos de teste ficam em %TEMP%\mixer-check\atalhos (nunca se abre atalho de verdade); o timer de sono so grava no log
#              (volume geral e musica reais nao sao tocados).
#   -Parte d : casos 9.13 a 9.15 (area de transferencia, arquivo, estado do PC), cerca de 40 s. A area de transferencia do app e uma variavel em
#              memoria (D28): o roteiro so le a REAL com Get-Clipboard, antes e depois, para provar que nao mudou (nunca usa Set-Clipboard).
#              Os arquivos recebidos vao para %TEMP%\mixer-check\recebidos (nunca para Downloads) e o limite no --teste-pc e de 5 MB.
#              O 9.16 (medicao com os 8 grupos ligados) roda a parte: medir.ps1 -ComServidor -TodosGrupos e medir.ps1 -ComCelular -TodosGrupos -Ciclos 50.
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente: so linha de comando do app
# (--configuracoes-sem-foco), UI Automation por padroes (Invoke, Toggle, leitura) e o celular de teste (celular.cs) em 127.0.0.1.
# SEGURANCA (D7, D30): o app roda SEMPRE com --teste-oculto --celular-local --teste-pc. Nesse modo toda acao do PC so grava "teste-pc: ..."
# no log (o RealPc nunca e criado). Antes de qualquer comando de ferramenta o roteiro confere a linha "teste-pc: modo de teste ligado"
# no log (AssertModoTestePc) e o celular.exe confere "teste":true no estado; faltando um dos dois, aborta sem enviar. Nunca desliga,
# reinicia, suspende nem bloqueia o PC, nao troca a projecao, nao apaga as telas e nao envia teclas nem cliques reais (a entrada e o
# "manter acordado" so vao para o RecordingPc). A captura da tela (9.8 e 9.9) e real, so de leitura, e a imagem nunca e gravada em disco.
# BackupConfig/RestoreConfig guardam e devolvem o config.json e o registro; nada mata o roteiro no meio (esperas por condicao).
param([ValidateSet('a', 'b', 'c', 'd')][string]$Parte = 'a', [switch]$SemMedicao)
if ($Parte -notin 'a', 'b', 'c', 'd') { "Parte '$Parte' nao existe (partes a, b, c e d)."; exit 2 }
. "$PSScriptRoot\lib.ps1"
$ErrorActionPreference = 'Stop'
$falhas = 0
$tempoTotal = [Diagnostics.Stopwatch]::StartNew()
$script:inicioRoteiro = Get-Date
$inv = [Globalization.CultureInfo]::InvariantCulture
function Resultado([string]$caso, [bool]$ok, [string]$detalhe) {
  if (-not $ok) { $script:falhas++ }
  '{0} {1}: {2}' -f $(if ($ok) { 'OK   ' } else { 'FALHA' }), $caso, $detalhe
}
function Config { if (Test-Path $script:cfgFile) { Get-Content $script:cfgFile -Raw -Encoding utf8 | ConvertFrom-Json } else { $null } }
function AbrirJanela([string]$cmd = '--abrir-sem-foco') {
  Cmd $cmd
  WaitFor { @(Windows).Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 700
  @(Windows) | Select-Object -First 1
}
function Pronto([string]$arquivo) { WaitFor { (Test-Path $arquivo) -and ((Get-Content $arquivo -Encoding utf8 -ErrorAction SilentlyContinue) -contains 'PRONTO') } 5 }
function Num($v) { [double]::Parse("$v", $inv) }

$h = $null; $difFinal = @('o RestoreConfig nao rodou'); $backupFeito = $false; $seg = $null; $arq = "$script:out\f9-segurar.txt"
try {
  StopAll
  StopPlayers
  PrepararFerramentas
  BackupConfig
  $backupFeito = $true
  "backup guardado: config.json existia=$($script:backup.ConfigExiste); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado)"

  $grupos = if ($Parte -eq 'b') { @('entrada', 'tela') } elseif ($Parte -eq 'c') { @('janelas', 'atalhos', 'estado') } elseif ($Parte -eq 'd') { @('transferencia', 'estado') } else { @() }
  $porta = LigarFerramentas $grupos
  if (-not $porta) { throw 'o servidor do celular nao ligou (sem linha no log)' }
  AssertModoTestePc
  $pa = CelularParear $porta '123456'
  $pb = CelularParear $porta '123456'
  if ($pa.Status -ne 200 -or $pb.Status -ne 200 -or -not $pa.Chave -or -not $pb.Chave -or $pa.Chave -eq $pb.Chave) { throw "o pareamento das duas chaves falhou ($($pa.Status), $($pb.Status))" }
  $ferrIds = @('energia', 'monitores', 'entrada', 'tela', 'janelas', 'atalhos', 'transferencia', 'estado')

  if ($Parte -eq 'a') {
  # 9.1 Um celular por vez
  $seg = CelularSegurar $porta $pa.Chave $arq 120
  $pronto = Pronto $arq
  $b = CelularSegundo $porta $pb.Chave
  $ocupado = @($b.Msgs | Where-Object { $_.t -eq 'ocupado' })
  $okB = [bool]($ocupado.Count -eq 1 -and $ocupado[0].por -eq 'Android' -and $b.Codigo -eq 4009)
  $novoA = CelularEstado $porta $pa.Chave
  $primeiraFechou = WaitFor { $seg.HasExited } 5
  $av = CelularAvulsas $arq
  $okA = [bool]($null -ne $novoA -and $novoA.t -eq 'estado' -and $primeiraFechou -and $av.Codigo -eq 4001 -and @($av.Msgs | Where-Object { $_.t -eq 'desconectado' }).Count -eq 1)
  $recusas = LogCount 'celular: outro celular recusado \(ocupado\)'
  $dois = LogCount '\(2 no total\)'
  $seg = $null
  Resultado '9.1 Um celular por vez' ([bool]($pronto -and $okB -and $okA -and $recusas -eq 1 -and $dois -eq 0)) "A conectado (segurar)=$pronto; B recebeu ocupado por='$($ocupado[0].por)' e fechamento=$($b.Codigo) (esperado 4009)=$okB; segunda conexao de A: recebeu estado=$($null -ne $novoA) e a primeira fechou com $($av.Codigo) apos 'desconectado' (esperado 4001)=$okA; log 'outro celular recusado'=$recusas (esperado 1); '(2 no total)'=$dois (esperado 0)"

  # 9.2 Desconectar pelo PC
  $seg = CelularSegurar $porta $pa.Chave $arq 120
  $pronto = Pronto $arq
  $h = AbrirJanela '--configuracoes-sem-foco'
  if (-not (WaitFor { $null -ne (FindById $h 'RemoteCount') } 3)) { throw 'a secao Controle pelo celular nao apareceu' }
  $cnt1 = WaitFor { (FindById $h 'RemoteCount').Current.Name -eq 'Conectado: Android' } 3
  $txt1 = (FindById $h 'RemoteCount').Current.Name
  InvokeById $h 'RemoteDisconnect'
  $fechou = WaitFor { $seg.HasExited } 5
  $av = CelularAvulsas $arq
  $cnt0 = WaitFor { (FindById $h 'RemoteCount').Current.Name -eq 'Nenhum celular conectado' } 3
  $txt0 = (FindById $h 'RemoteCount').Current.Name
  $denovo = CelularEstado $porta $pa.Chave
  $seg = $null
  $logPc = LogCount 'celular: desconectado pelo PC'
  Resultado '9.2 Desconectar pelo PC' ([bool]($pronto -and $cnt1 -and $fechou -and $av.Codigo -eq 4000 -and @($av.Msgs | Where-Object { $_.t -eq 'desconectado' }).Count -eq 1 -and $cnt0 -and $null -ne $denovo -and $denovo.t -eq 'estado' -and $logPc -eq 1)) "RemoteCount com A conectado='$txt1'; depois do Desconectar='$txt0'; A recebeu 'desconectado' e fechamento $($av.Codigo) (esperado 4000), processo encerrou=$fechou; a chave continua valendo (nova conexao recebeu estado)=$($null -ne $denovo); log 'desconectado pelo PC'=$logPc (esperado 1)"

  # 9.3 Interruptores
  $e0 = CelularEstado $porta $pa.Chave
  $todosFalsos = [bool]($null -ne $e0 -and (@($ferrIds | Where-Object { $e0.ferr.$_ -ne $false }).Count -eq 0))
  $teste = [bool]($e0.teste -eq $true)
  $rc = CelularFerramenta $porta $pa.Chave '{"t":"energia","acao":"desligar","seg":0}'
  Start-Sleep -Milliseconds 800
  $semAcao = (@(LogTestePc 'energia').Count -eq 0)
  $seg = CelularSegurar $porta $pa.Chave $arq 60
  $pronto = Pronto $arq
  $t0 = EpochMs
  ToggleFerramenta $h 'ToolPower'
  $lat = CelularEsperar $arq $t0 { param($e) $e.ferr.energia -eq $true } 3
  $cfgTem = WaitFor { @((Config).FerramentasCelular) -contains 'energia' } 1.5
  CelularSoltar $seg $arq; $seg = $null
  $ligado = FerramentaLigada $h 'ToolPower'
  Resultado '9.3 Interruptores' ([bool]($todosFalsos -and $teste -and $rc -eq 0 -and $semAcao -and $pronto -and $null -ne $lat -and $lat -le 1000 -and $cfgTem -and $ligado)) "estado inicial: ferr todo falso=$todosFalsos, teste=$teste; comando de energia com o grupo desligado (enviado=$($rc -eq 0)) sem nenhuma linha 'teste-pc: energia'=$semAcao; depois do Toggle de ToolPower: ferr.energia verdadeiro no celular em $lat ms (<= 1000), config.json com 'energia'=$cfgTem, interruptor ligado=$ligado"

  # 9.4 Energia (teste)
  AssertModoTestePc
  # a) desligar agora
  $rc = CelularFerramenta $porta $pa.Chave '{"t":"energia","acao":"desligar","seg":0}'
  $okA = WaitFor { @(LogTestePc '^energia desligar$').Count -ge 1 } 3
  $nA = @(LogTestePc '^energia desligar$').Count
  # b) suspender em 60 s e cancelar
  $tb = EpochMs
  CelularFerramenta $porta $pa.Chave '{"t":"energia","acao":"suspender","seg":60}' | Out-Null
  $eb = CelularEstado $porta $pa.Chave
  $en = $eb.pc.energia
  $okB1 = [bool]($null -ne $en -and $en.acao -eq 'suspender' -and [math]::Abs((Num $en.em) - ($tb + 60000)) -lt 8000 -and (Num $en.rest) -ge 45 -and (Num $en.rest) -le 60)
  CelularFerramenta $porta $pa.Chave '{"t":"energiaCancelar"}' | Out-Null
  $ec = CelularEstado $porta $pa.Chave
  $okB2 = [bool]($null -ne $ec.pc -and $null -eq $ec.pc.energia)
  $logCancel = LogCount 'celular: energia cancelada$'
  # c) bloquear em 5 s
  CelularFerramenta $porta $pa.Chave '{"t":"energia","acao":"bloquear","seg":5}' | Out-Null
  $okC = WaitFor { @(LogTestePc '^energia bloquear$').Count -ge 1 } 8
  Start-Sleep -Milliseconds 1500   # margem para o app nao repetir nada
  $hAg = LogHora 'celular: energia bloquear em 5 s'
  $hEx = if ($okC) { (@(LogTestePc '^energia bloquear$'))[0].Hora } else { $null }
  $dt = if ($hAg -and $hEx) { ($hEx - $hAg).TotalSeconds } else { $null }
  $ee = CelularEstado $porta $pa.Chave
  $okC2 = [bool]($okC -and $null -ne $dt -and $dt -ge 5 -and $dt -le 6.5 -and @(LogTestePc '^energia bloquear$').Count -eq 1 -and $null -eq $ee.pc.energia)
  # d) reiniciar em 60 s e desligar o grupo
  CelularFerramenta $porta $pa.Chave '{"t":"energia","acao":"reiniciar","seg":60}' | Out-Null
  $ed = CelularEstado $porta $pa.Chave
  $okD1 = [bool]($null -ne $ed.pc.energia -and $ed.pc.energia.acao -eq 'reiniciar')
  ToggleFerramenta $h 'ToolPower'
  $okD2 = WaitFor { (LogCount 'energia cancelada \(grupo desligado\)') -ge 1 } 3
  $ef = CelularEstado $porta $pa.Chave
  $okD3 = [bool]($ef.ferr.energia -eq $false -and $null -eq $ef.pc)
  ToggleFerramenta $h 'ToolPower'   # liga de novo: o timer nao pode ter voltado
  WaitFor { @((Config).FerramentasCelular) -contains 'energia' } 1.5 | Out-Null
  $eg = CelularEstado $porta $pa.Chave
  $okD4 = [bool]($eg.ferr.energia -eq $true -and $null -eq $eg.pc.energia)
  $semReiniciar = (@(LogTestePc 'reiniciar').Count -eq 0) -and (@(LogTestePc 'suspender').Count -eq 0)
  $semReal = (@(Get-Process shutdown -ErrorAction SilentlyContinue).Count -eq 0)
  $totalAcoes = @(LogTestePc '^energia ').Count
  Resultado '9.4 Energia (teste)' ([bool]($okA -and $nA -eq 1 -and $okB1 -and $okB2 -and $logCancel -ge 1 -and $okC2 -and $okD1 -and $okD2 -and $okD3 -and $okD4 -and $semReiniciar -and $semReal -and $totalAcoes -eq 2)) "desligar agora gravou 'teste-pc: energia desligar' ($nA vez)=$okA; suspender em 60 s apareceu em pc.energia (acao/em/rest corretos)=$okB1 e sumiu ao cancelar=$okB2 sem executar (log 'energia cancelada' x$logCancel); bloquear em 5 s executou $(if ($null -ne $dt) { $dt.ToString('0.00', $inv) } else { '?' }) s depois de programado (5 a 6,5), uma vez, e pc.energia voltou a nulo=$okC2; reiniciar em 60 s apareceu=$okD1, ao desligar ToolPower o log registrou 'grupo desligado'=$okD2, ferr.energia falso e pc ausente=$okD3, ao religar o timer nao voltou=$okD4; nenhum 'reiniciar' nem 'suspender' no log=$semReiniciar; nenhum shutdown.exe real=$semReal; so 2 acoes gravadas no total ($totalAcoes)"

  # 9.5 Monitores (teste)
  ToggleFerramenta $h 'ToolDisplay'
  WaitFor { @((Config).FerramentasCelular) -contains 'monitores' } 1.5 | Out-Null
  $m0 = CelularEstado $porta $pa.Chave
  $modo0 = $m0.pc.projecao.modo; $telas0 = [int]$m0.pc.projecao.telas
  $nWin = [Windows.Forms.Screen]::AllScreens.Count
  CelularFerramenta $porta $pa.Chave '{"t":"projecao","modo":"estender"}' | Out-Null
  CelularFerramenta $porta $pa.Chave '{"t":"telas","acao":"apagar"}' | Out-Null
  CelularFerramenta $porta $pa.Chave '{"t":"telas","acao":"acordar"}' | Out-Null
  $logs = WaitFor { @(LogTestePc '^(projecao estender|telas apagar|telas acordar)$').Count -ge 3 } 3
  $m1 = CelularEstado $porta $pa.Chave
  $n1 = @(LogTestePc '^projecao estender$').Count; $n2 = @(LogTestePc '^telas apagar$').Count; $n3 = @(LogTestePc '^telas acordar$').Count
  $modoValido = @('principal', 'segunda', 'estender', 'duplicar') -contains $modo0
  Resultado '9.5 Monitores (teste)' ([bool]($modoValido -and $telas0 -ge 1 -and $m1.pc.projecao.modo -eq $modo0 -and [int]$m1.pc.projecao.telas -eq $telas0 -and $logs -and $n1 -eq 1 -and $n2 -eq 1 -and $n3 -eq 1)) "pc.projecao: modo='$modo0' telas=$telas0 (o Windows tem $nWin tela(s) na area de trabalho); depois dos comandos continua modo='$($m1.pc.projecao.modo)' telas=$($m1.pc.projecao.telas) (nada mudou); log: 'teste-pc: projecao estender' x$n1, 'teste-pc: telas apagar' x$n2, 'teste-pc: telas acordar' x$n3"

  }  # fim da parte a

  if ($Parte -eq 'b') {
  function Env([string]$json) { CelularFerramenta $porta $pa.Chave $json }   # um celular de teste por comando (conecta, envia, desconecta)
  function MemApp { $p = MainProc; $p.Refresh(); [pscustomobject]@{ PrivMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1); Handles = $p.HandleCount } }
  $h = AbrirJanela '--configuracoes-sem-foco'
  if (-not (WaitFor { $null -ne (FindById $h 'ToolInput') } 3)) { throw 'a secao Controle pelo celular nao apareceu' }
  AssertModoTestePc

  # 9.6 Entrada (teste): nada vai para o Windows, so para o log do RecordingPc
  $cA = [char]0xE1; $cCheck = [char]0x2713
  $textoLog = "entrada texto 16 eventos: ol$cA $cCheck\nMX"
  # JSON so com ASCII e sem espaco literal (o PowerShell 5.1 estraga argumentos com espaco e aspas escapadas): "olá ✓" e a quebra de linha vao como \uXXXX e \n
  $bs = [string][char]92
  $jsonTexto = '{"t":"texto","s":"ol' + $bs + 'u00e1' + $bs + 'u0020' + $bs + 'u2713' + $bs + 'nMX"}'
  $codigos = @(
    (Env '{"t":"mouse","dx":10,"dy":-5}'),
    (Env '{"t":"clique","b":"esq"}'),
    (Env '{"t":"clique","b":"esq","duplo":true}'),
    (Env '{"t":"botao","b":"esq","baixo":true}'))   # o celular de teste desconecta logo depois: o botao preso tem que ser solto
  $soltou = WaitFor { @(LogTestePc '^entrada botao esq cima$').Count -ge 1 } 3
  $codigos += @(
    (Env '{"t":"rolar","dy":-3}'),
    (Env $jsonTexto),
    (Env '{"t":"tecla","k":"alt+tab"}'),
    (Env '{"t":"tecla","k":"ctrl+v"}'),
    (Env '{"t":"tecla","k":"win+r"}'),
    (Env '{"t":"mouseAbs","mon":0,"x":0.5,"y":0.5}'))
  WaitFor { @(LogTestePc '^entrada mouseAbs').Count -ge 1 } 3 | Out-Null
  Start-Sleep -Milliseconds 400
  $tx = @(LogTestePc '^entrada ' | ForEach-Object { $_.Texto })
  $conta = { param($pat) @($tx | Where-Object { $_ -match $pat }).Count }
  $iBaixo = [array]::IndexOf($tx, 'entrada botao esq baixo'); $iCima = [array]::IndexOf($tx, 'entrada botao esq cima')
  $abs = @($tx | Where-Object { $_ -match '^entrada mouseAbs (\d+) (\d+)$' } | ForEach-Object { [pscustomobject]@{ X = [int]$Matches[1]; Y = [int]$Matches[2] } })
  $okAbs = [bool]($abs.Count -eq 1 -and $abs[0].X -ge 0 -and $abs[0].X -le 65535 -and $abs[0].Y -ge 0 -and $abs[0].Y -le 65535)
  $enviou = [bool]((@($codigos | Where-Object { $_ -ne 0 })).Count -eq 0)
  $okMouse = (& $conta '^entrada mouse 10 -5$') -eq 1
  $okClique = ((& $conta '^entrada clique esq$') -eq 1) -and ((& $conta '^entrada clique esq duplo$') -eq 1)
  $okBotao = [bool]($iBaixo -ge 0 -and $iCima -gt $iBaixo -and $soltou)
  $okRolar = (& $conta '^entrada rolar 0 -3$') -eq 1
  $okTexto = @($tx | Where-Object { $_ -ceq $textoLog }).Count -eq 1
  $okTeclas = ((& $conta '^entrada tecla alt\+tab 4 eventos$') -eq 1) -and ((& $conta '^entrada tecla ctrl\+v 4 eventos$') -eq 1)
  $semWin = (@(LogLines | Where-Object { $_ -match 'win\+r' }).Count -eq 0)
  $totalEnt = $tx.Count
  Resultado '9.6 Entrada (teste)' ([bool]($enviou -and $okMouse -and $okClique -and $okBotao -and $okRolar -and $okTexto -and $okTeclas -and $semWin -and $okAbs -and $totalEnt -eq 10)) "todos os comandos enviados (codigos de saida: $($codigos -join ','))=$enviou;'mouse 10 -5'=$okMouse; clique esq e duplo=$okClique; botao esq baixo (linha $iBaixo) e solto ao desconectar (linha $iCima)=$okBotao; rolar 0 -3=$okRolar; texto com acento, simbolo e quebra de linha = 16 eventos com o conteudo certo=$okTexto; alt+tab e ctrl+v com 4 eventos cada=$okTeclas; win+r (fora da lista) sem linha=$semWin; mouseAbs no centro=$(if ($abs.Count) { "$($abs[0].X),$($abs[0].Y)" } else { '?' }) dentro de 0 a 65535=$okAbs; total de $totalEnt linhas 'entrada' (esperado 10)"

  # 9.7 Manter acordado (teste)
  Env '{"t":"acordado","on":true}' | Out-Null
  $okOn = WaitFor { @(LogTestePc '^acordado on$').Count -ge 1 } 3
  $e1 = CelularEstado $porta $pa.Chave   # outra conexao, depois de a primeira ter caido
  $okCont = [bool]($null -ne $e1 -and $e1.pc.acordado -eq $true)
  $semOff = (@(LogTestePc '^acordado off$').Count -eq 0)
  ToggleFerramenta $h 'ToolInput'
  $okOff = WaitFor { @(LogTestePc '^acordado off$').Count -ge 1 } 3
  WaitFor { @((Config).FerramentasCelular) -notcontains 'entrada' } 1.5 | Out-Null
  $e2 = CelularEstado $porta $pa.Chave
  $okFerr = [bool]($null -ne $e2 -and $e2.ferr.entrada -eq $false -and -not ($e2.pc.PSObject.Properties.Name -contains 'acordado'))
  $antesEnt = @(LogTestePc '^entrada ').Count
  Env '{"t":"mouse","dx":3,"dy":3}' | Out-Null
  Start-Sleep -Milliseconds 600
  $ignorou = (@(LogTestePc '^entrada ').Count -eq $antesEnt)
  $nOn = @(LogTestePc '^acordado on$').Count; $nOff = @(LogTestePc '^acordado off$').Count
  Resultado '9.7 Manter acordado (teste)' ([bool]($okOn -and $okCont -and $semOff -and $okOff -and $okFerr -and $ignorou -and $nOn -eq 1 -and $nOff -eq 1)) "'teste-pc: acordado on'=$okOn (x$nOn); depois de desconectar e reconectar, pc.acordado verdadeiro=$okCont e nenhum 'acordado off' ainda=$semOff; ao desligar ToolInput: 'teste-pc: acordado off'=$okOff (x$nOff), ferr.entrada falso e pc sem 'acordado'=$okFerr; com o grupo desligado, 'mouse' nao gera linha=$ignorou"

  # 9.8 Ver a tela (captura GDI e JPEG reais; so leitura, a imagem nao e gravada)
  $seg = CelularSegurar $porta $pa.Chave $arq 600
  $pronto = Pronto $arq
  WaitFor { @(CelularLinhas $arq).Count -ge 1 } 3 | Out-Null
  $est = @(CelularLinhas $arq)[-1].Estado
  $mons = @($est.pc.monitores)
  $m0 = $mons | Where-Object { $_.i -eq 0 } | Select-Object -First 1
  $okMons = [bool]($pronto -and $mons.Count -ge 1 -and $null -ne $m0 -and $m0.principal -eq $true -and $m0.w -gt 0)
  if (-not $okMons) { throw "o estado nao trouxe pc.monitores (monitores=$($mons.Count))" }
  $esp640 = @([math]::Min(640, [int]$m0.w), [int][math]::Round([int]$m0.h * [math]::Min(640, [int]$m0.w) / [int]$m0.w, [MidpointRounding]::AwayFromZero))
  $w5k = [math]::Min(1280, [int]$m0.w)
  $esp5k = @($w5k, [int][math]::Round([int]$m0.h * $w5k / [int]$m0.w, [MidpointRounding]::AwayFromZero))
  $t640 = (CelularTela $porta $pa.Chave 0 640).Quadros[0]
  $t5k = (CelularTela $porta $pa.Chave 0 5000).Quadros[0]
  $ok640 = [bool]($t640.Status -eq 200 -and $t640.Tipo -eq 'image/jpeg' -and $t640.Jpeg -and $t640.W -eq $esp640[0] -and $t640.H -eq $esp640[1] -and $t640.Bytes -gt 1000)
  $ok5k = [bool]($t5k.Status -eq 200 -and $t5k.Jpeg -and $t5k.W -eq $esp5k[0] -and $t5k.H -eq $esp5k[1])
  $st401 = (CelularTela $porta '-' 0 640).Quadros[0].Status
  $st409 = (CelularTela $porta $pb.Chave 0 640).Quadros[0].Status
  $dez = CelularTela $porta $pa.Chave 0 640 10
  $okDez = [bool]($dez.Quadros.Count -eq 10 -and @($dez.Quadros | Where-Object { $_.Status -ne 200 -or -not $_.Jpeg }).Count -eq 0 -and $dez.TotalMs -ge 2250)
  $encerrou = WaitFor { (LogCount 'celular: tela encerrada') -ge 1 } 15
  $nComp = LogCount 'celular: tela compartilhada'; $nEnc = LogCount 'celular: tela encerrada'
  ToggleFerramenta $h 'ToolScreen'
  WaitFor { @((Config).FerramentasCelular) -notcontains 'tela' } 1.5 | Out-Null
  $st403 = (CelularTela $porta $pa.Chave 0 640).Quadros[0].Status
  ToggleFerramenta $h 'ToolScreen'   # liga de novo para o 9.9
  WaitFor { @((Config).FerramentasCelular) -contains 'tela' } 1.5 | Out-Null
  $semDisco = (@(Get-ChildItem $script:out -Recurse -Include *.jpg, *.jpeg -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt $script:inicioRoteiro }).Count -eq 0)
  Resultado '9.8 Ver a tela' ([bool]($ok640 -and $ok5k -and $st401 -eq 401 -and $st409 -eq 409 -and $st403 -eq 403 -and $okDez -and $encerrou -and $nComp -eq 1 -and $nEnc -eq 1 -and $semDisco)) "tela principal $($m0.w)x$($m0.h) ($($mons.Count) tela(s)); mon 0 w 640: $($t640.Status) $($t640.Tipo) $($t640.Bytes) bytes, $($t640.W)x$($t640.H) (esperado $($esp640[0])x$($esp640[1]))=$ok640; w 5000: $($t5k.W)x$($t5k.H) (esperado $($esp5k[0])x$($esp5k[1]); maximo 1280)=$ok5k; sem cookie=$st401 (401); chave de outro celular=$st409 (409); ToolScreen desligado=$st403 (403); 10 quadros seguidos: todos 200 em JPEG=$okDez, total $($dez.TotalMs) ms (minimo 2250); log 'tela compartilhada' x$nComp (esperado 1) e 'tela encerrada' x$nEnc depois de 10 s sem pedido=$encerrou; nenhuma imagem gravada em disco=$semDisco"

  # 9.9 Leveza da tela (60 s a 2 qps, depois 60 s parado), com a janela de Configuracoes fechada
  if ($SemMedicao) {
    "     9.9 pulado (-SemMedicao)"
  } else {
    Cmd '--fechar'
    WaitFor { @(Windows).Count -eq 0 } 3 | Out-Null
    Start-Sleep -Seconds 5
    $p = MainProc
    $mAntes = MemApp
    $p.Refresh(); $c0 = $p.TotalProcessorTime.TotalSeconds; $r0 = Get-Date
    $rodada = CelularTela $porta $pa.Chave 0 640 120 500
    $p.Refresh(); $c1 = $p.TotalProcessorTime.TotalSeconds; $r1 = Get-Date
    $secs = ($r1 - $r0).TotalSeconds
    $cpuNucleo = [math]::Round(($c1 - $c0) / $secs * 100, 2)
    $cpuTotal = [math]::Round(($c1 - $c0) / ($secs * [Environment]::ProcessorCount) * 100, 3)
    $ok200 = @($rodada.Quadros | Where-Object { $_.Status -eq 200 -and $_.Jpeg }).Count
    $mFim = MemApp
    Start-Sleep -Seconds 60
    $mDepois = MemApp
    $difMB = [math]::Round($mDepois.PrivMB - $mAntes.PrivMB, 1)
    $difH = $mDepois.Handles - $mAntes.Handles
    $encerrou2 = (LogCount 'celular: tela encerrada') -ge 2
    $mediaMs = if ($rodada.Quadros.Count) { [math]::Round(($rodada.Quadros | Measure-Object -Property Ms -Average).Average, 0) } else { '?' }
    Resultado '9.9 Leveza da tela' ([bool]($ok200 -ge 110 -and $cpuNucleo -lt 5 -and $difMB -le 5 -and $difH -le 10 -and $encerrou2)) "60 s a 2 qps: $ok200 de $($rodada.Quadros.Count) quadros 200 em JPEG (media $mediaMs ms por quadro, $([math]::Round($secs,1)) s); CPU media durante a captura=$cpuNucleo% de um nucleo ($cpuTotal% da CPU toda; meta < 5%); bytes privados antes=$($mAntes.PrivMB) MB, no fim da captura=$($mFim.PrivMB) MB, 60 s depois de parar=$($mDepois.PrivMB) MB (diferenca $difMB MB; maximo 5); handles antes=$($mAntes.Handles), depois=$($mDepois.Handles) (diferenca $difH; nao pode crescer mais de 10); 'tela encerrada' registrada=$encerrou2"
  }
  CelularSoltar $seg $arq; $seg = $null
  }  # fim da parte b

  if ($Parte -eq 'c') {
  function Env([string]$json) { CelularFerramenta $porta $pa.Chave $json }
  AssertModoTestePc
  $jlog = "$script:out\f9-janela.txt"
  $dirAt = "$script:out\atalhos"
  $inv2 = [Globalization.CultureInfo]::InvariantCulture

  # 9.10 Janelas (EnumWindows e WM_CLOSE reais, so na janela-teste.exe)
  $jp = StartJanelaTeste $jlog 300
  $jl0 = JanelaTesteLog $jlog
  $linhaPronto = @($jl0 | Where-Object { $_ -like 'PRONTO *' })
  if ($linhaPronto.Count -ne 1) { throw 'a janela de teste nao ficou pronta' }
  $hwndJ = [int64](($linhaPronto[0] -split ' ')[1])
  $r1 = CelularFerramentaMsgs $porta $pa.Chave '{"t":"janelas"}'
  $msgJ = @($r1.Msgs | Where-Object { $_.t -eq 'janelas' })
  $lista = if ($msgJ.Count) { @($msgJ[0].lista) } else { @() }
  $item = @($lista | Where-Object { $_.titulo -eq 'MX-teste janela' })
  if ($item.Count -ne 1) { throw "a janela de teste nao esta na lista (itens=$($lista.Count))" }
  $idJ = [int64]$item[0].id
  $okLista = [bool]($r1.Codigo -eq 0 -and $idJ -eq $hwndJ -and $item[0].proc -eq 'janela-teste' -and $item[0].min -eq $false -and $item[0].icone)
  $semProprio = (@($lista | Where-Object { $_.proc -eq 'MixerDeAudio' }).Count -eq 0)
  $semVazio = (@($lista | Where-Object { -not $_.titulo }).Count -eq 0)
  $stIcone = CelularGet $porta "/icone/$($item[0].icone)" $pa.Chave
  $jl1 = (JanelaTesteLog $jlog).Count
  # minimizar (so a janela de teste; ela registra o pedido e nao minimiza de verdade)
  $rcMin = Env "{`"t`":`"janela`",`"id`":$idJ,`"acao`":`"minimizar`"}"
  $okMin = WaitFor { (JanelaTesteLog $jlog) -contains 'WM_SYSCOMMAND minimizar' } 3
  $logMin = (WaitFor { @(LogTestePc '^janela minimizar$').Count -ge 1 } 3)
  # frente: so grava no log do app; a janela de teste nunca e ativada
  $rcFr = Env "{`"t`":`"janela`",`"id`":$idJ,`"acao`":`"frente`"}"
  $okFr = WaitFor { @(LogTestePc '^janela frente$').Count -ge 1 } 3
  Start-Sleep -Milliseconds 400
  $semAtiv = -not ((JanelaTesteLog $jlog) -contains 'WM_ACTIVATE ativada')
  # id inventado: ignorado
  $rcFake = Env '{"t":"janela","id":12345,"acao":"fechar"}'
  $okFake = WaitFor { (LogCount 'celular: janela ignorada') -ge 1 } 3
  Start-Sleep -Milliseconds 300
  $vivaFake = (-not $jp.HasExited) -and (-not ((JanelaTesteLog $jlog) -contains 'WM_CLOSE'))
  # fechar
  $rcClose = Env "{`"t`":`"janela`",`"id`":$idJ,`"acao`":`"fechar`"}"
  $saiu = WaitFor { $jp.HasExited } 5
  $logJ = JanelaTesteLog $jlog
  $okClose = [bool]($saiu -and ($logJ -contains 'WM_CLOSE'))
  $acoesApp = @(LogLines | Where-Object { $_ -match 'celular: janela (frente|minimizar|fechar) \(' })
  $soTeste = [bool]($acoesApp.Count -eq 3 -and @($acoesApp | Where-Object { $_ -notmatch '\(janela-teste\)$' }).Count -eq 0)
  $nFechar = @(LogTestePc '^janela fechar$').Count
  $r2 = CelularFerramentaMsgs $porta $pa.Chave '{"t":"janelas"}'
  $lista2 = @(@($r2.Msgs | Where-Object { $_.t -eq 'janelas' })[0].lista)
  $sumiu = (@($lista2 | Where-Object { $_.titulo -eq 'MX-teste janela' }).Count -eq 0)
  $script:janelaTeste = $null
  Resultado '9.10 Janelas' ([bool]($okLista -and $semProprio -and $semVazio -and $stIcone -eq 200 -and $rcMin -eq 0 -and $okMin -and $logMin -and $okFr -and $semAtiv -and $okFake -and $vivaFake -and $okClose -and $nFechar -eq 1 -and $soTeste -and $sumiu)) "lista com $($lista.Count) janela(s): 'MX-teste janela' com id=$idJ (igual ao hwnd da janela de teste=$($idJ -eq $hwndJ)), proc='$($item[0].proc)', minimizada=$($item[0].min), icone=$([bool]$item[0].icone) (GET /icone=$stIcone, esperado 200)=$okLista; sem janela do proprio app=$semProprio e sem titulo vazio=$semVazio; minimizar: o log da janela de teste registrou 'WM_SYSCOMMAND minimizar'=$okMin e o app gravou 'teste-pc: janela minimizar'=$logMin; frente: so 'teste-pc: janela frente'=$okFr e a janela nunca foi ativada=$semAtiv; id inventado (12345): 'janela ignorada' no log=$okFake e a janela de teste continuou viva sem WM_CLOSE=$vivaFake; fechar: WM_CLOSE registrado e o processo saiu=$okClose, 'teste-pc: janela fechar' x$nFechar; as 3 acoes do app foram todas em (janela-teste)=$soTeste ($($acoesApp.Count) linhas); depois de fechar a janela sumiu da lista=$sumiu"

  # 9.11 Atalhos (pasta de teste; nenhum atalho e aberto de verdade)
  New-Item -ItemType Directory -Force $dirAt | Out-Null
  Get-ChildItem $dirAt -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in '.lnk', '.url', '.txt' } | Remove-Item -Force
  $a0 = CelularEstado $porta $pa.Chave
  $vazio = [bool]($null -ne $a0.pc -and @($a0.pc.atalhos).Count -eq 0 -and ($a0.pc.PSObject.Properties.Name -contains 'atalhos'))
  [IO.File]::WriteAllText("$dirAt\Teste.url", "[InternetShortcut]`r`nURL=https://example.com/`r`n")
  [IO.File]::WriteAllText("$dirAt\nota.txt", 'nao e atalho')
  Env '{"t":"atualizar"}' | Out-Null
  $a1 = CelularEstado $porta $pa.Chave
  $lstA = @($a1.pc.atalhos)
  $okLst = [bool]($lstA.Count -eq 1 -and $lstA[0] -eq 'Teste')
  Env '{"t":"atalho","nome":"Teste"}' | Out-Null
  $okAbre = WaitFor { @(LogTestePc '^atalho Teste$').Count -ge 1 } 3
  $nAbre = @(LogTestePc '^atalho ').Count
  $logAbre = LogCount 'celular: atalho aberto Teste'
  Env '{"t":"atalho","nome":"..\\x"}' | Out-Null
  Env '{"t":"atalho","nome":"../x"}' | Out-Null
  Env '{"t":"atalho","nome":"nota"}' | Out-Null
  $okIgn = WaitFor { (LogCount 'celular: atalho ignorado') -ge 3 } 3
  Start-Sleep -Milliseconds 300
  $nAbre2 = @(LogTestePc '^atalho ').Count
  Remove-Item "$dirAt\Teste.url", "$dirAt\nota.txt" -Force -ErrorAction SilentlyContinue
  Resultado '9.11 Atalhos' ([bool]($vazio -and $okLst -and $okAbre -and $nAbre -eq 1 -and $logAbre -eq 1 -and $okIgn -and $nAbre2 -eq 1)) "pasta de teste ${dirAt}: sem atalhos o estado traz pc.atalhos vazio=$vazio; com Teste.url (e um nota.txt que nao conta) pc.atalhos=[$($lstA -join ', ')] (esperado [Teste])=$okLst; abrir 'Teste': 'teste-pc: atalho Teste' x$nAbre=$okAbre e 'celular: atalho aberto Teste' x$logAbre; '..\x', '../x' e 'nota' (fora da lista) ignorados=$okIgn, continua 1 so linha 'teste-pc: atalho' ($nAbre2); nada foi aberto de verdade (o RecordingPc so grava)"

  # 9.12 Timer de sono (teste): nada de volume nem de musica reais
  $devsAntes = @(Devices | Where-Object { $_.Padrao }) | Select-Object -First 1
  $volAntes = if ($devsAntes) { $devsAntes.Volume } else { $null }; $mudoAntes = if ($devsAntes) { $devsAntes.Mudo } else { $null }
  Env '{"t":"sono","min":5}' | Out-Null
  $s1 = CelularEstado $porta $pa.Chave
  $restOk = [bool]($null -ne $s1.pc.sono -and (Num $s1.pc.sono.rest) -ge 240 -and (Num $s1.pc.sono.rest) -le 300)
  $emOk = [bool]($restOk -and [math]::Abs((Num $s1.pc.sono.em) - ((EpochMs) + 300000)) -lt 20000)
  Env '{"t":"sono","min":0}' | Out-Null
  $s2 = CelularEstado $porta $pa.Chave
  $sumiuSono = [bool]($null -ne $s2.pc -and $null -eq $s2.pc.sono)
  $logSono5 = LogCount 'celular: sono em 5 min'; $logCancSono = LogCount 'celular: sono cancelado'
  $semGravado = (@(LogTestePc '^sono').Count -eq 0)
  # curto (so no --teste-pc): 8 s, descida de 8 passos
  $t0s = EpochMs
  Env '{"t":"sono","seg":8}' | Out-Null
  $s3 = CelularEstado $porta $pa.Chave
  $apareceCurto = [bool]($null -ne $s3.pc.sono -and (Num $s3.pc.sono.rest) -le 8)
  $feito = WaitFor { (LogCount 'celular: sono executado') -ge 1 } 20
  $tSono = ((EpochMs) - $t0s) / 1000
  Start-Sleep -Milliseconds 400
  $s4 = CelularEstado $porta $pa.Chave
  $sumiuCurto = [bool]($null -ne $s4.pc -and $null -eq $s4.pc.sono)
  $volLinhas = @(LogTestePc '^sono volume ' | ForEach-Object { [double]::Parse(($_.Texto -split ' ')[2], $inv2) })
  $nPausar = @(LogTestePc '^sono pausar$').Count
  # 8 passos de descida (o ultimo em 0) e uma linha final com o volume original de volta
  $descendo = [bool]($volLinhas.Count -eq 9 -and $volLinhas[7] -eq 0 -and $null -ne $volAntes -and [math]::Abs($volLinhas[8] - $volAntes) -lt 0.011)
  $naOrdem = $volLinhas.Count -ge 8; for ($i = 1; $i -lt [math]::Min(8, $volLinhas.Count); $i++) { if ($volLinhas[$i] -gt $volLinhas[$i - 1] + 0.0001) { $naOrdem = $false } }
  $devsDepois = @(Devices | Where-Object { $_.Padrao }) | Select-Object -First 1
  $volDepois = if ($devsDepois) { $devsDepois.Volume } else { $null }; $mudoDepois = if ($devsDepois) { $devsDepois.Mudo } else { $null }
  $mesmoVol = [bool]($null -ne $volAntes -and $null -ne $volDepois -and [math]::Abs($volAntes - $volDepois) -lt 0.011 -and $mudoAntes -eq $mudoDepois)
  $logExec = LogCount 'celular: sono executado \(0 apps pausados\)'
  Resultado '9.12 Timer de sono (teste)' ([bool]($restOk -and $emOk -and $sumiuSono -and $logSono5 -eq 1 -and $logCancSono -ge 1 -and $semGravado -and $apareceCurto -and $feito -and $sumiuCurto -and $nPausar -eq 1 -and $descendo -and $naOrdem -and $mesmoVol -and $logExec -eq 1)) "5 min: pc.sono apareceu (rest e em corretos)=$emOk e sumiu ao cancelar=$sumiuSono (log 'sono em 5 min' x$logSono5, 'sono cancelado' x$logCancSono), nenhuma linha 'teste-pc: sono' antes de executar=$semGravado; curto (seg 8): pc.sono apareceu=$apareceCurto, executou em $([math]::Round($tSono,1)) s=$feito e pc.sono sumiu=$sumiuCurto; log: $($volLinhas.Count) linhas 'teste-pc: sono volume' (valores: $(($volLinhas | ForEach-Object { $_.ToString('0.00', $inv2) }) -join ' ')) descendo ate 0 e voltando ao original no fim=$descendo, em ordem decrescente=$naOrdem; 'teste-pc: sono pausar' x$nPausar; 'sono executado (0 apps pausados)' x$logExec; volume geral real antes=$volAntes depois=$volDepois e mudo $mudoAntes/$mudoDepois (iguais)=$mesmoVol"
  }  # fim da parte c

  if ($Parte -eq 'd') {
  function Env([string]$json) { CelularFerramenta $porta $pa.Chave $json }
  AssertModoTestePc
  $dirR = "$script:out\recebidos"
  $inv2 = [Globalization.CultureInfo]::InvariantCulture

  # 9.13 Area de transferencia (teste): ida e volta pelo protocolo; a area de transferencia REAL nunca e tocada pelo app
  $clipAntes = "$(Get-Clipboard -Raw -ErrorAction SilentlyContinue)"
  # o PowerShell 5.1 estraga argumentos com espaco: o espaco do texto vai como escape unicode do JSON (u0020)
  $rcW = Env '{"t":"clipGravar","s":"MX-teste\u0020123"}'
  $okG = WaitFor { @(LogTestePc '^area de transferencia gravar MX-teste 123$').Count -ge 1 } 3
  $rL = CelularFerramentaMsgs $porta $pa.Chave '{"t":"clipLer"}'
  $clipMsgs = @($rL.Msgs | Where-Object { $_.t -eq 'clip' })
  $okClip = [bool]($clipMsgs.Count -eq 1 -and $clipMsgs[0].s -ceq 'MX-teste 123' -and $clipMsgs[0].cortado -eq $false)
  # texto maior que o limite: gravar 15500 caracteres e ler de volta (so 15000 voltam)
  $rcW2 = Env ('{"t":"clipGravar","s":"' + ('a' * 15500) + '"}')
  Start-Sleep -Milliseconds 300
  $rL2 = CelularFerramentaMsgs $porta $pa.Chave '{"t":"clipLer"}'
  $clipGrande = @($rL2.Msgs | Where-Object { $_.t -eq 'clip' })
  $okGrande = [bool]($clipGrande.Count -eq 1 -and $clipGrande[0].s.Length -eq 15000)
  $clipDepois = "$(Get-Clipboard -Raw -ErrorAction SilentlyContinue)"
  $igual = ($clipAntes -ceq $clipDepois)
  $logLida = LogCount 'celular: area de transferencia lida \(12 caracteres\)'
  $logGrav = LogCount 'celular: area de transferencia gravada \(12 caracteres\)'
  Resultado '9.13 Area de transferencia (teste)' ([bool]($rcW -eq 0 -and $okG -and $rL.Codigo -eq 0 -and $okClip -and $okGrande -and $igual -and $logLida -eq 1 -and $logGrav -eq 1)) "clipGravar 'MX-teste 123' gravado so na memoria do modo de teste ('teste-pc: area de transferencia gravar')=$okG; clipLer devolveu 'clip' com s='$($clipMsgs[0].s)' e cortado=$($clipMsgs[0].cortado)=$okClip; 15500 caracteres gravados e lidos de volta: $($clipGrande[0].s.Length) (esperado 15000)=$okGrande; area de transferencia REAL (Get-Clipboard) antes e depois iguais=$igual ($($clipAntes.Length) caracteres); log 'lida (12 caracteres)' x$logLida e 'gravada (12 caracteres)' x$logGrav (sem o conteudo no log do app: so o RecordingPc grava no modo de teste)"

  # 9.14 Arquivo: corpo grande em streaming, fora do limite de 1 KB
  AssertModoTestePc
  Remove-Item $dirR -Recurse -Force -ErrorAction SilentlyContinue
  $src = "$script:out\f9-envio-1mb.bin"; $src6 = "$script:out\f9-envio-6mb.bin"
  $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
  $b1 = New-Object byte[] (1MB); $rng.GetBytes($b1); [IO.File]::WriteAllBytes($src, $b1)
  $b6 = New-Object byte[] (6MB); $rng.GetBytes($b6); [IO.File]::WriteAllBytes($src6, $b6)
  $rng.Dispose()
  $hash1 = (Get-FileHash $src -Algorithm SHA256).Hash
  $e1 = CelularArquivo $porta $pa.Chave $src 'foto.bin'
  $f1 = "$dirR\foto.bin"
  $ok1 = [bool]($e1.Status -eq 200 -and $e1.Json.nome -ceq 'foto.bin' -and (Test-Path $f1) -and (Get-FileHash $f1 -Algorithm SHA256).Hash -eq $hash1)
  $zone = ''
  try { $zone = (Get-Content $f1 -Stream Zone.Identifier -Raw -ErrorAction Stop) } catch { $zone = "erro: $($_.Exception.Message)" }
  $okZone = [bool]($zone -match 'ZoneId=3')
  $e2 = CelularArquivo $porta $pa.Chave $src 'foto.bin'
  $f2 = "$dirR\foto (2).bin"
  $ok2 = [bool]($e2.Status -eq 200 -and $e2.Json.nome -ceq 'foto (2).bin' -and (Test-Path $f2) -and (Get-FileHash $f2 -Algorithm SHA256).Hash -eq $hash1)
  $e3 = CelularArquivo $porta $pa.Chave $src ('..' + [char]92 + 'x.txt')
  $f3 = "$dirR\x.txt"
  $ok3 = [bool]($e3.Status -eq 200 -and $e3.Json.nome -ceq 'x.txt' -and (Test-Path $f3) -and -not (Test-Path "$script:out\x.txt") -and -not ((Test-Path "$env:TEMP\x.txt") -and (Get-Item "$env:TEMP\x.txt").LastWriteTime -gt $script:inicioRoteiro))
  $nArq = @(Get-ChildItem $dirR -File).Count
  $e4 = CelularArquivo $porta $pa.Chave $src6 'grande.bin'
  $parciais = @(Get-ChildItem $dirR -Filter '*.parcial' -ErrorAction SilentlyContinue).Count
  $ok4 = [bool]($e4.Status -eq 413 -and $parciais -eq 0 -and @(Get-ChildItem $dirR -File).Count -eq $nArq -and -not (Test-Path "$dirR\grande.bin"))
  # grupo desligado pelo interruptor das Configuracoes: 403 e nada gravado
  $h = AbrirJanela '--configuracoes-sem-foco'
  if (-not (WaitFor { $null -ne (FindById $h 'ToolTransfer') } 3)) { throw 'o interruptor ToolTransfer nao apareceu' }
  ToggleFerramenta $h 'ToolTransfer'
  $off = WaitFor { -not (@((Config).FerramentasCelular) -contains 'transferencia') } 3
  $e5 = CelularArquivo $porta $pa.Chave $src 'proibido.bin'
  $ok5 = [bool]($off -and $e5.Status -eq 403 -and -not (Test-Path "$dirR\proibido.bin") -and @(Get-ChildItem $dirR -File).Count -eq $nArq)
  $balao = LogCount 'celular: balao: Arquivo recebido$'
  $logRec = LogCount 'celular: arquivo recebido \(1048576 bytes\)'
  $semNome = (LogCount 'foto\.bin|x\.txt|proibido') -eq 0
  $zonaBal = LogCount 'celular: arquivo recusado \(413\)'
  Remove-Item $src, $src6 -Force -ErrorAction SilentlyContinue
  Resultado '9.14 Arquivo' ([bool]($ok1 -and $okZone -and $ok2 -and $ok3 -and $nArq -eq 3 -and $ok4 -and $ok5 -and $balao -eq 3 -and $logRec -eq 3 -and $semNome -and $zonaBal -eq 1)) "1 MB aleatorio: status $($e1.Status), nome='$($e1.Json.nome)' com o mesmo SHA-256=$ok1 e Zone.Identifier com ZoneId=3=$okZone; o mesmo nome de novo: status $($e2.Status), nome='$($e2.Json.nome)' (esperado 'foto (2).bin') com o mesmo SHA-256=$ok2; nome '..\x.txt': status $($e3.Status), gravado como '$($e3.Json.nome)' dentro de recebidos e nada fora da pasta=$ok3 ($nArq arquivos na pasta, esperado 3); 6 MB (limite do modo de teste 5 MB): status $($e4.Status) (esperado 413), '.parcial' restantes=$parciais, nada gravado=$ok4; grupo desligado (ToolTransfer): config sem 'transferencia'=$off, status $($e5.Status) (esperado 403), nada gravado=$ok5; log: 'arquivo recebido (1048576 bytes)' x$logRec, balao 'Arquivo recebido' x$balao, 'recusado (413)' x$zonaBal, nenhum nome de arquivo no log=$semNome"

  # 9.15 Estado do PC: o timer so existe com a assinatura
  $ri = CelularInfo $porta $pa.Chave 7 5
  $infos = @($ri.Info)
  $coerente = $infos.Count -gt 0
  foreach ($i in $infos) {
    $cpu = Num $i.cpu; $mu = [double]$i.mem.usada; $mt = [double]$i.mem.total
    if ($cpu -lt 0 -or $cpu -gt 100 -or $mt -le 0 -or $mu -le 0 -or $mu -gt $mt -or [double]$i.ligado -le 0 -or [double]$i.disco.livre -le 0 -or [double]$i.disco.total -lt [double]$i.disco.livre) { $coerente = $false }
  }
  $cpus = ($infos | ForEach-Object { $_.cpu }) -join ' '
  $semBat = if ($infos.Count) { if ($infos[0].bateria) { "bateria $($infos[0].bateria.pct)%" } else { 'sem bateria' } } else { '?' }
  Resultado '9.15 Estado do PC' ([bool]($ri.Codigo -eq 0 -and $infos.Count -ge 3 -and $infos.Count -le 4 -and $coerente -and @($ri.Depois).Count -eq 0)) "assinar info por 7 s (codigo de saida $($ri.Codigo)): $($infos.Count) mensagens (esperado 3 ou 4; CPU: $cpus; memoria $([math]::Round([double]$infos[0].mem.usada / 1GB, 1)) de $([math]::Round([double]$infos[0].mem.total / 1GB, 1)) GB, disco livre $([math]::Round([double]$infos[0].disco.livre / 1GB, 0)) GB, $semBat), todas coerentes=$coerente; depois do off e de 5 s de espera: $(@($ri.Depois).Count) mensagens (esperado 0)"
  }  # fim da parte d

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
  if ($seg) { try { CelularSoltar $seg $arq } catch {} }
  StopJanelaTeste
  if ($Parte -eq 'c') { Remove-Item "$script:out\atalhos\Teste.url", "$script:out\atalhos\nota.txt" -Force -ErrorAction SilentlyContinue }
  if ($Parte -eq 'd') { Remove-Item "$script:out\f9-envio-1mb.bin", "$script:out\f9-envio-6mb.bin" -Force -ErrorAction SilentlyContinue; Remove-Item "$script:out\recebidos" -Recurse -Force -ErrorAction SilentlyContinue }
  StopPlayers
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) tocadores=$(PlayersAbertos) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
  if ((AppProcs).Count -ne 0 -or (PlayersAbertos) -ne 0) { $script:falhas++ }
  ReabrirAppDoUsuario
  'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
}
if ($falhas -gt 0) { exit 1 }
