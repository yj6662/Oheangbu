# Generate 5-element Korean ink-painting motif textures via Recraft API (SPEC-ART-INK-LOOK).
# Reads RECRAFT_API_KEY from repo-root .env. Saves PNGs to Assets/_Project/Art/Motifs/.
# Motif subjects are TEST candidates (user examples: wood=plum/bamboo, fire=flames).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $root ".env"
$outDir = Join-Path $root "Oheangbu\Assets\_Project\Art\Motifs"

$key = $null
foreach ($line in Get-Content $envFile) {
    if ($line -match "^RECRAFT_API_KEY=(.+)$") { $key = $Matches[1].Trim() }
}
if (-not $key) { Write-Error "RECRAFT_API_KEY not found in .env"; exit 1 }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }

# Small single-element sprites (user direction 2026-08-27): a few tiny sprites attach to the
# drawn letter to read the element at a glance - NOT a full painting behind it.
$common = "one single small object only, centered, traditional Korean ink wash painting style with light color wash, simple bold silhouette readable at tiny size, isolated on a pure white background, minimalist, no text, no border"

$jobs = @(
    @{ Name = "T_Motif_Mok";  Prompt = "a single small bamboo leaf sprig with two or three leaves, muted teal-green wash. $common" },
    @{ Name = "T_Motif_Hwa";  Prompt = "a single small rising flame tongue, muted vermilion orange wash. $common" },
    @{ Name = "T_Motif_To";   Prompt = "a single small rounded rock stone, muted ochre yellow-brown wash. $common" },
    @{ Name = "T_Motif_Geum"; Prompt = "a single small four-pointed star sparkle glint, silvery gray wash. $common" },
    @{ Name = "T_Motif_Su";   Prompt = "a single small water droplet with a tiny splash, deep indigo blue wash. $common" }
)

$headers = @{ "Authorization" = "Bearer $key"; "Content-Type" = "application/json" }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

foreach ($job in $jobs) {
    $outPath = Join-Path $outDir "$($job.Name).png"
    if (Test-Path $outPath) { Write-Output "[skip] $($job.Name) exists"; continue }
    Write-Output "[gen] $($job.Name) ..."
    $body = @{ prompt = $job.Prompt; style = "digital_illustration"; model = "recraftv3"; n = 1; size = "1024x1024" } | ConvertTo-Json
    $resp = Invoke-RestMethod -Method Post -Uri "https://external.api.recraft.ai/v1/images/generations" -Headers $headers -Body $body
    $url = $resp.data[0].url
    if (-not $url) { Write-Error "no url returned for $($job.Name)"; exit 1 }
    Invoke-WebRequest -Uri $url -OutFile $outPath
    Write-Output "[ok] $outPath"
}
# Recraft returns WebP bytes even with .png names - convert in place so Unity can import.
python -c "from PIL import Image; import glob`nfor p in glob.glob(r'$outDir\T_Motif_*.png'):`n    Image.open(p).save(p, 'PNG')"
Write-Output "DONE"
