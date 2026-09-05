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
    # ---- 2026-09-03 실체 사실감 재작업(SPEC-SPELL-FX-REWORK, DECISIONS #142) ----
    # 오 2차: o 1차 실측이 폭 12% 핀(×24 스케일로 벽) → 「폭이 주축인 로브」로 재생성, 3개 지그재그 조합.
    o2 = @{
        Letter          = "오"
        Prompt          = "A single stylized breaking ocean wave, a thick curling crest rolling forward with a hooked overhanging lip, heavy rounded water mass beneath the curl, the crest line runs across the full width with a gently undulating lip edge, about as wide as it is tall and clearly wider than it is deep, sculptural clean silhouette, one single solid object centered, simple smooth surfaces, no foam particles, no droplets, no base plane, no decoration"
        TargetPolycount = 3000
    }
    # 오 로브 변형 B(가파른 립·두 갈래) — G1 룩 게이트에서 3로브 반복감이 읽힐 때만 생성.
    o3 = @{
        Letter          = "오"
        Prompt          = "A single stylized breaking ocean wave, a steep plunging crest with a thin overhanging lip beginning to split into two tongues, heavy rounded water mass beneath, crest line spanning the full width, about as wide as it is tall and clearly wider than it is deep, sculptural clean silhouette, one single solid object centered, simple smooth surfaces, no foam particles, no base plane, no decoration"
        TargetPolycount = 3000
    }
    # 고 2차: go 1차가 납작 4갈래 뭉치(풀포기) → 한 줄기 세장 덩굴 가닥(세장비 ≥6:1). no downward hooks=셰이더 성장 전선(z 단조) 전제.
    go2 = @{
        Letter          = "고"
        Prompt          = "A single thick bramble cane, one gnarled living-wood vine stem rising in a gentle S-curve, tapering to a sharp thorn tip, short curved thorns fused along the stem, twisted bark ridges, stylized game asset, clean bold silhouette, one single object centered, elongated vertical proportions, no base, no leaves, no separate parts, no downward hooks, no decoration"
        TargetPolycount = 900
    }
    # 고 변형 B(나선 덩굴손 — 목 성질 「굽음·감김」) — go2 룩 게이트 통과 후에만.
    go3 = @{
        Letter          = "고"
        Prompt          = "A single spiraling thorny vine tendril, one continuous living-wood stem coiling upward in a loose helix and tapering to a sharp point, small hooked thorns fused along the stem, stylized game asset, clean bold silhouette, one single object centered, elongated vertical proportions, no base, no leaves, no separate parts, no downward hooks, no decoration"
        TargetPolycount = 900
    }
    # 고 4차(예준 2026-09-04 지시): 「사방에서 올라와 천천히 바닥을 휘감고, 바닥을 뚫기도 위로 솟기도 하는
    # 나무 뿌리가 무작위로 자라는 듯한」 모습. 구현 = 긴 덩굴 1본이 아니라 **짧은 마디 체인**이다 —
    # 관절 높이를 지면고+사인 기복으로 잡으면 마디 피치가 저절로 나와 흙을 뚫고 솟는다(셰이더는 지면고를 모른다).
    # 따라서 필요한 것은 세장 줄기가 아니라 **마디 한 토막**(세장비 3:1). 폴리 = 마디가 최대 52개 깔리므로 12k/52 ≈ 230 상한.
    go4 = @{
        Letter          = "고"
        Prompt          = "A single gnarled woody stick lying down horizontally, one short knotty rod of old wood resting on its side like a fallen twig, gently bent along its length into a shallow arc, a swollen knobby lump near the middle, both ends cut blunt and roughly equal in width, coarse fibrous bark ridges running lengthwise, the whole piece is long and low and lies flat, stylized low-poly game asset, clean bold silhouette, one single connected solid object centered, elongated horizontal bar proportions about four times longer than thick, no upright orientation, no standing pose, no trunk, no stump, no flared base, no cone shape, no pedestal, no root crown, no branching, no thorns, no leaves, no soil, no dirt, no rocks, no ground plane, no base, no separate parts, no decoration"
        TargetPolycount = 200
    }
    # 고 5차(예준 2026-09-05): 「더 굵게 · Realistic 한 메시로 다시」.
    # go4 산출이 저폴리 양식화로 나온 원인은 art_style이 아니라 프롬프트의 "stylized low-poly game asset,
    # clean bold silhouette" — 양식화를 문구로 직접 지시하고 있었다. 그 문구를 걷어내고 사실적 어휘로 교체한다.
    # 굵기: 세장비 4:1 -> 2.5:1. 폴리는 300(사실적 요철은 폴리를 먹는다) — 마디 수 상한으로 예산을 잠근다.
    go5 = @{
        Letter          = "고"
        ArtStyle        = "realistic"
        Prompt          = "A realistic long thick tree root lying horizontally on its side, a heavy woody root of an old tree stretching lengthwise, naturally gnarled and irregular with organic swellings and a knotted burl near the middle, deeply furrowed rough bark with wrinkled fibrous lengthwise grain, both ends broken off blunt and roughly equal in girth, slightly bent along its length, photorealistic natural organic form, highly detailed weathered wood, one single connected solid object centered, clearly longer than it is thick, elongated proportions about three times longer than thick, not a boulder or lump, no upright standing pose, no trunk, no stump, no flared base, no root crown, no branching, no leaves, no soil, no ground plane, no base, no separate parts"
        TargetPolycount = 400
    }
    # 고 6차(예준 2026-09-05 「굵기도 좀 다양하고 모양도 다양하면」): go5의 가는 변종.
    # 같은 사실적 어휘를 쓰되 성격을 가른다 — go5=굵고 옹이진 몸통 / go6=가늘고 길게 뻗으며 꼬인 곁뿌리.
    # 효과 쪽은 _vineMeshes[] 배열과 _primaryMeshCount로 이미 A/B를 섞으므로 배선만 하면 된다.
    go6 = @{
        Letter          = "고"
        ArtStyle        = "realistic"
        Prompt          = "A single unbranched piece of old woody stick lying down horizontally, one solid rod of weathered wood resting on its side like a fallen branch, no forks and no side twigs, gently bent along its length into a shallow arc, a knotted swelling near the middle, both ends broken off blunt, deeply furrowed realistic bark with wrinkled lengthwise grain, photorealistic natural organic wood, highly detailed, one single connected solid object centered, slim rod proportions about four times longer than thick, no upright standing pose, no trunk, no stump, no root crown, no flared base, no branching, no twigs, no leaves, no soil, no ground plane, no base, no separate parts, no loose fragments"
        TargetPolycount = 350
    }
    # 소 3차(조건부): 0단계(InkMetal+플랫 법선, 기존 CrystalSpike_So)에서 「결정이지 쇠가 아니다」 판정일 때만 — 벼린 사각 송곳.
    so3 = @{
        Letter          = "소"
        Prompt          = "A single hand-forged iron awl spike, square cross-section tapering evenly to a long needle point, slightly twisted shaft, hammered faceted metal surface with crisp hard edges, short thick collar at the blunt end, stylized game projectile, clean bold silhouette, one single object centered, simple flat facets, no handle, no wooden parts, no base, no decoration"
        TargetPolycount = 700
    }
    so3b = @{
        Letter          = "소"
        Prompt          = "A single forged steel spike, octagonal cross-section tapering evenly to a needle point, straight shaft, crisp flat facets with hard edges, short flared collar at the blunt end, hammered metal look, stylized game projectile, clean bold silhouette, one single object centered, simple flat facets, no handle, no base, no decoration"
        TargetPolycount = 700
    }
    # 노(火 광역 — 화염 방사)의 불혀 낱개 — 30개 풀로 항력 방사(FlameJetEffect). 화 모티프 「불꽃 한 줄기」의 3D 번안.
    no = @{
        Letter          = "노"
        Prompt          = "A single stylized flame tongue, one tall tapering tongue of fire rising from a rounded thick base to a sharp pointed tip, gently twisting as it rises, with two smaller side licks splitting off partway up, sculptural low-poly game effect mesh, clean bold silhouette, one single object centered, simple faceted surfaces, solid closed watertight mesh, no base plate, no embers, no sparks, no smoke, no decoration"
        TargetPolycount = 400
    }
    # 모(土 광역 — 직선 경로 모래폭풍) 전면 벽 — 기본안은 Blender 메타볼 절차 생성(0크레딧). 이 키는 G1 게이트에서 「풍선/젤리」 판정 시에만.
    mo = @{
        Letter          = "모"
        Prompt          = "A single stylized advancing dust storm front, a low wide rolling wall of dense billowing sand cloud, several bulging rounded lobes tumbling forward along the leading face, thick heavy mass at the bottom thinning toward the top, flat bottom resting on the ground, flat vertical cut on the rear side, clearly wider than tall and taller than deep, sculptural clean silhouette, one single solid object centered, simple smooth chunky forms, no ground plane, no base, no scattered particles, no decoration"
        TargetPolycount = 2000
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
        }
        # 스펙이 ArtStyle을 지정하면 실어 보낸다(미지정 키는 API 기본값 — 기존 생성물 재현성 무손상)
        if ($spec.ContainsKey("ArtStyle")) { $body["art_style"] = $spec.ArtStyle }
        $body = $body | ConvertTo-Json
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
