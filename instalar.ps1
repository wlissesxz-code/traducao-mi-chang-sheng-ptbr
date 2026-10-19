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
