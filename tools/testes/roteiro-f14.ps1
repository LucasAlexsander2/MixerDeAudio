# Roteiro de tela da Fase 14: servidor de arquivos estaticos, pagina antiga em partes e fontes (so o que depende do app publicado de verdade;
# as regras de caminho e ETag ficam nos testes unitarios WebAssetsTests).
#   Casos 14.2 a 14.4: recursos embutidos e cabecalhos, 304 e sem regressao (parear e estado).
#   O 14.1 (pagina antiga identica) foi substituido pelo 17.1 (roteiro-f17): a pagina antiga saiu na Fase 17.
# Roda depois da 14a (app/ vazia: os casos que dependem de arquivos de app/ viram PULADO) e de novo depois da 14b, sem mudar nada.
# Usa o build publicado em publicado\. Sem teclas nem cliques reais, sem trazer janela para a frente.
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
function Pulado([string]$caso, [string]$detalhe) { 'PULADO {0}: {1}' -f $caso, $detalhe }

$tipos = @{
  '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'; '.css' = 'text/css; charset=utf-8'
  '.webmanifest' = 'application/manifest+json'; '.png' = 'image/png'; '.svg' = 'image/svg+xml'; '.woff2' = 'font/woff2'; '.txt' = 'text/plain; charset=utf-8'
}
# Devolve status, tipo, cache, etag, sha256, bytes e sha256 sem CR de um GET cru (celular.exe cabecalhos).
function Cabecalhos([int]$porta, [string]$caminho, [string]$etag = '-') {
  $arg = $etag.Replace([string][char]34, [string][char]92 + [char]34)  # o PowerShell 5.1 perde as aspas dos argumentos de programas nativos
  $linha = (& (CelularExe) cabecalhos $porta $caminho $arg) | Select-Object -First 1
  $p = "$linha" -split '\|'
  [pscustomobject]@{ Status = [int]$p[0]; Tipo = $p[1]; Cache = $p[2]; ETag = $p[3]; Sha = $p[4]; Bytes = [int]$p[5]; ShaSemCr = $(if ($p.Count -gt 6) { $p[6] } else { '' }) }
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

  # 14.1 substituido pelo 17.1 (roteiro-f17.ps1): a pagina antiga saiu e o GET / serve o layout novo

  # 14.2 Recursos embutidos e cabecalhos reais
  $web = Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'MixerDeAudio\Remote\Web'
  $arquivos = @()
  foreach ($sub in 'app', 'fontes') {
    $raiz = Join-Path $web $sub
    if (Test-Path $raiz) {
      $arquivos += @(Get-ChildItem $raiz -Recurse -File | Where-Object { $_.Extension -ne '.md' } | ForEach-Object { '/' + $sub + '/' + ($_.FullName.Substring($raiz.Length + 1) -replace '\\', '/') })
    }
  }
  $falhasDe = @(); $conferidos = 0; $etagApp = @{}
  foreach ($url in $arquivos) {
    $ext = [IO.Path]::GetExtension($url)
    $minusculo = ($url -cmatch '^[a-z0-9._/-]+$')
    $r = Cabecalhos $porta $url
    if (-not $minusculo) {
      # licencas e LEIAME (nome com maiuscula, D17): vao no .exe mas nao sao rota (D2)
      if ($r.Status -ne 404) { $falhasDe += "$url deveria ser 404 e deu $($r.Status)" }
      $conferidos++; continue
    }
    $cacheEsperado = if ($url.StartsWith('/fontes/')) { 'public, max-age=31536000, immutable' } else { 'no-cache' }
    $tipoOk = $tipos.ContainsKey($ext) -and $r.Tipo -eq $tipos[$ext]
    $arqBytes = (Get-Item (Join-Path $web ($url.TrimStart('/') -replace '/', '\'))).Length
    $etagOk = [bool]($r.ETag -match '^"[0-9a-f]{16}"$')
    if (-not ($r.Status -eq 200 -and $tipoOk -and $r.Cache -eq $cacheEsperado -and $etagOk -and $r.Bytes -eq $arqBytes)) {
      $falhasDe += "$url status=$($r.Status) tipo=$($r.Tipo) cache=$($r.Cache) etag=$($r.ETag) bytes=$($r.Bytes)/$arqBytes"
    }
    if ($url.StartsWith('/app/')) { $etagApp[$url] = $r.ETag }
    $conferidos++
  }
  # Fase 17, D1: /novo e /novo/ respondem 301 (sem corpo) para /; o destino e a CSP ficam no 17.1
  $novo = Cabecalhos $porta '/novo'
  $novoBarra = Cabecalhos $porta '/novo/'
  foreach ($n in @($novo, $novoBarra)) {
    if (-not ($n.Status -eq 301 -and $n.Bytes -eq 0)) { $falhasDe += "/novo status=$($n.Status) bytes=$($n.Bytes) (esperado 301 sem corpo)" }
  }
  $temNovo = Test-Path (Join-Path $web 'app\index.html')
  if (-not $temNovo) { $falhasDe += 'app/index.html nao existe' }
  $negados = @()
  foreach ($url in '/app/../x.js', '/antigo/04-base.js', '/app/nao-existe.js', '/app/', '/app/%2e%2e/x.js') {
    $r = Cabecalhos $porta $url
    $negados += "$url=$($r.Status)"
    if ($r.Status -ne 404) { $falhasDe += "$url deveria ser 404 e deu $($r.Status)" }
  }
  Resultado '14.2 Recursos embutidos e cabecalhos' ([bool]($falhasDe.Count -eq 0)) "arquivos conferidos em app/ e fontes/=$conferidos; negados: [$($negados -join ' ')]; /novo=$($novo.Status); problemas=[$($falhasDe -join '; ')]"

  # 14.3 304 por ETag
  $alvo = if ($etagApp.ContainsKey('/app/js/app.js')) { '/app/js/app.js' } else { $null }
  if ($alvo) {
    $r = Cabecalhos $porta $alvo $etagApp[$alvo]
    $r2 = Cabecalhos $porta $alvo '"0000000000000000"'
    Resultado '14.3 304 por ETag' ([bool]($r.Status -eq 304 -and $r.Bytes -eq 0 -and $r.Tipo -eq '' -and $r.ETag -eq $etagApp[$alvo] -and $r2.Status -eq 200)) "com o ETag certo: status=$($r.Status) corpo=$($r.Bytes) bytes content-type='$($r.Tipo)'; com ETag errado: status=$($r2.Status)"
  }
  else { Pulado '14.3 304 por ETag' '/app/js/app.js ainda nao existe (14b)' }

  # 14.4 Sem regressao: parear e receber o estado
  $pa = CelularParear $porta '123456'
  $e = if ($pa.Status -eq 200 -and $pa.Chave) { CelularEstado $porta $pa.Chave } else { $null }
  Resultado '14.4 Sem regressao (parear e estado)' ([bool]($pa.Status -eq 200 -and $pa.Chave -and $null -ne $e)) "pareamento=$($pa.Status); estado recebido=$($null -ne $e)"

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
  StopAll
  if ($backupFeito) { $difFinal = @(RestoreConfig) }
  Resultado 'Final Configuracao e registro iguais aos de antes' ([bool]($difFinal.Count -eq 0)) "config.json=$(if ($script:backup.ConfigExiste) { 'restaurado' } else { 'nao existia (apagado)' }); Run MixerDeAudio=$(RegTexto $script:backup.Run); StartupApproved MixerDeAudio=$(RegTexto $script:backup.Aprovado); diferencas=[$($difFinal -join '; ')]"
  "processos restantes: app=$((AppProcs).Count) celular=$(@(Get-Process celular -ErrorAction SilentlyContinue).Count)"
  if ((AppProcs).Count -ne 0) { $script:falhas++ }
  ReabrirAppDoUsuario
  'tempo do roteiro: {0:N0} s; falhas: {1}' -f $tempoTotal.Elapsed.TotalSeconds, $falhas
}
if ($falhas -gt 0) { exit 1 }
