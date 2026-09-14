# SPEC-WORLD-MAP §4 층 4 · §7 — Blender 랜드마크 계약 검사 래퍼(D8).
# Art/Blender/*.blend 전부(또는 -Name LM_<Name>)를 헤드리스로 검사(landmark_check.py)하고, PASS 산출물(FBX·footprint.json·report.json)만
# Oheangbu/Assets/_Project/Art/Models/Landmarks/ 로 복사한다(.meta는 Unity 임포트가 생성). FAIL이 하나라도 있으면 exit 1.
# Blender 탐색 순서: Tools/Blender/blender_path.local(gitignore, 한 줄 경로) → $env:BLENDER_EXE → 기본 설치 경로.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Invoke-LandmarkCheck.ps1 [-Name LM_Dummy] [-SkirtMin -0.5] [-MakeDummy]
param(
    [string]$Name = "",
    [double]$SkirtMin = -0.5,
    [switch]$MakeDummy,
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$blenderDir = Join-Path $root "Art\Blender"
$toolsDir = Join-Path $PSScriptRoot "Blender"
$checkScript = Join-Path $toolsDir "landmark_check.py"
$dummyScript = Join-Path $toolsDir "make_dummy_landmark.py"
$outRoot = Join-Path $toolsDir "_out"
$dest = Join-Path $root "Oheangbu\Assets\_Project\Art\Models\Landmarks"

function Find-Blender {
    $local = Join-Path $toolsDir "blender_path.local"
    if (Test-Path $local) {
        $p = (Get-Content $local -Raw).Trim()
        if ($p -and (Test-Path $p)) { return $p }
    }
    if ($env:BLENDER_EXE -and (Test-Path $env:BLENDER_EXE)) { return $env:BLENDER_EXE }
    $default = "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
    if (Test-Path $default) { return $default }
    return $null
}

$blender = Find-Blender
if (-not $blender) { Write-Error "Blender not found (Tools/Blender/blender_path.local | BLENDER_EXE | default install)"; exit 2 }
if (-not (Test-Path $checkScript)) { Write-Error "missing $checkScript"; exit 2 }

if ($MakeDummy) {
    & $blender --background --python $dummyScript
    if ($LASTEXITCODE -ne 0) { Write-Error "make_dummy_landmark.py failed exit=$LASTEXITCODE"; exit 2 }
}

if (-not (Test-Path $blenderDir)) { Write-Error "no $blenderDir (Art/Blender/LM_<Name>.blend expected)"; exit 2 }
$blends = @()
if ($Name) {
    $file = if ($Name.EndsWith(".blend")) { $Name } else { "$Name.blend" }
    $blends = @(Get-Item (Join-Path $blenderDir $file))
} else {
    $blends = @(Get-ChildItem -Path $blenderDir -Filter "LM_*.blend" -File)
}
if ($blends.Count -eq 0) { Write-Error "no .blend under $blenderDir"; exit 2 }

New-Item -ItemType Directory -Force $outRoot | Out-Null
$summary = @()
$anyFail = $false
foreach ($blend in $blends) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($blend.Name)
    $out = Join-Path $outRoot $stem
    New-Item -ItemType Directory -Force $out | Out-Null
    Get-ChildItem $out -File | Remove-Item -Force -Confirm:$false
    $skirt = $SkirtMin.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $log = & $blender --background $blend.FullName --python $checkScript -- --out $out --skirt-min $skirt
    $code = $LASTEXITCODE
    if (-not $Quiet) { $log | Where-Object { $_ -match "^LANDMARK_|^  FAIL|Error|Traceback" } }
    $reportPath = Join-Path $out "$stem.report.json"
    $result = "NO_REPORT"
    if (Test-Path $reportPath) { $result = (Get-Content $reportPath -Raw | ConvertFrom-Json).result }
    if ($code -eq 0 -and $result -eq "PASS") {
        New-Item -ItemType Directory -Force $dest | Out-Null
        foreach ($suffix in @(".fbx", ".footprint.json", ".report.json")) {
            Copy-Item (Join-Path $out "$stem$suffix") (Join-Path $dest "$stem$suffix") -Force
        }
        $summary += "PASS $stem -> $dest"
    } else {
        $anyFail = $true
        $summary += "FAIL $stem (exit=$code result=$result report=$reportPath)"
    }
}
Write-Output ""
Write-Output "== Landmark check summary =="
$summary | ForEach-Object { Write-Output $_ }
if ($anyFail) { exit 1 } else { exit 0 }
