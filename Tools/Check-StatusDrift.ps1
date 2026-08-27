# PROJECT_STATUS 드리프트 검사 — Docs/Specs/*.md의 상태와 PROJECT_STATUS.md 기재를 대조한다.
# 정책: 자동 "생성"이 아니라 자동 "검사"다. 불일치가 나오면 PROJECT_STATUS를 갱신한다
# (충돌 시 Spec이 정본 — PROJECT_STATUS는 Authority 없음, 설계 내용 복제 금지).
# 세션 시작 훅에서 실행된다. 출력이 없으면 드리프트 없음.
# 인코딩 주의: 이 파일은 UTF-8 BOM이어야 한다(PS 5.1은 무BOM 한글을 ANSI로 읽어 깨진다).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$statusPath = Join-Path $root "Docs\PROJECT_STATUS.md"
$specsDir = Join-Path $root "Docs\Specs"

if (-not (Test-Path $statusPath)) { Write-Output "[StatusDrift] PROJECT_STATUS.md 없음"; exit 0 }
if (-not (Test-Path $specsDir)) { exit 0 }

$statusLines = Get-Content $statusPath -Encoding UTF8
$stateWords = "PASS WITH CONDITIONS|PASS|LOCKED|TEST|DRAFT|PROPOSED|TBD|LEGACY"
$drift = @()

foreach ($spec in Get-ChildItem $specsDir -Filter "SPEC-*.md")
{
    $specName = [IO.Path]::GetFileNameWithoutExtension($spec.Name)
    $head = Get-Content $spec.FullName -Encoding UTF8 -TotalCount 20

    # 헤더 표에서 상태 행을 찾는다 — 신형(| 상태 |)과 구형(| State | / | Result | / | Migration Result |) 겸용.
    # Result(판정)가 있으면 그것이 우선, 없으면 State(문서 상태어)를 쓴다.
    $stateRow = ($head | Where-Object { $_ -match "^\|\s*(상태|State)\s*\|" } | Select-Object -First 1)
    $resultRow = ($head | Where-Object { $_ -match "^\|\s*[\w ]*Result\s*\|" } | Select-Object -First 1)

    $primary = $null
    foreach ($row in @($resultRow, $stateRow))
    {
        if ($row -and $row -match "($stateWords)") { $primary = $Matches[1]; break }
    }
    if (-not $primary) { $drift += "[?] $specName - 헤더에서 상태어를 읽지 못함"; continue }

    # PROJECT_STATUS에서 이 Spec을 언급한 줄에 같은 상태어가 있는가
    $mention = $statusLines | Where-Object { $_ -match [regex]::Escape($specName) } | Select-Object -First 1
    if (-not $mention)
    {
        $drift += "[누락] $specName ($primary) - PROJECT_STATUS에 미기재"
    }
    elseif ($mention -notmatch [regex]::Escape($primary))
    {
        $drift += "[불일치] $specName - Spec=$primary / STATUS 기재: $($mention.Trim())"
    }
}

if ($drift.Count -gt 0)
{
    Write-Output "[StatusDrift] PROJECT_STATUS.md 드리프트 감지 - Spec이 정본이다. STATUS를 갱신하라(설계 내용 복제 금지):"
    $drift | ForEach-Object { Write-Output "  $_" }
}
exit 0

