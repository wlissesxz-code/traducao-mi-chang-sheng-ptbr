# Instala as imagens do Guia (enciclopedia) em portugues. Faz backup dos originais (so na 1a vez) e troca os PNG do jogo.
# Uso: instalar-guias.bat   |   -Jogo "pasta do jogo"   |   -Restaurar (volta os originais em chines)
param([string]$Jogo = "", [switch]$Restaurar, [switch]$Silencioso)
$ErrorActionPreference = "Stop"
$aqui = Split-Path -Parent $MyInvocation.MyCommand.Path
$rel  = "Res\NewUI\TuJian\Image"
$logf = Join-Path $aqui "instalar-guias.log"
function Log([string]$m, [string]$cor = "Gray") { Write-Host $m -ForegroundColor $cor; try { Add-Content -Path $logf -Value $m -Encoding UTF8 } catch {} }

function Achar-Imagens([string]$jogo) {
  # aceita a pasta do jogo, a pasta *_Data ou a propria pasta Image
  if (-not $jogo) { return $null }
  $jogo = $jogo.Trim('"').TrimEnd('\')
  foreach ($c in @($jogo, (Join-Path $jogo $rel))) { if (Test-Path (Join-Path $c "caiji_1.png")) { return $c } }
  if (Test-Path $jogo) {
    foreach ($dd in Get-ChildItem $jogo -Directory -Filter "*_Data" -ErrorAction SilentlyContinue) {
      $c = Join-Path $dd.FullName $rel
      if (Test-Path (Join-Path $c "caiji_1.png")) { return $c }
    }
  }
  return $null
}

function Bibliotecas-Steam {
  $lib = New-Object System.Collections.ArrayList
  foreach ($d in Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue) {
    $r = $d.Root
    foreach ($p in @("SteamLibrary","Steam","Program Files (x86)\Steam","Program Files\Steam","Games\Steam","Games\SteamLibrary")) { [void]$lib.Add((Join-Path $r $p)) }
  }
  foreach ($k in @("HKCU:\Software\Valve\Steam","HKLM:\SOFTWARE\WOW6432Node\Valve\Steam","HKLM:\SOFTWARE\Valve\Steam")) {
    try { $v = Get-ItemProperty $k -ErrorAction Stop; foreach ($n in @("SteamPath","InstallPath")) { if ($v.$n) { [void]$lib.Add(($v.$n -replace "/","\")) } } } catch {}
  }
  foreach ($l in @($lib)) {
    $vdf = Join-Path $l "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
      foreach ($line in Get-Content $vdf -ErrorAction SilentlyContinue) {
        if ($line -match '"path"\s+"([^"]+)"') { [void]$lib.Add(($Matches[1] -replace "\\\\","\")) }
      }
    }
  }
  return ($lib | Select-Object -Unique)
}

try {
  if (Test-Path $logf) { Remove-Item $logf -Force -ErrorAction SilentlyContinue }
  Log ("Instalador das imagens do Guia - " + (Get-Date)) "Cyan"
  $dest = Achar-Imagens $Jogo
  if (-not $dest) {
    foreach ($l in Bibliotecas-Steam) {
      $common = Join-Path $l "steamapps\common"
      if (-not (Test-Path $common)) { continue }
      foreach ($g in Get-ChildItem $common -Directory -ErrorAction SilentlyContinue) {
        $dest = Achar-Imagens $g.FullName
        if ($dest) { break }
      }
      if ($dest) { break }
    }
  }
  while (-not $dest -and -not $Silencioso) {
    Log "Nao achei o jogo automaticamente." "Yellow"
    $r = Read-Host "Cole o caminho da pasta do jogo (ex.: D:\SteamLibrary\steamapps\common\<nome do jogo>) ou deixe vazio para sair"
    if (-not $r) { break }
    $dest = Achar-Imagens $r
    if (-not $dest) { Log "Essa pasta nao contem ...\_Data\$rel\caiji_1.png" "Red" }
  }
  if (-not $dest) { throw "Pasta de imagens do jogo nao encontrada. Rode de novo informando: -Jogo 'CAMINHO'" }
  Log "Pasta de imagens do jogo: $dest" "Cyan"

  $bak = Join-Path (Split-Path -Parent $dest) "_originais-chines-ptbr"
  New-Item -ItemType Directory -Path $bak -Force | Out-Null
  $n = 0

  if ($Restaurar) {
    Get-ChildItem $bak -Filter *.png | ForEach-Object { Copy-Item $_.FullName (Join-Path $dest $_.Name) -Force; $n++ }
    Log "$n imagens originais restauradas." "Green"
    return
  }

  $fonte = Join-Path $aqui "imagens"
  if (-not (Test-Path $fonte)) { $fonte = Join-Path $aqui "guias\imagens" }
  if (-not (Test-Path $fonte)) { throw "Pasta 'imagens' do patch nao encontrada ao lado do instalador." }

  Get-ChildItem $fonte -Filter *.png | ForEach-Object {
    $alvo = Join-Path $dest $_.Name
    $copiaBak = Join-Path $bak $_.Name
    if ((Test-Path $alvo) -and -not (Test-Path $copiaBak)) { Copy-Item $alvo $copiaBak -Force }   # guarda o original so uma vez
    Copy-Item $_.FullName $alvo -Force
    $n++
  }
  Log "`n$n imagens do Guia instaladas." "Green"
  Log "Originais em chines guardados em: $bak" "Gray"
  Log "Para desfazer: instalar-guias.ps1 -Restaurar" "Gray"
} catch {
  Log ("ERRO: " + $_) "Red"
  Log "Detalhes salvos em: $logf" "Yellow"
}
