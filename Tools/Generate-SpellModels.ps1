# Generate-SpellModels.ps1 — Meshy Text-to-3D 술식 모델 생성 (SPEC-SPELL-FX-ASSETS §4.1·§4.6)
# 프롬프트의 단일 출처 = 이 스크립트(Generate-Motifs.ps1 선례). 키는 .env — 절대 출력 금지.
# 사용:
#   .\Generate-SpellModels.ps1 -Action balance
#   .\Generate-SpellModels.ps1 -Action create -Key so
#   .\Generate-SpellModels.ps1 -Action poll -TaskId <id>
#   .\Generate-SpellModels.ps1 -Action download -TaskId <id> -OutDir <dir>
param(
    [Parameter(Mandatory = $true)][ValidateSet("balance", "create", "poll", "download")][string]$Action,
    [string]$Key,
    [string]$TaskId,
    [string]$OutDir = "."
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# ---- 생성 명세 (어휘별 — 프롬프트 정본. 수정 이력은 git이 진다) ----
$Specs = @{
    # 소(金 광역 — 다연발 송곳 속사)의 송곳 낱개. 방향: 붓촉·먹 결(예준 2026-08-29 G2 문답).
    # 결합 프리팹에서 다연발은 파티클/배치가 담당 — 모델은 낱개 하나만.
    so = @{
        Letter          = "소"
        Prompt          = "A single elongated sharp spike shaped like a calligraphy brush tip, gentle organic curve, ink-drop teardrop taper narrowing to a needle point, stylized game projectile, clean bold silhouette, one single object centered, simple smooth surfaces, no base, no decoration"
        TargetPolycount = 1000
    }
    # 소 2차(12차 검수 — 「결정화된 느낌」, 각진 단결정 방향은 예준 문답 확정)
    so2 = @{
        Letter          = "소"
        Prompt          = "A single elongated sharp crystal spike, faceted hexagonal crystal shard, angular gemstone facets narrowing to a needle point, crystallized metal, stylized game projectile, clean bold silhouette, one single object centered, simple flat facets, no base, no decoration"
        TargetPolycount = 1000
    }
    # 고(木 광역 — 영역 가시 일제 솟음)의 가시 클러스터 낱개. 곡직(曲直) — 굽은 생목의 결(P4 플랜 승인).
    go = @{
        Letter          = "고"
        Prompt          = "A cluster of three gnarled curved wooden thorns, twisted living-wood spikes narrowing to sharp points, organic bramble growth, stylized game asset, clean bold silhouette, one single object centered, no base, no decoration"
        TargetPolycount = 3000
    }
    # 오(水 광역 — 전진하는 느린 파도)의 파도 crest. 윤하(潤下) — 낮게 말려 덮치는 물마루(P4 플랜 승인).
    o = @{
        Letter          = "오"
        Prompt          = "A single stylized curling ocean wave crest, smooth flowing curved water form breaking forward, wide arc shape, sculptural clean silhouette, one single object centered, simple smooth surfaces, no foam, no base, no decoration"
        TargetPolycount = 5000
    }
    # 마(土 단일 — 포물선 바위)의 바위 실체. 가색(稼穡) — 던져진 흙의 무게(SPELL-FIDELITY §4.5).
    ma = @{
        Letter          = "마"
        Prompt          = "A single rugged boulder rock, chunky angular stone with rough faceted surfaces, heavy compact mass, stylized game projectile, clean bold silhouette, one single object centered, simple low-poly facets, no base, no decoration, no moss"
        TargetPolycount = 1000
    }
}

# ---- 키 로드 (.env — 값은 변수로만, 어떤 경로로도 출력하지 않는다) ----
$apiKey = $null
foreach ($line in Get-Content (Join-Path $root ".env")) {
    if ($line -match "^MESHY_API_KEY=(.+)$") { $apiKey = $Matches[1].Trim() }
}
if (-not $apiKey) { Write-Error "MESHY_API_KEY not found in .env"; exit 1 }
$headers = @{ Authorization = "Bearer $apiKey" }

switch ($Action) {
    "balance" {
        $r = Invoke-RestMethod -Uri "https://api.meshy.ai/openapi/v1/balance" -Headers $headers -Method Get
        Write-Host "balance: $($r.balance)"
    }
    "create" {
        if (-not $Specs.ContainsKey($Key)) { Write-Error "unknown spec key: $Key"; exit 1 }
        $spec = $Specs[$Key]
        $body = @{
            mode             = "preview"
            prompt           = $spec.Prompt
            ai_model         = "meshy-5"        # 버전 고정 — 재현성·비용(§4.1)
            topology         = "triangle"
            target_polycount = $spec.TargetPolycount
            should_remesh    = $true
            target_formats   = @("glb", "fbx")
        } | ConvertTo-Json
        $r = Invoke-RestMethod -Uri "https://api.meshy.ai/openapi/v2/text-to-3d" -Headers $headers `
            -Method Post -Body $body -ContentType "application/json"
        Write-Host "letter: $($spec.Letter)"
        Write-Host "task: $($r.result)"
    }
    "poll" {
        $r = Invoke-RestMethod -Uri "https://api.meshy.ai/openapi/v2/text-to-3d/$TaskId" -Headers $headers -Method Get
        Write-Host "status: $($r.status) progress: $($r.progress)"
        if ($r.thumbnail_url) { Write-Host "thumbnail: $($r.thumbnail_url)" }
        if ($r.task_error -and $r.task_error.message) { Write-Host "error: $($r.task_error.message)" }
    }
    "download" {
        $r = Invoke-RestMethod -Uri "https://api.meshy.ai/openapi/v2/text-to-3d/$TaskId" -Headers $headers -Method Get
        if ($r.status -ne "SUCCEEDED") { Write-Error "task not SUCCEEDED: $($r.status)"; exit 1 }
        New-Item -ItemType Directory -Force $OutDir | Out-Null
        foreach ($fmt in @("glb", "fbx")) {
            $url = $r.model_urls.$fmt
            if ($url) {
                $dest = Join-Path $OutDir "$TaskId.$fmt"
                Invoke-WebRequest -Uri $url -OutFile $dest
                Write-Host "saved: $dest ($([math]::Round((Get-Item $dest).Length/1KB)) KB)"
            }
        }
        if ($r.thumbnail_url) {
            $dest = Join-Path $OutDir "$TaskId.png"
            Invoke-WebRequest -Uri $r.thumbnail_url -OutFile $dest
            Write-Host "saved: $dest"
        }
        Write-Host "consumed_credits(field): $($r.consumed_credits)"
    }
}
