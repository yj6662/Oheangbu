namespace Oheangbu.Data.World
{
    // [SPEC-WORLD-MAP §4 층 2] 강토(五方) 식별자 — RealmSheetSO.realmId.
    // 순서 = LDB 강토 방위 표(청림=東·木 / 황경=中·土 / 적로=南·火 / 철옹=西·金 / 현강=北·水).
    // 직렬화는 정수 인덱스라 순서 변경 = 에셋 파손 — 끝에만 추가한다.
    public enum RealmId
    {
        Cheongrim = 0,   // 청림(靑林) — 木 · 東 · 첫 강토(금표의 길 S2)
        Hwanggyeong = 1, // 황경(黃京) — 土 · 中 · 도성(상경 가도 종점)
        Jeokro = 2,      // 적로(赤爐) — 火 · 南
        Cheolong = 3,    // 철옹(鐵甕) — 金 · 西
        Hyeongang = 4,   // 현강(玄江) — 水 · 北
    }
}
