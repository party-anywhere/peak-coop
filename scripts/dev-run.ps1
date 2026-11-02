# Para desenvolver: fecha o PEAK, compila, instala o mod e abre o jogo pela Steam (com o Hub aberto e, para testar
# sozinho, `pa phone-sim --players 3` rodando). Com as opções de Desenvolvimento ligadas no
# BepInEx\config\com.joaocaioo.peak-coop.cfg, o jogo entra sozinho na partida e tira prints em BepInEx\prints.
param([string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\PEAK')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Stop-Process -Name PEAK -Force -ErrorAction SilentlyContinue
dotnet build (Join-Path $root 'src') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'O build falhou.' }
& (Join-Path $PSScriptRoot 'dev-install.ps1') -GameDir $GameDir
Remove-Item (Join-Path $GameDir 'BepInEx\prints') -Recurse -Force -ErrorAction SilentlyContinue
Start-Process 'steam://rungameid/3527290'
Write-Host "PEAK abrindo. Log: $GameDir\BepInEx\LogOutput.log"
