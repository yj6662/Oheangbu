# Generate target-look reference cuts for SPEC-SPIKE-WORLD-LOOKDEV via Recraft API (recraftv3).
# Reads RECRAFT_API_KEY from repo-root .env (never printed). Saves PNGs to Docs/Refs/Lookdev/.
# Text prompts only - no reference image upload. 3 cuts x 2 variants = 6 images.
# Size 1820x1024 (16:9) so the composition matches the in-game FOV 60 16:9 render cuts (Spec 5-12).
# The approved cut is copied by hand to approved_cut2.png and its histogram fixes the B1 band.
# Skeleton follows Generate-Motifs.ps1 (same endpoint, same WebP->PNG conversion trap).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root ".env"
$outDir = Join-Path $root "Docs\Refs\Lookdev"

$key = $null
foreach ($line in Get-Content $envFile) {
    if ($line -match "^RECRAFT_API_KEY=(.+)$") { $key = $Matches[1].Trim() }
}
if (-not $key) { Write-Error "RECRAFT_API_KEY not found in .env"; exit 1 }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }

# Look vocabulary (Spec 5-12): sumuk-damchae = ink wash WITH light color wash (not monochrome),
# sky = blank unpainted paper (yeobaek), distance = dissolving into paper (not fog), low saturation.
$commonLook = "traditional Korean ink wash landscape painting (sumuk damchae) on unpainted hanji paper, the sky left as blank unpainted paper, light color wash over ink, mostly ink tones with only faint muted color, minimal line work, distant ridges dissolving into blank paper, no fog effects (empty areas are simply blank paper), no people, no text, no border, low saturation"

$cuts = @(
    @{ Name = "ref_cut1"; Prompt = "view from deep inside a dark abandoned mine tunnel toward the small bright exit, heavy ink darkness on the walls, on the right wall a glowing mineral vein where the ink recedes and the pale paper shows through around it, the vein itself a dull muted teal-green, the only saturated color in the image. $commonLook" },
    @{ Name = "ref_cut2"; Prompt = "first-person view standing at the mouth of a dark mine tunnel, the timber frame of the entrance in deep ink silhouette, opening onto a bright morning mountain valley with a faint path, three layers of ridges getting lighter and fading into blank paper. $commonLook" },
    @{ Name = "ref_cut3"; Prompt = "morning mountain valley seen from the valley floor, a faint path leading away, three layers of ridges getting lighter with distance, a lone tall tree silhouette on a far ridge, wide blank paper sky, morning light from the front. $commonLook" }
)

$headers = @{ "Authorization" = "Bearer $key"; "Content-Type" = "application/json" }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

foreach ($cut in $cuts) {
    foreach ($v in @("a", "b")) {
        $outPath = Join-Path $outDir "$($cut.Name)_$v.png"
        if (Test-Path $outPath) { Write-Output "[skip] $($cut.Name)_$v exists"; continue }
        Write-Output "[gen] $($cut.Name)_$v ..."
        $body = @{ prompt = $cut.Prompt; style = "digital_illustration"; model = "recraftv3"; n = 1; size = "1820x1024" } | ConvertTo-Json
        $resp = Invoke-RestMethod -Method Post -Uri "https://external.api.recraft.ai/v1/images/generations" -Headers $headers -Body $body
        $url = $resp.data[0].url
        if (-not $url) { Write-Error "no url returned for $($cut.Name)_$v"; exit 1 }
        Invoke-WebRequest -Uri $url -OutFile $outPath
        Write-Output "[ok] $outPath"
    }
}
# Recraft returns WebP bytes even with .png names - convert in place.
python -c "from PIL import Image; import glob`nfor p in glob.glob(r'$outDir\ref_cut*.png'):`n    Image.open(p).save(p, 'PNG')"
Write-Output "DONE"
