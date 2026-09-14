# Offline compile check for one Oheangbu asmdef assembly without the Unity editor (MSBuild + Unity-generated csproj).
# Unity's csproj lists explicit <Compile Include> items, so freshly added .cs files are missing until the editor
# regenerates the project. This script copies the csproj to a temp file, replaces the Compile items with a
# wildcard over the assembly's script folder, optionally adds extra references (Library/ScriptAssemblies dlls),
# and builds it. Exit code = MSBuild exit code. Used by SPEC-WORLD-MAP P1 (MCP bridge down) — not a substitute
# for the editor's own compile; run assets-refresh when the bridge is back.
#
#   powershell -File Tools/Build/Compile-Assembly.ps1 -Assembly Oheangbu.App
#   powershell -File Tools/Build/Compile-Assembly.ps1 -Assembly Oheangbu.EditorTools -ExtraRefs Unity.AI.Navigation,Unity.AI.Navigation.Editor,Unity.Splines,Unity.Splines.Editor,Unity.ProBuilder
param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [string[]]$ExtraRefs = @(),
    [switch]$Quiet
)
$ErrorActionPreference = "Stop"
$root = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) "Oheangbu"
$csproj = Join-Path $root "$Assembly.csproj"
if (-not (Test-Path $csproj)) { Write-Error "csproj not found: $csproj (open the project in Unity once to generate it)"; exit 2 }
$msbuild = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) { Write-Error "MSBuild not found: $msbuild"; exit 2 }

# "</Project>" also closes every <ProjectReference><Project>{guid}</Project> metadata element, so only the LAST
# occurrence (the document root) may be used as the insertion anchor — a plain Replace corrupts the references (MSB3107).
function Insert-BeforeProjectEnd([string]$doc, [string]$block) {
    $i = $doc.LastIndexOf("</Project>")
    if ($i -lt 0) { throw "csproj has no </Project> root close" }
    return $doc.Substring(0, $i) + $block + $doc.Substring($i)
}

$folderMap = @{
    "Oheangbu.Core" = "Core"; "Oheangbu.Data" = "Data"; "Oheangbu.Drawing" = "Drawing"; "Oheangbu.BrushRender" = "BrushRender";
    "Oheangbu.Spellcraft" = "Spellcraft"; "Oheangbu.Combat" = "Combat"; "Oheangbu.Presentation" = "Presentation";
    "Oheangbu.App" = "App"; "Oheangbu.EditorTools" = "Editor"
}
if (-not $folderMap.ContainsKey($Assembly)) { Write-Error "unknown assembly $Assembly"; exit 2 }
$scriptDir = "Assets\_Project\Scripts\" + $folderMap[$Assembly]

$xml = Get-Content $csproj -Raw -Encoding UTF8
# Replace every explicit Compile item with one wildcard over the assembly folder.
$xml = [regex]::Replace($xml, '<Compile Include="[^"]*" />\s*', '')
$wild = "<Compile Include=`"$scriptDir\**\*.cs`" />`r`n"
$xml = [regex]::Replace($xml, '(<ItemGroup>\s*)(<None Include=)', ("`$1" + $wild.Replace('$', '$$') + "`$2"), 1)
if ($xml -notmatch [regex]::Escape($scriptDir)) {
    # No None-item group to anchor on; append a fresh ItemGroup before </Project>.
    $xml = Insert-BeforeProjectEnd $xml "<ItemGroup>`r`n$wild</ItemGroup>`r`n"
}
# Extra references from Library/ScriptAssemblies (package assemblies not yet listed by Unity's csproj).
$ExtraRefs = @($ExtraRefs | ForEach-Object { $_ -split "," } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($ExtraRefs.Count -gt 0) {
    $refs = ""
    foreach ($r in $ExtraRefs) {
        $dll = Join-Path $root "Library\ScriptAssemblies\$r.dll"
        if (-not (Test-Path $dll)) { Write-Warning "extra ref dll missing: $dll" }
        $refs += "<Reference Include=`"$r`"><HintPath>$dll</HintPath></Reference>`r`n"
    }
    $xml = Insert-BeforeProjectEnd $xml "<ItemGroup>`r`n$refs</ItemGroup>`r`n"
}
$tmp = Join-Path $root "$Assembly.offline.csproj"
Set-Content -Path $tmp -Value $xml -Encoding UTF8
try {
    $args = @($tmp, "/t:Build", "/p:Configuration=Debug", "/v:m", "/nologo", "/clp:ErrorsOnly;Summary", "/p:OutputPath=Temp\OfflineBin\$Assembly\")
    $out = & $msbuild @args 2>&1
    $code = $LASTEXITCODE
    if (-not $Quiet) { $out | Select-Object -Last 40 }
    if ($code -eq 0) { Write-Output "COMPILE_OK $Assembly" } else { Write-Output "COMPILE_FAIL $Assembly exit=$code" }
    exit $code
}
finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}
