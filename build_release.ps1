<#
.SYNOPSIS
    Script de build + packaging Gestion Couture v2.0
    Un seul script pour produire un Setup.exe prêt à installer chez le client.

.UTILISATION
    .\build_release.ps1                    # build complet (tests + publish + notice Inno)
    .\build_release.ps1 -SkipTests         # sauter les tests (rapide)
    .\build_release.ps1 -InnoSetupPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
                                           # compiler aussi le Setup.exe

.PRÉREQUIS
    - .NET SDK 8 (dotnet --version)
    - Inno Setup 6 (optionnel, https://jrsoftware.org/isinfo.php)
#>
param(
    [switch]$SkipTests,
    [string]$InnoSetupPath = ""
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host ""
Write-Host "======================================================"
Write-Host "  Gestion Couture — Build Release v2.0.0"
Write-Host "======================================================"
Write-Host ""

# ── 1. Tests ──────────────────────────────────────────────────────────────────
if (-not $SkipTests) {
    Write-Host "[1/3] Exécution des tests..."
    dotnet test GestionCoutureApp.Tests/GestionCoutureApp.Tests.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "ERREUR : des tests ont échoué. Corrigez-les avant de publier." -ForegroundColor Red
        exit 1
    }
    Write-Host "[1/3] Tests OK" -ForegroundColor Green
} else {
    Write-Host "[1/3] Tests ignorés (--SkipTests)" -ForegroundColor Yellow
}

Write-Host ""

# ── 2. Publication autonome ────────────────────────────────────────────────────
Write-Host "[2/3] Publication autonome (win-x64, self-contained)..."
dotnet publish GestionCoutureApp.csproj -c Release -p:PublishProfile=SelfContainedWin64 --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERREUR : échec de la publication." -ForegroundColor Red
    exit 1
}
$exeSize = [math]::Round((Get-ChildItem "publish\GestionCoutureApp.exe").Length / 1MB, 1)
Write-Host "[2/3] Binaire publié — publish\GestionCoutureApp.exe ($exeSize Mo)" -ForegroundColor Green
Write-Host ""

# ── 3. Installeur Inno Setup (optionnel) ──────────────────────────────────────
$innoDefault = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($InnoSetupPath -eq "" -and $innoDefault) {
    $InnoSetupPath = $innoDefault
}

if ($InnoSetupPath -ne "" -and (Test-Path $InnoSetupPath)) {
    Write-Host "[3/3] Compilation de l'installeur..."
    & $InnoSetupPath "installer\GestionCouture.iss"
    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERREUR : échec de la compilation Inno Setup." -ForegroundColor Red
        exit 1
    }
    $setup = Get-ChildItem "installer\Output\GestionCouture_Setup.exe" -ErrorAction SilentlyContinue
    if ($setup) {
        $setupSize = [math]::Round($setup.Length / 1MB, 1)
        Write-Host "[3/3] Installeur prêt — installer\Output\GestionCouture_Setup.exe ($setupSize Mo)" -ForegroundColor Green
    }
} else {
    Write-Host "[3/3] Inno Setup non trouvé — étape installeur ignorée." -ForegroundColor Yellow
    Write-Host "      Pour générer le Setup.exe :"
    Write-Host "        1. Installer Inno Setup 6 depuis https://jrsoftware.org/isinfo.php"
    Write-Host "        2. Relancer : .\build_release.ps1"
    Write-Host "          ou compiler manuellement : ISCC.exe installer\GestionCouture.iss"
}

Write-Host ""
Write-Host "======================================================"
Write-Host "  Build terminé avec succès !"
Write-Host "  Exécutable standalone : publish\GestionCoutureApp.exe"
if (Test-Path "installer\Output\GestionCouture_Setup.exe") {
    Write-Host "  Installeur client    : installer\Output\GestionCouture_Setup.exe"
}
Write-Host "======================================================"
Write-Host ""
