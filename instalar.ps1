param([switch]$Remover)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
$appId = '1189490'     # 觅长生 na Steam
$modBase = '2824349934' # mod de base da Oficina (BepInEx)
$modEn = '2826780370'   # mod de traducao em ingles
$nomePlugin = 'MCS-PTBR-Menus'

function Fechar($codigo) {
    Write-Host ''
    Read-Host 'Pressione Enter para fechar' | Out-Null
    exit $codigo
}

try {
    Write-Host ''
    Write-Host '=== Traducao PT-BR de 觅长生 ===' -ForegroundColor Cyan
    Write-Host ''

    # Procura a pasta do mod de base nas bibliotecas do Steam.
    $bibliotecas = New-Object System.Collections.ArrayList
    $steam = $null
    foreach ($chave in @('HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        try {
            $valores = Get-ItemProperty -Path $chave -ErrorAction Stop
            if ($valores.SteamPath) { $steam = $valores.SteamPath; break }
            if ($valores.InstallPath) { $steam = $valores.InstallPath; break }
        } catch { }
    }
    if ($steam) {
        $steam = $steam -replace '/', '\'
        [void]$bibliotecas.Add($steam)
        $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($linha in Get-Content -Path $vdf) {
                if ($linha -match '"path"\s+"([^"]+)"') { [void]$bibliotecas.Add(($Matches[1] -replace '\\\\', '\')) }
            }
        }
    }
    $pastaMod = $null
    foreach ($biblioteca in $bibliotecas) {
        $candidata = Join-Path $biblioteca ('steamapps\workshop\content\' + $appId + '\' + $modBase)
        if (Test-Path $candidata) { $pastaMod = $candidata; break }
    }
    if (-not $pastaMod) {
        Write-Host 'Nao encontrei o mod de base automaticamente.' -ForegroundColor Yellow
        Write-Host ('Cole o caminho da pasta "' + $modBase + '" (ex.: D:\SteamLibrary\steamapps\workshop\content\' + $appId + '\' + $modBase + '):')
        $pastaMod = (Read-Host 'Caminho').Trim('"', ' ')
    }
    if (-not (Test-Path (Join-Path $pastaMod 'BepInEx'))) {
        throw ('A pasta BepInEx nao existe em "' + $pastaMod + '". Assine o mod de base na Oficina Steam (item ' + $modBase + ') e abra o jogo uma vez.')
    }
    $plugins = Join-Path $pastaMod 'BepInEx\plugins'
    $alvo = Join-Path $plugins $nomePlugin
    Write-Host ('Mod de base encontrado: ' + $pastaMod) -ForegroundColor Green

    if ($Remover) {
        if (-not (Test-Path $alvo)) { Write-Host 'A traducao nao esta instalada.'; Fechar 0 }
        Write-Host 'Feche o jogo antes de continuar.' -ForegroundColor Yellow
        $resposta = Read-Host ('Remover "' + $alvo + '"? (S/N)')
        if ($resposta -match '^[sSyY]') {
            Remove-Item -Path $alvo -Recurse -Force
            Write-Host 'Traducao removida.' -ForegroundColor Green
        } else { Write-Host 'Nada foi removido.' }
        Fechar 0
    }

    $origem = Join-Path $PSScriptRoot $nomePlugin
    if (-not (Test-Path $origem)) {
        throw ('Pasta "' + $nomePlugin + '" nao encontrada ao lado do instalador. Extraia o zip inteiro antes de executar.')
    }
    Write-Host 'Feche o jogo antes de continuar.' -ForegroundColor Yellow
    if (-not (Test-Path $plugins)) { New-Item -ItemType Directory -Path $plugins | Out-Null }
    if (-not (Test-Path $alvo)) { New-Item -ItemType Directory -Path $alvo | Out-Null }
    Copy-Item -Path (Join-Path $origem '*') -Destination $alvo -Recurse -Force
    Get-ChildItem -Path $alvo -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
    Write-Host ''
    Write-Host ('Traducao instalada em: ' + $alvo) -ForegroundColor Green

    # Imagens do Guia/Enciclopedia em portugues (os PNG originais sao guardados so na 1a vez).
    $guias = Join-Path $PSScriptRoot 'guias\instalar-guias.ps1'
    if (Test-Path $guias) {
        Write-Host ''
        Write-Host 'Instalando imagens do Guia...' -ForegroundColor Cyan
        try { & powershell -NoProfile -ExecutionPolicy Bypass -File $guias } catch { Write-Host ('  Imagens do Guia: ' + $_) -ForegroundColor Yellow }
    }

    # Compila o plugin (src\MenusPTBR.cs) com as bibliotecas do seu jogo; sem isso vale a DLL antiga incluida.
    $fonte = Join-Path $PSScriptRoot 'src\MenusPTBR.cs'
    if (Test-Path $fonte) {
        Write-Host ''
        Write-Host 'Compilando o plugin...' -ForegroundColor Cyan
        $managed = $null
        foreach ($biblioteca in $bibliotecas) {
            $common = Join-Path $biblioteca 'steamapps\common'
            if (-not (Test-Path $common)) { continue }
            foreach ($g in Get-ChildItem $common -Directory -ErrorAction SilentlyContinue) {
                foreach ($dd in Get-ChildItem $g.FullName -Directory -Filter '*_Data' -ErrorAction SilentlyContinue) {
                    $m = Join-Path $dd.FullName 'Managed'
                    $eDoJogo = (Test-Path (Join-Path $g.FullName 'MCSSave')) -or (Test-Path (Join-Path $dd.FullName 'Res\NewUI\TuJian\Image\caiji_1.png'))
                    if ($eDoJogo -and (Test-Path (Join-Path $m 'Assembly-CSharp.dll'))) { $managed = $m; break }
                }
                if ($managed) { break }
            }
            if ($managed) { break }
        }
        if (-not $managed) {
            Write-Host 'Nao encontrei a pasta do jogo. Cole o caminho da pasta do jogo (a que contem a pasta *_Data) ou deixe vazio para pular a compilacao:' -ForegroundColor Yellow
            $r = (Read-Host 'Caminho').Trim('"', ' ')
            if ($r) { $dd = Get-ChildItem $r -Directory -Filter '*_Data' -ErrorAction SilentlyContinue | Select-Object -First 1; if ($dd) { $managed = Join-Path $dd.FullName 'Managed' } }
        }
        $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
        $compilou = $false
        if ($managed -and (Test-Path $csc)) {
            $bep = Join-Path $pastaMod 'BepInEx'
            $refs = @('mscorlib','netstandard','System','System.Core','Unity.TextMeshPro','Assembly-CSharp','Assembly-CSharp-firstpass','DOTween','UniRx','Newtonsoft.Json') | ForEach-Object { Join-Path $managed ($_ + '.dll') }
            $refs += Get-ChildItem $managed -Filter 'UnityEngine*.dll' | ForEach-Object { $_.FullName }
            $refs += Join-Path $bep 'core\BepInEx.dll'
            $refs += Join-Path $bep 'core\0Harmony.dll'
            $refs = $refs | Where-Object { Test-Path $_ }
            $tmp = Join-Path $env:TEMP 'MCS-PTBR-build'
            New-Item -ItemType Directory -Path $tmp -Force | Out-Null
            $dll = Join-Path $tmp 'MenusPTBR.dll'
            $rsp = Join-Path $tmp 'build.rsp'
            $linhas = @('/nologo','/target:library','/optimize+','/nostdlib+','/codepage:65001', ('/out:"' + $dll + '"'))
            $linhas += $refs | ForEach-Object { '/r:"' + $_ + '"' }
            $linhas += ('"' + $fonte + '"')
            [IO.File]::WriteAllLines($rsp, $linhas, (New-Object Text.UTF8Encoding($false)))
            $saida = & $csc /noconfig ('@' + $rsp) 2>&1
            if ($LASTEXITCODE -eq 0 -and (Test-Path $dll)) {
                $velha = Join-Path $alvo 'MenusPTBR.dll'
                Copy-Item $dll $velha -Force
                Copy-Item $fonte (Join-Path $alvo 'MenusPTBR.cs') -Force
                $compilou = $true
                Write-Host 'Plugin compilado e instalado.' -ForegroundColor Green
            } else {
                $saida | Out-File (Join-Path $PSScriptRoot 'build.log') -Encoding utf8
                Write-Host 'A compilacao falhou. Veja build.log (ao lado do instalador) e abra uma issue anexando-o.' -ForegroundColor Red
            }
        }
        if (-not $compilou) {
            Write-Host 'Ficou a DLL antiga incluida: textos e texturas novos valem, mas as regras novas de codigo (ajustes-*.tsv, busca em portugues, layouts) so valem com a compilacao.' -ForegroundColor Yellow
        }
    }

    # Aviso sobre o mod em ingles.
    $pastaEn = Join-Path (Split-Path -Path $pastaMod -Parent) $modEn
    if (Test-Path $pastaEn) {
        Write-Host ''
        Write-Host 'ATENCAO: o mod de traducao em ingles (EN Translation) esta assinado.' -ForegroundColor Yellow
        Write-Host 'Desative-o no gerenciador de mods do jogo, senao ele conflita com a traducao PT-BR.' -ForegroundColor Yellow
    }
    Write-Host ''
    Write-Host 'Pronto! Abra o jogo.' -ForegroundColor Cyan
    Fechar 0
} catch {
    Write-Host ''
    Write-Host ('ERRO: ' + $_.Exception.Message) -ForegroundColor Red
    Fechar 1
}
