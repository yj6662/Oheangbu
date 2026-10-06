using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Oheangbu.App.World.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>#308 menu text diet checks (SPEC-PLAYTEST-TEXT-DIET "D308-27 메뉴 쪽 글 줄이기", AC-TD27.1 .. .8).
    /// Queue: Oheangbu.EditorTools.WorldMacro.MenuText308 Execute
    ///   "static"                      Edit or Play. AC-TD27.8: the tables below are consistent (every removed example is refused,
    ///                                 every kept example is allowed), the members that drew the removed texts are gone.
    ///   "page:&lt;id&gt;" | "all"          Play, unpaused, a gameplay session (the pages exist only at runtime). id = pause, inventory,
    ///                                 codex, chapae, options, controls, service (service = 정비, only beside a shelter).
    ///                                 Opens the page, walks its tabs and every 칸, and prints one PASS / FAIL line per AC.
    ///   "large:&lt;id&gt;" | "large:all"   the same under an unsaved preview of UI 크기 1.2 + 본문 크기 1.25 (reverted in finally).
    /// AC-TD27.5 (what must still stand: the audit's [D] / [E] texts and what answer 2 keeps - the pause slip, the centre head, the
    /// bottom key line) is the table Needed; its conditional groups are demanded only when the session can show them and are
    /// printed as "NOT DEMANDED here" otherwise.
    /// Never opens a dialog; a refusal is a returned string. Each run writes Art/Playtest308/MenuText/menutext308_&lt;command&gt;.txt and
    /// ends with "RESULT ... PASS n / FAIL n". Synchronous. Like PlaytestMenuReview layout-check it opens pages through OpenPage
    /// (the first one saves the session) and it selects every 칸 (new-content marks of this Play session are looked at).
    /// The allow list is the table R below; Tools/Unity/Stage308_menutext/_Tools/menutext_static.py reads the same lines offline.</summary>
    public static class MenuText308
    {
        // ------------------------------------------------------------------ tables
        // One entry per line, verbatim strings only (the offline check parses these lines). Paths are the object names from the
        // page root down ("/PageContent/Resume/Label", "/MenuRail304/Tab_소지품/Label"), matched by a regex that ends at the leaf
        // (the examples carry a longer path: only its end matters); texts are matched whole
        // after rich-text tags are taken out. Kinds: D fixed word, V value of a fixed form, N a name from data, E reading matter
        // (CSV 효과, 의뢰 대사), K a key.
        sealed class Rule { public string Page, Path, Text, Kind; public Regex PathRx, TextRx; }
        sealed class Entry { public string Page, Audit, Path, Text; public Regex PathRx, TextRx; }

        static Rule R(string page, string path, string text, string kind)
            => new Rule { Page = page, Path = path, Text = text, Kind = kind, PathRx = new Regex(path, RegexOptions.CultureInvariant), TextRx = Whole(text) };
        // X / K: an example text at an example path (the two characters \n in an example stand for a line break)
        static Entry X(string page, string audit, string path, string text) => new Entry { Page = page, Audit = audit, Path = path, Text = text.Replace(@"\n", "\n") };
        static Entry K(string page, string path, string text) => new Entry { Page = page, Audit = "", Path = path, Text = text.Replace(@"\n", "\n") };
        static Entry G(string page, string audit, string pattern) => new Entry { Page = page, Audit = audit, Path = "", Text = pattern, TextRx = new Regex(pattern, RegexOptions.CultureInvariant) };
        static Entry O(string page, string audit, string path) => new Entry { Page = page, Audit = audit, Path = path, Text = "", PathRx = new Regex(path, RegexOptions.CultureInvariant) };
        static Entry N(string page, string path, string text) => new Entry { Page = page, Audit = "", Path = path, Text = text, PathRx = new Regex(path, RegexOptions.CultureInvariant), TextRx = Whole(text) };
        static Regex Whole(string pattern) => new Regex(@"\A(?:" + pattern + @")\z", RegexOptions.CultureInvariant);

        // allow308-begin : AC-TD27.1. What may stand on a page after D308-27.
        static readonly Rule[] Rules =
        {
            R(@"rail", @"/MenuRail304/Key_(Q|E)/Label$", @"Q|E", @"K"),
            R(@"rail", @"/MenuRail304/Tab_[^/]+/Label$", @"일시정지|소지품|술식|차패|지도|설정|조작|정비", @"D"),
            R(@"rail", @"/MenuRail304/Close/Hint/Key_[^/]+/Label$", @"Esc|M", @"K"),
            R(@"rail", @"/MenuRail304/Close/Hint/Label$", @"닫기", @"D"),

            R(@"pause", @"/PageContent/Resume/Label$", @"계속하기", @"D"),
            R(@"pause", @"/PageContent/Resume/(Key|KeyFocused)/Label$", @"Esc", @"K"),
            R(@"pause", @"/PageContent/Tab_[^/]+/Label$", @"소지품|술식|차패|지도|설정|조작", @"D"),
            R(@"pause", @"/PageContent/Tab_소지품/(Key|KeyFocused)/Label$", @"I", @"K"),
            R(@"pause", @"/PageContent/Tab_지도/(Key|KeyFocused)/Label$", @"M", @"K"),
            R(@"pause", @"/PageContent/Tab_소지품/Meta$", @"(장비 \d+  )?석경 조각 \d+", @"V"),
            R(@"pause", @"/PageContent/Tab_술식 도감/Meta$", @"석경에 남은 술식 \d+ / \d+", @"V"),
            R(@"pause", @"/PageContent/Tab_차패/(Virtues|VirtuesFocused)$", @"仁 禮 義 智 信", @"D"),
            R(@"pause", @"/PageContent/Tab_차패/VirtueCount$", @"오덕 \d / 5", @"V"),
            R(@"pause", @"/PageContent/ReturnTitle/Label$", @"타이틀로", @"D"),
            R(@"pause", @"/PageContent/CommissionMeta$", @"진행 중인 의뢰", @"D"),
            R(@"pause", @"/PageContent/CommissionTitle$", @"[\s\S]+", @"N"),
            R(@"pause", @"/PageContent/CommissionLine$", @"[\s\S]+", @"E"),
            R(@"pause", @"/PageContent/SaveState$", @"여정 기록됨|저장하지 못했다", @"D"),
            R(@"pause", @"/PageContent/SaveRestart$", @".*에서 재시작", @"V"),
            R(@"pause", @"/PageContent/SaveCoinName$", @"조선통보", @"D"),
            R(@"pause", @"/PageContent/SaveCoins$", @"\d[\d,.]*", @"V"),
            R(@"pause", @"/PageContent/RegionSlip/(Region|Place)$", @"[\s\S]+", @"N"),

            R(@"inventory", @"/PageContent/(EquipmentTab|FragmentsTab)/Label$", @"장비|석경", @"D"),
            R(@"inventory", @"/PageContent/(EquipmentTab|FragmentsTab)/Count$", @"\d+", @"V"),
            R(@"inventory", @"/PageContent/Currency/Label$", @"조선통보", @"D"),
            R(@"inventory", @"/PageContent/Currency/Value$", @"\d[\d,.]*", @"V"),
            R(@"inventory", @"/EquipStatus308/Status_drop$", @"남긴 통보 \d[\d,.]*", @"V"),
            R(@"inventory", @"/EquipStatus308/(StatusBody|StatusPower|StatusTier|StatusLearned)$", @"몸과 먹|속성 위력|보강|익힌 것", @"D"),
            R(@"inventory", @"/EquipStatus308/Status_(?!virtues)[^/]+/Name$", @"체력|먹|먹 회복|[木火土金水] [목화토금수]|먹 용량|술식", @"D"),
            R(@"inventory", @"/EquipStatus308/Status_(?!virtues)[^/]+/Value$", @"\d+ / \d+|초당 \d+(\.\d)?|[+\-]\d+%", @"V"),
            R(@"inventory", @"/Equipped_[^/]+/SlotName$", @"(붓|머리|몸|손|발|장신구)( \+\d+)?", @"D"),
            R(@"inventory", @"/Stone_[^/]+/SlotName$", @"[목화토금수]", @"D"),
            R(@"inventory", @"/Stone_[^/]+/Element_\d/Hanja$", @"[木火土金水]", @"D"),
            R(@"inventory", @"/VirtueSlot_\d/SlotName$", @"[인예의지신]", @"D"),
            R(@"inventory", @"/VirtueSlot_\d/Glyph$", @"[仁禮義智信]", @"D"),
            R(@"inventory", @"/Fragment_[^/]+/Glyph$", @"[가-힣]", @"D"),
            R(@"inventory", @"/PageContent/EmptyTitle$", @"빈 봇짐", @"D"),
            R(@"inventory", @"/EquipInfo308/SelectedGearSlot$", @"붓|머리|몸|손|발|장신구|오행 마석|오덕|석경 조각", @"D"),
            R(@"inventory", @"/EquipInfo308/SelectedGearTitle$", @"[\s\S]+", @"N"),
            R(@"inventory", @"/EquipInfo308/ItemTitle$", @"[가-힣]의 석경 조각", @"D"),
            R(@"inventory", @"/EquipInfo308/SelectedGearLevel$", @"(\+\d+ 강화|강화 전)( · 착용 중)?|\d+단|새겨짐|새기지 못함|쓰임|지닌 수 \d+|맞는 장비 없음", @"D"),
            R(@"inventory", @"/EquipInfo308/GearError$", @"장비 저장을 확인해야 한다\.", @"D"),
            R(@"inventory", @"/EquipInfo308/(EffectHead|CompareHead|UpgradeHead|SourceHead)$", @"효과|견줌|다음 강화|나온 곳", @"D"),
            R(@"inventory", @"/EquipInfo308/SelectedGearEffect$", @"(최대 체력|최대 먹|[목화토금수] 속성 위력|모든 속성 위력) \+\d+%", @"V"),
            R(@"inventory", @"/EquipInfo308/EffectParts$", @"기본 \+\d+% · 강화 \+\d+%", @"V"),
            R(@"inventory", @"/EquipInfo308/SelectedGearCompare$", @"교체 [+\-]\d+%p", @"V"),
            R(@"inventory", @"/EquipInfo308/UpgradeNext$", @"\+\d+ 강화|강화 완료|\d+단 \+\d+%", @"V"),
            R(@"inventory", @"/EquipInfo308/UpgradeWhere$", @".+에게서 조선통보 \d[\d,.]*|(쉼터 · )?조선통보 \d[\d,.]*", @"V"),
            R(@"inventory", @"/EquipInfo308/LearnedHead$", @"익힌 [목화토금수] 술식 \d+ / \d+", @"V"),
            R(@"inventory", @"/EquipInfo308/LearnedGlyph$", @"[가-힣]", @"D"),
            R(@"inventory", @"/EquipInfo308/Source$", @"[\s\S]+", @"N"),
            R(@"inventory", @"/EquipInfo308/GearTransactionError$", @"[\s\S]+", @"N"),
            R(@"inventory", @"/EquipInfo308/ItemArt/Art/(Element_\d/Hanja|Glyph|ItemGlyph)$", @"[木火土金水仁禮義智信가-힣]", @"D"),
            R(@"inventory", @"/EquipInfo308/GearEquip/Label$", @"착용|해제", @"D"),
            R(@"inventory", @"/EquipInfo308/GearEquip/(Key|KeyFocused)/Label$", @"Enter", @"K"),
            R(@"inventory", @"/GearDetail304/CandidatesHead$", @"바꿔 낄 것 \d+", @"V"),
            R(@"inventory", @"/GearDetail304/(.+/)?Gear_[^/]+/Label$", @"[\s\S]+", @"N"),
            R(@"inventory", @"/GearDetail304/(.+/)?Gear_[^/]+/Meta$", @"착용 중|(최대 체력|최대 먹|[목화토금수] 속성 위력|모든 속성 위력) \+\d+%", @"V"),
            R(@"inventory", @"/EquipLegend308/Legend_\d/Label$", @"바꿔 끼기|빼기|착용|해제|칸으로|정비|차패|술식 도감에서 보기", @"D"),
            R(@"inventory", @"/EquipLegend308/Legend_\d/Key_[^/]+/Label$", @"Enter|Esc|끌기|[A-Z]", @"K"),

            R(@"codex", @"/PageContent/KnownCount$", @"\d+", @"V"),
            R(@"codex", @"/PageContent/KnownLabel$", @"석경에 남은 술식", @"D"),
            R(@"codex", @"/PageContent/TotalCount$", @"/ \d+", @"V"),
            R(@"codex", @"/PageContent/ExtraLabel$", @"그 밖에 익힌 글자", @"D"),
            R(@"codex", @"/PageContent/Element_\d/Hanja$", @"[木火土金水]", @"D"),
            R(@"codex", @"/PageContent/ColumnName$", @"[목화토금수]", @"D"),
            R(@"codex", @"/PageContent/ColumnMeta$", @"[ᄀ-ᇿㄱ-ㅣ]?  \d+ / \d+", @"V"),
            R(@"codex", @"/PageContent/RowJamo$", @"[ᄀ-ᇿㄱ-ㅣ]{1,3}", @"D"),
            R(@"codex", @"/PageContent/(RowKind|RowFrame)$", @"[\s\S]+", @"N"),
            R(@"codex", @"/PageContent/Codex_[^/]+/Glyph$", @"[가-힣]", @"D"),
            R(@"codex", @"/PageContent/Codex_[^/]+/Name$", @"(?!미발견$)[\s\S]+", @"N"),
            R(@"codex", @"/CodexDetail304/Element_\d/Hanja$", @"[木火土金水]", @"D"),
            R(@"codex", @"/CodexDetail304/TagElement$", @"[목화토금수]", @"D"),
            R(@"codex", @"/CodexDetail304/(TagFrame|TagCategory)$", @"[\s\S]+", @"N"),
            R(@"codex", @"/CodexDetail304/SpellGlyph$", @"[가-힣]", @"D"),
            R(@"codex", @"/CodexDetail304/Composition$", @"[ᄀ-ᇿㄱ-ㅣ] \+ [ᄀ-ᇿㄱ-ㅣ]( \+ [ᄀ-ᇿㄱ-ㅣ]{1,2})?", @"D"),
            R(@"codex", @"/CodexDetail304/FinalHead$", @"받침", @"D"),
            R(@"codex", @"/CodexDetail304/FinalGlyph$", @"[가-힣]", @"D"),
            R(@"codex", @"/CodexDetail304/FinalJamo$", @"[ᄀ-ᇿㄱ-ㅣ]{1,2}", @"D"),
            R(@"codex", @"/CodexDetail304/FinalText$", @"[\s\S]+", @"E"),
            R(@"codex", @"/CodexDetail304/ExampleLabel$", @"쓰기 예시", @"D"),
            R(@"codex", @"/CodexDetail304/ReplayHint/Label$", @"획 다시 보기", @"D"),
            R(@"codex", @"/CodexDetail304/ReplayHint/Key_Enter/Label$", @"Enter", @"K"),
            R(@"codex", @"/CodexDetail304/Film_\d/(FallbackGlyph|OrderNo)$", @"[가-힣]|\d+", @"D"),
            R(@"codex", @"/CodexDetail304/SpellEffect$", @"(?!아직 석경에서)[\s\S]+", @"E"),
            R(@"codex", @"/CodexDetail304/SpellRange$", @"[\s\S]+", @"E"),
            R(@"codex", @"/CodexDetail304/(ShengLabel|KeLabel)$", @"상생|상극", @"D"),
            R(@"codex", @"/CodexDetail304/(Sheng|Ke)$", @"[木火土金水] → [木火土金水] → [木火土金水]", @"D"),

            R(@"chapae", @"/PageContent/Identity/Title$", @"差\s*牌", @"D"),
            R(@"chapae", @"/PageContent/Identity/IdentityLabel$", @"오행부 소속\n전직 집행관", @"D"),
            R(@"chapae", @"/PageContent/Virtue_\d/Glyph$", @"[仁禮義智信]", @"D"),
            R(@"chapae", @"/PageContent/VirtueName_\d$", @"인 · 목|예 · 화|의 · 금|지 · 수|신 · 토", @"D"),
            R(@"chapae", @"/PageContent/VirtueState_0$", @"쓰임", @"D"),

            R(@"options", @"/PageContent/OptionTab_[^/]+/Label$", @"화면|소리|조작|접근성", @"D"),
            R(@"options", @"/PageContent/Value_[^/]+/Label$", @"해상도|화면 모드|품질|수직동기화|프레임 제한|전체 음량|게임 효과음|UI 효과음|마우스 감도|시점 Y축 반전|익힘 멈춤|UI 크기|본문 크기|미니맵|방위선|지도 펼침 동작 줄이기|임팩트 프레임|HUD 반응", @"D"),
            R(@"options", @"/PageContent/Value_[^/]+/Meta$", @"적용 전", @"D"),
            R(@"options", @"/PageContent/Value_[^/]+/(Prev|Next)$", @"‹|›", @"D"),
            R(@"options", @"/PageContent/Value_(?!품질)[^/]+/Value$", @"\d+ × \d+|창 모드|전체 화면|최대화 창|테두리 없는 전체 화면|켜기|끄기|제한 없음|\d+ fps|\d+%|\d\.\d\d×|북쪽 고정|시점 따라|숨기기|표시|전체|줄임|끔", @"V"),
            R(@"options", @"/PageContent/Value_품질/Value$", @"[^\n]+", @"N"),
            R(@"options", @"/PageContent/Value_[^/]+/Reason$", @"수직동기화 사용 중", @"D"),
            R(@"options", @"/PageContent/ApplyDisplay/Label$", @"화면 설정 적용", @"D"),
            R(@"options", @"/PageContent/ApplyDisplay/(Key|KeyFocused)/Label$", @"Enter", @"K"),
            R(@"options", @"/PageContent/ResetSettings/Label$", @"기본값 복원", @"D"),
            R(@"options", @"/PageContent/Listen/Label$", @"효과음 들어보기", @"D"),
            R(@"options", @"/PageContent/CurrentDisplayBlock/CurrentDisplay$", @"지금 쓰는 화면", @"D"),
            R(@"options", @"/PageContent/CurrentDisplayBlock/CurrentName_\d$", @"해상도|화면 모드|수직동기화", @"D"),
            R(@"options", @"/PageContent/CurrentDisplayBlock/CurrentValue_\d$", @"\d+ × \d+|창 모드|전체 화면|최대화 창|테두리 없는 전체 화면|켜기|끄기", @"V"),
            R(@"options", @"/PageContent/SettingsError$", @"[\s\S]+", @"N"),

            R(@"controls", @"/PageContent/ControlsTab_[^/]+/Label$", @"이동|작도|메뉴", @"D"),
            R(@"controls", @"/PageContent/Key_\d+/Key_[^/]+/Label$", @"W|A|S|D|Ctrl|Space|C|X|왼쪽 Shift|Tab|F|V|G|Q|M|I|Esc|E|마우스|좌클릭|우클릭|휠", @"K"),
            R(@"controls", @"/PageContent/Key_\d+/(Word|Sep)$", @"이동|유지|/|\+", @"D"),
            R(@"controls", @"/PageContent/Action_\d+$", @"걷기 / 탑승 중 가속·조향|달리기 켜기·끄기|달리기|지상 점프 / 탑승 중 제동|웅크리기 전환 / 바닥 착석·일어나기|시점 이동|회피 / 락온|조사, 대화, 석경 파편 획득, 자동차 탑승·하차|탑승 중 시점 전환|자동차 부르기·거두기|글씨 그리기|먹 갈무리|지도 / 소지품 / 일시정지·뒤로|메뉴 탭 넘기기", @"D"),

            R(@"service", @"/PageContent/ShopPlace$", @"[^\n]+", @"N"),
            R(@"service", @"/PageContent/Currency/Label$", @"조선통보", @"D"),
            R(@"service", @"/PageContent/Currency/Value$", @"\d[\d,.]*", @"V"),
            R(@"service", @"/PageContent/GroupStones$", @"마석 · 속성 위력", @"D"),
            R(@"service", @"/PageContent/GroupBody$", @"보강", @"D"),
            R(@"service", @"/PageContent/Buy_[^/]+/Label$", @"[목화토금수] 마석|체력 보강|먹 용량 보강", @"D"),
            R(@"service", @"/PageContent/Buy_[^/]+/Meta$", @"강화 완료|통보가 모자라다", @"D"),
            R(@"service", @"/PageContent/Buy_[^/]+/(Key|KeyFocused)/Label$", @"Enter", @"K"),
            R(@"service", @"/PageContent/Buy_[^/]+/Element_\d/Hanja$", @"[木火土金水]", @"D"),
            R(@"service", @"/PageContent/Buy_[^/]+/UpgradeLevel_[^/]+$", @"\+\d+%", @"V"),
            R(@"service", @"/PageContent/Buy_[^/]+/UpgradeNext_[^/]+$", @"→ \+\d+%", @"V"),
            R(@"service", @"/PageContent/Buy_[^/]+/UpgradePrice_[^/]+$", @"\d[\d,.]*", @"V"),
        };
        // allow308-end

        // gone308-begin : AC-TD27.2. Texts that may not stand anywhere on the page, shown or hidden (regex, searched in the text).
        static readonly Entry[] GoneTexts =
        {
            G(@"pause", @"일시정지 ①", @"잠시 붓을 내려놓는다"),
            G(@"pause", @"일시정지 ③", @"화면과 소리, 조작, 접근성"),
            G(@"pause", @"일시정지 ④", @"키보드와 마우스"),
            G(@"pause", @"일시정지 ⑤", @"진행을 저장하고 로비로 돌아간다"),
            G(@"inventory", @"소지품 ①", @"^의복 · "),
            G(@"inventory", @"소지품 ③", @"칸에 놓아 착용"),
            G(@"inventory", @"소지품 ④", @"(착용 중인 .+|비어 있는 .+ 칸과) 비교$"),
            G(@"inventory", @"소지품 ⑤", @"쉼터 곁에서만 열린다"),
            G(@"inventory", @"소지품 ⑥", @"오행 마석 · 속성 위력"),
            G(@"inventory", @"소지품 ⑧", @"쓰였다"),
            G(@"inventory", @"소지품 ⑨", @"^(체력|먹 용량) 보강$"),
            G(@"inventory", @"소지품 ⑪", @"^[가-힣]의 석경$"),
            G(@"inventory", @"소지품 ⑫", @"석경 조각을 찾으면 여기 모인다"),
            G(@"codex", @"술식 ②", @"^(초성|중성) "),
            G(@"codex", @"술식 ③", @"아직 석경에서 찾지 못한 술식"),
            G(@"codex", @"술식 ⑤", @"^긋기$|떼면 발동"),
            G(@"chapae", @"차패 ①", @"새기지 못함|새겨짐"),
            G(@"chapae", @"차패 ②", @"자동차 부르기"),
            G(@"chapae", @"차패 ③", @"하차 30m"),
            G(@"chapae", @"차패 ④", @"쓰였다"),
            G(@"options", @"설정 ④", @"전체 조작 안내"),
            G(@"controls", @"조작 ㉮", @"멈추면 걷기"),
            G(@"controls", @"조작 ㉯", @"웅크림 중 구르기|대상 락온"),
            G(@"controls", @"조작 ㉰", @"붓으로|30m"),
            G(@"service", @"정비 ①", @"^(지금|다음) "),
            G(@"service", @"정비 ②", @"^조선통보 \d"),
            G(@"service", @"정비 ③", @"최대 체력과 먹"),
        };
        // gone308-end

        // objects308-begin : AC-TD27.3. Objects that drew a removed text: none may be left under the page, active or not.
        static readonly Entry[] GoneObjects =
        {
            O(@"pause", @"일시정지 ①", @"/PageContent/Heading$"),
            O(@"pause", @"일시정지 ②③④", @"/PageContent/Tab_(지도|옵션|조작 안내)/Meta$"),
            O(@"pause", @"일시정지 ⑤", @"/PageContent/ReturnTitle/Meta$"),
            O(@"inventory", @"소지품 ①", @"/PageContent/(SlotKind|SlotItem)$"),
            O(@"inventory", @"소지품 ④", @"/CompareAgainst$"),
            O(@"inventory", @"소지품 ⑤⑥⑦", @"/(LinkService|LinkChapae|ReadCodex)$"),
            O(@"inventory", @"소지품 ⑩", @"/EquipStatus308/Status_virtues$"),
            O(@"inventory", @"소지품 ⑪", @"/Fragment_[^/]+/Name$"),
            O(@"inventory", @"소지품 ⑫", @"/PageContent/EmptyBody$"),
            O(@"codex", @"술식 ②", @"/CodexDetail304/(InitialMeta|MedialMeta)$"),
            O(@"codex", @"술식 ⑤", @"/CodexDetail304/(DrawHint|ReleaseHint)$"),
            O(@"codex", @"술식 ㉮", @"/CodexDetail304/(RuleJamo|RuleText)$"),
            O(@"chapae", @"차패 ①", @"/PageContent/VirtueState_[1-9]$"),
            O(@"chapae", @"차패 ②③", @"/PageContent/(SummonPalanquinHint|SummonPalanquinMeta)$"),
            O(@"options", @"설정 ①②㉮", @"/(OptionHelp|HelpTitle|HelpSummary|HelpWarning|HelpWarningEdge|HelpValue_\d+|HelpMeaning_\d+)$"),
            O(@"options", @"설정 ③", @"/PageContent/(CurrentDisplay|CurrentName_\d|CurrentValue_\d)$"),
            O(@"options", @"설정 ④", @"/PageContent/ControlsLink$"),
        };
        // objects308-end

        // removed308-begin : AC-TD27.8. One example of every removed text where it stood: the allow list must refuse each.
        static readonly Entry[] Removed =
        {
            X(@"pause", @"일시정지 ①", @"/Page304/PageContent/Heading", @"잠시 붓을 내려놓는다"),
            X(@"pause", @"일시정지 ②", @"/Page304/PageContent/Tab_지도/Meta", @"청림 · 벌목마을"),
            X(@"pause", @"일시정지 ③", @"/Page304/PageContent/Tab_옵션/Meta", @"화면과 소리, 조작, 접근성"),
            X(@"pause", @"일시정지 ④", @"/Page304/PageContent/Tab_조작 안내/Meta", @"키보드와 마우스"),
            X(@"pause", @"일시정지 ⑤", @"/Page304/PageContent/ReturnTitle/Meta", @"진행을 저장하고 로비로 돌아간다"),
            X(@"inventory", @"소지품 ①", @"/Page304/PageContent/SlotKind", @"의복 · 손"),
            X(@"inventory", @"소지품 ①", @"/Page304/PageContent/SlotItem", @"송연필 +1"),
            X(@"inventory", @"소지품 ③", @"/Page304/PageContent/EquipLegend308/Legend_2/Label", @"칸에 놓아 착용"),
            X(@"inventory", @"소지품 ④", @"/Page304/PageContent/GearDetail304/EquipInfo308/CompareAgainst", @"착용 중인 송연필과 비교"),
            X(@"inventory", @"소지품 ⑤", @"/Page304/PageContent/GearDetail304/LinkService/Meta", @"쉼터 곁에서만 열린다"),
            X(@"inventory", @"소지품 ⑥", @"/Page304/PageContent/GearDetail304/LinkService/Meta", @"오행 마석 · 속성 위력"),
            X(@"inventory", @"소지품 ⑦", @"/Page304/PageContent/GearDetail304/LinkService/Label", @"정비"),
            X(@"inventory", @"소지품 ⑦", @"/Page304/PageContent/GearDetail304/LinkChapae/Label", @"차패"),
            X(@"inventory", @"소지품 ⑦", @"/Page304/PageContent/GearDetail304/ReadCodex/Label", @"술식 도감에서 보기"),
            X(@"inventory", @"소지품 ⑧", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearLevel", @"쓰였다 · 쉼터에서 쉬면 돌아온다"),
            X(@"inventory", @"소지품 ⑨", @"/Page304/PageContent/EquipStatus308/Status_tier_Health/Name", @"체력 보강"),
            X(@"inventory", @"소지품 ⑨", @"/Page304/PageContent/EquipStatus308/Status_tier_Ink/Name", @"먹 용량 보강"),
            X(@"inventory", @"소지품 ⑩", @"/Page304/PageContent/EquipStatus308/Status_virtues/Name", @"오덕"),
            X(@"inventory", @"소지품 ⑩", @"/Page304/PageContent/EquipStatus308/Status_virtues/Value", @"1 / 5"),
            X(@"inventory", @"소지품 ⑩", @"/Page304/PageContent/EquipStatus308/Status_virtues/Glyphs", @"仁 禮 義 智 信"),
            X(@"inventory", @"소지품 ⑪", @"/Page304/PageContent/FragmentGrid/Fragment_fragment.ga/Name", @"가의 석경"),
            X(@"inventory", @"소지품 ⑫", @"/Page304/PageContent/EmptyBody", @"석경 조각을 찾으면 여기 모인다"),
            X(@"inventory", @"답 1 (격자)", @"/Page304/PageContent/EquipGrid308/Stone_Wood/SlotName", @"2단"),
            X(@"inventory", @"답 1 (격자)", @"/Page304/PageContent/EquipGrid308/Equipped_Brush/SlotName", @"강화 전"),
            X(@"inventory", @"답 1 (격자)", @"/Page304/PageContent/EquipGrid308/VirtueSlot_1/SlotName", @"새기지 못함"),
            X(@"codex", @"술식 ①", @"/Page304/PageContent/Codex_가/Name", @"미발견"),
            X(@"codex", @"술식 ②", @"/Page304/PageContent/CodexDetail304/InitialMeta", @"초성 ㄱ  목"),
            X(@"codex", @"술식 ②", @"/Page304/PageContent/CodexDetail304/MedialMeta", @"중성 ㅏ  양·단일"),
            X(@"codex", @"술식 ③", @"/Page304/PageContent/CodexDetail304/SpellEffect", @"아직 석경에서 찾지 못한 술식"),
            X(@"codex", @"술식 ⑤", @"/Page304/PageContent/CodexDetail304/DrawHint/Label", @"긋기"),
            X(@"codex", @"술식 ⑤", @"/Page304/PageContent/CodexDetail304/ReleaseHint/Label", @"떼면 발동"),
            X(@"codex", @"술식 ㉮", @"/Page304/PageContent/CodexDetail304/RuleJamo", @"ㄱ"),
            X(@"codex", @"술식 ㉮", @"/Page304/PageContent/CodexDetail304/RuleText", @"가로에서 꺾어 한 번에 내린다"),
            X(@"chapae", @"차패 ①", @"/Page304/PageContent/VirtueState_1", @"새기지 못함"),
            X(@"chapae", @"차패 ①", @"/Page304/PageContent/VirtueState_0", @"새겨짐"),
            X(@"chapae", @"차패 ②", @"/Page304/PageContent/SummonPalanquinHint/Label", @"자동차 부르기"),
            X(@"chapae", @"차패 ②", @"/Page304/PageContent/SummonPalanquinHint/Key_G/Label", @"G"),
            X(@"chapae", @"차패 ③", @"/Page304/PageContent/SummonPalanquinMeta", @"하차 30m · 자동 회수"),
            X(@"chapae", @"차패 ④", @"/Page304/PageContent/VirtueState_0", @"쓰였다 · 쉼터에서 쉬면 돌아온다"),
            X(@"options", @"설정 ①", @"/Page304/PageContent/OptionHelp/HelpTitle", @"해상도"),
            X(@"options", @"설정 ②", @"/Page304/PageContent/OptionHelp/HelpWarning", @"적용한 뒤 15초 안에 확인하지 않으면 이전 화면 설정으로 돌아간다."),
            X(@"options", @"설정 ③", @"/Page304/PageContent/CurrentDisplay", @"지금 쓰는 화면"),
            X(@"options", @"설정 ④", @"/Page304/PageContent/ControlsLink/Label", @"전체 조작 안내"),
            X(@"options", @"설정 ㉮", @"/Page304/PageContent/OptionHelp/HelpSummary", @"화면을 그리는 픽셀 수. 높을수록 선명하고 무겁다."),
            X(@"options", @"설정 ㉮", @"/Page304/PageContent/OptionHelp/HelpValue_0", @"창 모드"),
            X(@"options", @"설정 ㉮", @"/Page304/PageContent/OptionHelp/HelpMeaning_0", @"다른 창과 나란히 둔다"),
            X(@"controls", @"조작 ㉮", @"/Page304/PageContent/Action_1", @"달리기 켜기·끄기 (멈추면 걷기)"),
            X(@"controls", @"조작 ㉯", @"/Page304/PageContent/Action_5", @"회피 (웅크림 중 구르기) / 대상 락온"),
            X(@"controls", @"조작 ㉰", @"/Page304/PageContent/Action_8", @"붓으로 자동차 부르기·거두기 · 하차 후 30m 자동 회수"),
            X(@"service", @"정비 ①", @"/Page304/PageContent/Buy_Wood/UpgradeLevel_Wood", @"지금 +20%"),
            X(@"service", @"정비 ①", @"/Page304/PageContent/Buy_Wood/UpgradeNext_Wood", @"다음 +30%"),
            X(@"service", @"정비 ②", @"/Page304/PageContent/Buy_Wood/UpgradePrice_Wood", @"조선통보 240"),
            X(@"service", @"정비 ③", @"/Page304/PageContent/GroupBody", @"보강 · 최대 체력과 먹"),
        };
        // removed308-end

        // kept308-begin : AC-TD27.8. Examples of what stays (the audit's [D] and [E], and what the answers keep): each must be allowed.
        static readonly Entry[] Kept =
        {
            K(@"pause", @"/Page304/PageContent/Resume/Label", @"계속하기"),
            K(@"pause", @"/Page304/PageContent/Resume/Key/Label", @"Esc"),
            K(@"pause", @"/Page304/PageContent/Tab_술식 도감/Label", @"술식"),
            K(@"pause", @"/Page304/PageContent/Tab_지도/Label", @"지도"),
            K(@"pause", @"/Page304/PageContent/Tab_지도/Key/Label", @"M"),
            K(@"pause", @"/Page304/PageContent/Tab_소지품/Meta", @"장비 8  석경 조각 12"),
            K(@"pause", @"/Page304/PageContent/Tab_소지품/Meta", @"석경 조각 0"),
            K(@"pause", @"/Page304/PageContent/Tab_술식 도감/Meta", @"석경에 남은 술식 10 / 20"),
            K(@"pause", @"/Page304/PageContent/Tab_차패/Virtues", @"仁 禮 義 智 信"),
            K(@"pause", @"/Page304/PageContent/Tab_차패/VirtueCount", @"오덕 1 / 5"),
            K(@"pause", @"/Page304/PageContent/ReturnTitle/Label", @"타이틀로"),
            K(@"pause", @"/Page304/PageContent/CommissionMeta", @"진행 중인 의뢰"),
            K(@"pause", @"/Page304/PageContent/CommissionTitle", @"옹기장수의 의뢰"),
            K(@"pause", @"/Page304/PageContent/SaveState", @"여정 기록됨"),
            K(@"pause", @"/Page304/PageContent/SaveState", @"저장하지 못했다"),
            K(@"pause", @"/Page304/PageContent/SaveRestart", @"사냥꾼 주막에서 재시작"),
            K(@"pause", @"/Page304/PageContent/SaveCoinName", @"조선통보"),
            K(@"pause", @"/Page304/PageContent/SaveCoins", @"1,240"),
            K(@"pause", @"/Page304/PageContent/RegionSlip/Region", @"청\n림"),
            K(@"rail", @"/Page304/MenuRail304/Tab_옵션/Label", @"설정"),
            K(@"rail", @"/Page304/MenuRail304/Tab_정비/Label", @"정비"),
            K(@"rail", @"/Page304/MenuRail304/Key_Q/Label", @"Q"),
            K(@"rail", @"/Page304/MenuRail304/Close/Hint/Key_Esc/Label", @"Esc"),
            K(@"rail", @"/Page304/MenuRail304/Close/Hint/Label", @"닫기"),
            K(@"inventory", @"/Page304/PageContent/EquipmentTab/Label", @"장비"),
            K(@"inventory", @"/Page304/PageContent/FragmentsTab/Count", @"12"),
            K(@"inventory", @"/Page304/PageContent/Currency/Label", @"조선통보"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/StatusBody", @"몸과 먹"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_hp/Name", @"체력"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_hp/Value", @"87 / 119"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_inkRegen/Value", @"초당 5"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_power_Wood/Name", @"木 목"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_power_Wood/Value", @"+32%"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_tier_Health/Name", @"체력"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_tier_Ink/Name", @"먹 용량"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_spells/Value", @"10 / 20"),
            K(@"inventory", @"/Page304/PageContent/EquipStatus308/Status_drop", @"남긴 통보 120"),
            K(@"inventory", @"/Page304/PageContent/EquipGrid308/Equipped_Brush/SlotName", @"붓 +1"),
            K(@"inventory", @"/Page304/PageContent/EquipGrid308/Stone_Wood/SlotName", @"목"),
            K(@"inventory", @"/Page304/PageContent/EquipGrid308/VirtueSlot_0/SlotName", @"인"),
            K(@"inventory", @"/Page304/PageContent/FragmentGrid/Fragment_fragment.ga/Glyph", @"가"),
            K(@"inventory", @"/Page304/PageContent/EmptyTitle", @"빈 봇짐"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearSlot", @"오행 마석"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearTitle", @"송연필"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearLevel", @"+1 강화 · 착용 중"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearLevel", @"강화 전"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearLevel", @"2단"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearLevel", @"새기지 못함"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearLevel", @"쓰임"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearLevel", @"지닌 수 1"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearEffect", @"모든 속성 위력 +6%"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/EffectParts", @"기본 +4% · 강화 +2%"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/CompareHead", @"견줌"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/SelectedGearCompare", @"교체 -6%p"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/UpgradeNext", @"3단 +30%"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/UpgradeWhere", @"목공 장인에게서 조선통보 80"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/UpgradeWhere", @"조선통보 240"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/UpgradeWhere", @"쉼터 · 조선통보 240"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/LearnedHead", @"익힌 목 술식 2 / 4"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/ItemTitle", @"가의 석경 조각"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipInfo308/Source", @"작업장 출구의 석경"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/CandidatesHead", @"바꿔 낄 것 2"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipmentCandidates/Gear_pine_brush/Label", @"송연필 +1"),
            K(@"inventory", @"/Page304/PageContent/GearDetail304/EquipmentCandidates/Gear_pine_brush/Meta", @"착용 중"),
            K(@"inventory", @"/Page304/PageContent/EquipLegend308/Legend_0/Label", @"바꿔 끼기"),
            K(@"inventory", @"/Page304/PageContent/EquipLegend308/Legend_2/Label", @"착용"),
            K(@"inventory", @"/Page304/PageContent/EquipLegend308/Legend_0/Label", @"술식 도감에서 보기"),
            K(@"inventory", @"/Page304/PageContent/EquipLegend308/Legend_2/Key_끌기/Label", @"끌기"),
            K(@"codex", @"/Page304/PageContent/KnownLabel", @"석경에 남은 술식"),
            K(@"codex", @"/Page304/PageContent/TotalCount", @"/ 20"),
            K(@"codex", @"/Page304/PageContent/ColumnMeta", @"ㄱ  0 / 4"),
            K(@"codex", @"/Page304/PageContent/RowJamo", @"ㅗㅁ"),
            K(@"codex", @"/Page304/PageContent/RowFrame", @"양·단일"),
            K(@"codex", @"/Page304/PageContent/Codex_가/Glyph", @"가"),
            K(@"codex", @"/Page304/PageContent/Codex_가/Name", @"생목 가시"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/Composition", @"ㄱ + ㅗ + ㅁ"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/FinalHead", @"받침"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/FinalText", @"완전 속박"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/FinalText", @"미습득"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/FinalText", @"미배정"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/ExampleLabel", @"쓰기 예시"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/ReplayHint/Label", @"획 다시 보기"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/SpellEffect", @"단일 대상에 곧게 뻗는 생목 가시"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/SpellEffect", @"미발견"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/SpellRange", @"근중거리"),
            K(@"codex", @"/Page304/PageContent/CodexDetail304/Sheng", @"水 → 木 → 火"),
            K(@"chapae", @"/Page304/PageContent/Identity/Title", @"差\n牌"),
            K(@"chapae", @"/Page304/PageContent/Identity/IdentityLabel", @"오행부 소속\n전직 집행관"),
            K(@"chapae", @"/Page304/PageContent/VirtueName_1", @"예 · 화"),
            K(@"chapae", @"/Page304/PageContent/VirtueState_0", @"쓰임"),
            K(@"options", @"/Page304/PageContent/OptionTab_접근성/Label", @"접근성"),
            K(@"options", @"/Page304/PageContent/Value_해상도/Label", @"해상도"),
            K(@"options", @"/Page304/PageContent/Value_해상도/Value", @"2560 × 1440"),
            K(@"options", @"/Page304/PageContent/Value_해상도/Meta", @"적용 전"),
            K(@"options", @"/Page304/PageContent/Value_프레임 제한/Reason", @"수직동기화 사용 중"),
            K(@"options", @"/Page304/PageContent/Value_품질/Value", @"Mobile"),
            K(@"options", @"/Page304/PageContent/Value_마우스 감도/Value", @"1.00×"),
            K(@"options", @"/Page304/PageContent/Value_임팩트 프레임/Value", @"줄임"),
            K(@"options", @"/Page304/PageContent/ApplyDisplay/Label", @"화면 설정 적용"),
            K(@"options", @"/Page304/PageContent/ResetSettings/Label", @"기본값 복원"),
            K(@"options", @"/Page304/PageContent/Listen/Label", @"효과음 들어보기"),
            K(@"options", @"/Page304/PageContent/CurrentDisplayBlock/CurrentDisplay", @"지금 쓰는 화면"),
            K(@"options", @"/Page304/PageContent/CurrentDisplayBlock/CurrentValue_0", @"1920 × 1080"),
            K(@"controls", @"/Page304/PageContent/ControlsTab_작도/Label", @"작도"),
            K(@"controls", @"/Page304/PageContent/Key_5/Key_왼쪽 Shift/Label", @"왼쪽 Shift"),
            K(@"controls", @"/Page304/PageContent/Key_1/Word", @"이동"),
            K(@"controls", @"/Page304/PageContent/Action_1", @"달리기 켜기·끄기"),
            K(@"controls", @"/Page304/PageContent/Action_5", @"회피 / 락온"),
            K(@"controls", @"/Page304/PageContent/Action_8", @"자동차 부르기·거두기"),
            K(@"controls", @"/Page304/PageContent/Action_0", @"글씨 그리기"),
            K(@"service", @"/Page304/PageContent/GroupStones", @"마석 · 속성 위력"),
            K(@"service", @"/Page304/PageContent/GroupBody", @"보강"),
            K(@"service", @"/Page304/PageContent/Buy_Health/Label", @"체력 보강"),
            K(@"service", @"/Page304/PageContent/Buy_Wood/Meta", @"통보가 모자라다"),
            K(@"service", @"/Page304/PageContent/Buy_Wood/UpgradeLevel_Wood", @"+20%"),
            K(@"service", @"/Page304/PageContent/Buy_Wood/UpgradeNext_Wood", @"→ +30%"),
            K(@"service", @"/Page304/PageContent/Buy_Wood/UpgradePrice_Wood", @"240"),
        };
        // kept308-end

        // needed308-begin : AC-TD27.5. What must still stand: the texts the audit classed necessary [D] or reading matter [E], and
        // what answer 2 keeps (the pause slip, the centre head, the bottom key line). Each must be seen at least once while the
        // page is walked. "<page>-<condition>" = demanded only when the session can show it (Conditions below; an undemanded
        // group is printed as an INFO line, never as a pass):
        //   pause-located       the walker stands in a realm or a place (the slip is built only then)
        //   inventory-gear      the session has an equipment catalog (without one the 소지품 page is the 석경 tab alone)
        //   inventory-stone     the grid has 오행 마석 칸 (they are built only with the shelter economy)
        //   inventory-fragment  at least one 석경 조각 is held (else the tab says 빈 봇짐)
        //   codex-found         at least one letter of the matrix is found (else every detail is the 묵등 block + 미발견)
        static readonly Entry[] Needed =
        {
            N(@"pause", @"/PageContent/Resume/Label$", @"계속하기"),
            N(@"pause", @"/PageContent/Tab_소지품/Label$", @"소지품"),
            N(@"pause", @"/PageContent/Tab_술식 도감/Label$", @"술식"),
            N(@"pause", @"/PageContent/Tab_차패/Label$", @"차패"),
            N(@"pause", @"/PageContent/Tab_지도/Label$", @"지도"),
            N(@"pause", @"/PageContent/Tab_옵션/Label$", @"설정"),
            N(@"pause", @"/PageContent/Tab_조작 안내/Label$", @"조작"),
            N(@"pause", @"/PageContent/Tab_소지품/Meta$", @".+"),
            N(@"pause", @"/PageContent/Tab_술식 도감/Meta$", @".+"),
            N(@"pause", @"/PageContent/Tab_차패/VirtueCount$", @".+"),
            N(@"pause", @"/PageContent/ReturnTitle/Label$", @"타이틀로"),
            N(@"pause", @"/PageContent/SaveState$", @".+"),
            N(@"pause", @"/PageContent/SaveRestart$", @".+"),
            N(@"pause", @"/PageContent/SaveCoinName$", @"조선통보"),
            N(@"pause", @"/PageContent/SaveCoins$", @".+"),
            N(@"pause-located", @"/PageContent/RegionSlip/Region$", @"[\s\S]+"),
            N(@"inventory", @"/PageContent/FragmentsTab/Label$", @"석경"),
            N(@"inventory", @"/PageContent/Currency/Label$", @"조선통보"),
            N(@"inventory", @"/PageContent/Currency/Value$", @".+"),
            N(@"inventory", @"/EquipStatus308/StatusLearned$", @"익힌 것"),
            N(@"inventory", @"/EquipStatus308/Status_spells/Name$", @"술식"),
            N(@"inventory-gear", @"/VirtueSlot_0/SlotName$", @"인"),
            N(@"inventory-gear", @"/EquipInfo308/SelectedGearSlot$", @"오덕"),
            N(@"inventory-gear", @"/EquipInfo308/SelectedGearSlot$", @"붓|머리|몸|손|발|장신구"),
            N(@"inventory-gear", @"/EquipInfo308/SelectedGearTitle$", @"[\s\S]+"),
            N(@"inventory-gear", @"/EquipInfo308/SelectedGearLevel$", @"새겨짐|새기지 못함|쓰임"),
            N(@"inventory-gear", @"/EquipLegend308/Legend_\d/Label$", @"차패"),
            N(@"inventory-gear", @"/EquipLegend308/Legend_\d/Key_[^/]+/Label$", @"Enter"),
            N(@"inventory-stone", @"/EquipInfo308/SelectedGearSlot$", @"오행 마석"),
            N(@"inventory-stone", @"/EquipInfo308/SelectedGearTitle$", @"[목화토금수] 마석"),
            N(@"inventory-stone", @"/EquipInfo308/SelectedGearLevel$", @"\d+단"),
            N(@"inventory-stone", @"/EquipInfo308/EffectHead$", @"효과"),
            N(@"inventory-stone", @"/EquipInfo308/SelectedGearEffect$", @"[목화토금수] 속성 위력 \+\d+%"),
            N(@"inventory-stone", @"/EquipInfo308/UpgradeHead$", @"다음 강화"),
            N(@"inventory-fragment", @"/Fragment_[^/]+/Glyph$", @"[가-힣]"),
            N(@"inventory-fragment", @"/EquipInfo308/SelectedGearSlot$", @"석경 조각"),
            N(@"inventory-fragment", @"/EquipInfo308/ItemTitle$", @"[가-힣]의 석경 조각"),
            N(@"inventory-fragment", @"/EquipInfo308/SelectedGearLevel$", @"지닌 수 \d+"),
            N(@"inventory-fragment", @"/EquipLegend308/Legend_\d/Label$", @"술식 도감에서 보기"),
            N(@"inventory-fragment", @"/EquipLegend308/Legend_\d/Key_[^/]+/Label$", @"Enter"),
            N(@"codex", @"/PageContent/KnownLabel$", @"석경에 남은 술식"),
            N(@"codex", @"/PageContent/KnownCount$", @".+"),
            N(@"codex", @"/PageContent/TotalCount$", @".+"),
            N(@"codex", @"/PageContent/ColumnName$", @".+"),
            N(@"codex", @"/PageContent/ColumnMeta$", @".+"),
            N(@"codex", @"/PageContent/RowJamo$", @".+"),
            N(@"codex", @"/CodexDetail304/TagElement$", @".+"),
            N(@"codex", @"/CodexDetail304/Composition$", @".+"),
            N(@"codex", @"/CodexDetail304/SpellEffect$", @"[\s\S]+"),
            N(@"codex", @"/CodexDetail304/ShengLabel$", @"상생"),
            N(@"codex", @"/CodexDetail304/KeLabel$", @"상극"),
            N(@"codex", @"/CodexDetail304/Sheng$", @".+"),
            N(@"codex", @"/CodexDetail304/Ke$", @".+"),
            N(@"codex-found", @"/PageContent/Codex_[^/]+/Glyph$", @"[가-힣]"),
            N(@"codex-found", @"/PageContent/Codex_[^/]+/Name$", @"[\s\S]+"),
            N(@"codex-found", @"/CodexDetail304/SpellGlyph$", @"[가-힣]"),
            N(@"codex-found", @"/CodexDetail304/ExampleLabel$", @"쓰기 예시"),
            N(@"codex-found", @"/CodexDetail304/ReplayHint/Label$", @"획 다시 보기"),
            N(@"codex-found", @"/CodexDetail304/ReplayHint/Key_Enter/Label$", @"Enter"),
            N(@"codex-found", @"/CodexDetail304/SpellEffect$", @"(?!미발견$)[\s\S]+"),
            N(@"chapae", @"/PageContent/Identity/Title$", @"[\s\S]+"),
            N(@"chapae", @"/PageContent/Identity/IdentityLabel$", @"[\s\S]+"),
            N(@"chapae", @"/PageContent/VirtueName_0$", @"인 · 목"),
            N(@"chapae", @"/PageContent/VirtueName_1$", @"예 · 화"),
            N(@"chapae", @"/PageContent/VirtueName_2$", @"의 · 금"),
            N(@"chapae", @"/PageContent/VirtueName_3$", @"지 · 수"),
            N(@"chapae", @"/PageContent/VirtueName_4$", @"신 · 토"),
            N(@"options", @"/PageContent/OptionTab_화면/Label$", @"화면"),
            N(@"options", @"/PageContent/OptionTab_소리/Label$", @"소리"),
            N(@"options", @"/PageContent/OptionTab_조작/Label$", @"조작"),
            N(@"options", @"/PageContent/OptionTab_접근성/Label$", @"접근성"),
            N(@"options", @"/PageContent/Value_해상도/Label$", @"해상도"),
            N(@"options", @"/PageContent/Value_화면 모드/Label$", @"화면 모드"),
            N(@"options", @"/PageContent/Value_품질/Label$", @"품질"),
            N(@"options", @"/PageContent/Value_수직동기화/Label$", @"수직동기화"),
            N(@"options", @"/PageContent/Value_프레임 제한/Label$", @"프레임 제한"),
            N(@"options", @"/PageContent/Value_전체 음량/Label$", @"전체 음량"),
            N(@"options", @"/PageContent/Value_게임 효과음/Label$", @"게임 효과음"),
            N(@"options", @"/PageContent/Value_UI 효과음/Label$", @"UI 효과음"),
            N(@"options", @"/PageContent/Value_마우스 감도/Label$", @"마우스 감도"),
            N(@"options", @"/PageContent/Value_시점 Y축 반전/Label$", @"시점 Y축 반전"),
            N(@"options", @"/PageContent/Value_익힘 멈춤/Label$", @"익힘 멈춤"),
            N(@"options", @"/PageContent/Value_UI 크기/Label$", @"UI 크기"),
            N(@"options", @"/PageContent/Value_본문 크기/Label$", @"본문 크기"),
            N(@"options", @"/PageContent/Value_미니맵/Label$", @"미니맵"),
            N(@"options", @"/PageContent/Value_방위선/Label$", @"방위선"),
            N(@"options", @"/PageContent/Value_지도 펼침 동작 줄이기/Label$", @"지도 펼침 동작 줄이기"),
            N(@"options", @"/PageContent/Value_임팩트 프레임/Label$", @"임팩트 프레임"),
            N(@"options", @"/PageContent/Value_HUD 반응/Label$", @"HUD 반응"),
            N(@"options", @"/PageContent/Value_[^/]+/Value$", @".+"),
            N(@"options", @"/PageContent/ApplyDisplay/Label$", @"화면 설정 적용"),
            N(@"options", @"/PageContent/ResetSettings/Label$", @"기본값 복원"),
            N(@"options", @"/PageContent/Listen/Label$", @"효과음 들어보기"),
            N(@"options", @"/PageContent/CurrentDisplayBlock/CurrentDisplay$", @"지금 쓰는 화면"),
            N(@"controls", @"/PageContent/ControlsTab_이동/Label$", @"이동"),
            N(@"controls", @"/PageContent/ControlsTab_작도/Label$", @"작도"),
            N(@"controls", @"/PageContent/ControlsTab_메뉴/Label$", @"메뉴"),
            N(@"controls", @"/PageContent/Action_\d+$", @"걷기 / 탑승 중 가속·조향"),
            N(@"controls", @"/PageContent/Action_\d+$", @"달리기 켜기·끄기|달리기"),
            N(@"controls", @"/PageContent/Action_\d+$", @"지상 점프 / 탑승 중 제동"),
            N(@"controls", @"/PageContent/Action_\d+$", @"웅크리기 전환 / 바닥 착석·일어나기"),
            N(@"controls", @"/PageContent/Action_\d+$", @"시점 이동"),
            N(@"controls", @"/PageContent/Action_\d+$", @"회피 / 락온"),
            N(@"controls", @"/PageContent/Action_\d+$", @"조사, 대화, 석경 파편 획득, 자동차 탑승·하차"),
            N(@"controls", @"/PageContent/Action_\d+$", @"탑승 중 시점 전환"),
            N(@"controls", @"/PageContent/Action_\d+$", @"자동차 부르기·거두기"),
            N(@"controls", @"/PageContent/Action_\d+$", @"글씨 그리기"),
            N(@"controls", @"/PageContent/Action_\d+$", @"먹 갈무리"),
            N(@"controls", @"/PageContent/Action_\d+$", @"지도 / 소지품 / 일시정지·뒤로"),
            N(@"controls", @"/PageContent/Action_\d+$", @"메뉴 탭 넘기기"),
            N(@"service", @"/PageContent/ShopPlace$", @".+"),
            N(@"service", @"/PageContent/GroupStones$", @"마석 · 속성 위력"),
            N(@"service", @"/PageContent/GroupBody$", @"보강"),
            N(@"service", @"/PageContent/Buy_[^/]+/Label$", @".+"),
            N(@"service", @"/PageContent/Buy_[^/]+/UpgradeLevel_[^/]+$", @".+"),
        };
        // needed308-end

        /// <summary>AC-TD27.4. The state words of answer 1. Never in a 칸 of a grid; at most once in the detail of the selected 칸.</summary>
        static readonly Regex StateWord = new Regex(@"\A(?:미발견|미습득|미배정|새기지 못함|강화 전( · 착용 중)?|\d+단)\z", RegexOptions.CultureInvariant);
        static readonly Regex GridCell = new Regex(@"/(Equipped_[^/]+|Stone_[^/]+|VirtueSlot_\d|Fragment_[^/]+|Codex_[^/]+|Virtue_\d|VirtueName_\d|VirtueState_\d)(/|$)", RegexOptions.CultureInvariant);
        static readonly Regex Detail = new Regex(@"/(GearDetail304|CodexDetail304)/", RegexOptions.CultureInvariant);
        static readonly Regex Tags = new Regex(@"<[^>]+>", RegexOptions.CultureInvariant);

        static readonly string[] PageIds = { "pause", "inventory", "codex", "chapae", "options", "controls", "service" };
        static readonly string[] MenuStateMembers = { "optionsTab", "menu304ControlsTab", "fragmentTab", "content304Focus", "selectedItem", "selectedSpell", "equip308Slot" };

        static string PageName(string id)
        {
            switch (id)
            {
                case "pause": return "일시정지"; case "inventory": return "소지품"; case "codex": return "술식 도감"; case "chapae": return "차패";
                case "options": return "옵션"; case "controls": return "조작 안내"; case "service": return "정비";
                default: throw new ArgumentException("Unknown page id: " + id + " (pause, inventory, codex, chapae, options, controls, service)");
            }
        }

        // ------------------------------------------------------------------ report
        sealed class Sheet
        {
            public readonly List<string> Lines = new List<string>(); public int Pass, Fail;
            public void Check(bool ok, string text) { Lines.Add((ok ? "PASS " : "FAIL ") + text); if (ok) Pass++; else Fail++; }
            public void Info(string text) { Lines.Add("INFO " + text); }
        }

        /// <summary>What one page gathered while its states were walked: one line per AC is written from it.</summary>
        sealed class PageFindings
        {
            public int States, Texts;
            public readonly List<string> NotAllowed = new List<string>(), GoneText = new List<string>(), GoneObject = new List<string>(),
                StateWords = new List<string>(), Overflow = new List<string>(), IconOnly = new List<string>(), Layout = new List<string>(), Empty = new List<string>();
            public readonly HashSet<string> Seen = new HashSet<string>();   // path + "\n" + text of every live text (AC-TD27.5)
            public int LayoutChecks;
            /// <summary>What this session can show (AC-TD27.5): "located", "gear", "stone", "fragment", "found". A needed entry of the
            /// page "inventory-gear" is demanded only while "gear" is in here.</summary>
            public readonly HashSet<string> Conditions = new HashSet<string>();
        }

        // conditions308-begin : one condition per line (the offline check reads the lines between the two markers)
        /// <summary>The conditional groups of the needed table, with what the report says when a group is not demanded.</summary>
        static readonly string[][] ConditionNotes =
        {
            new[] { "pause-located", "the walker stands in no realm and no place: the slip is not built here" },
            new[] { "inventory-gear", "no equipment catalog in this session: only the 석경 tab exists" },
            new[] { "inventory-stone", "no 오행 마석 칸 in this session (no shelter economy)" },
            new[] { "inventory-fragment", "no 석경 조각 is held: the tab says 빈 봇짐" },
            new[] { "codex-found", "no found letter in this save: no glyph, film or effect sentence can be read" },
        };
        // conditions308-end

        static string Output(string command)
        {
            string name = Regex.Replace(command, @"[^A-Za-z0-9]+", "_").Trim('_');
            return Path.GetFullPath(Path.Combine(Application.dataPath, "../../Art/Playtest308/MenuText/menutext308_" + name + ".txt"));
        }

        public static string Execute(string argument)
        {
            string a = (argument ?? "").Trim();
            var sheet = new Sheet(); string refused = null;
            try
            {
                if (a == "static") Static(sheet);
                else if (a == "all" || a.StartsWith("page:", StringComparison.Ordinal)) refused = Pages(sheet, a == "all" ? "all" : a.Substring(5).Trim(), false);
                else if (a.StartsWith("large:", StringComparison.Ordinal)) refused = Pages(sheet, a.Substring(6).Trim(), true);
                else throw new ArgumentException("Expected static, page:<id>, all, large:<id> or large:all (id = " + string.Join(", ", PageIds) + ")");
            }
            catch (ArgumentException) { throw; }
            catch (Exception e) { sheet.Check(false, "exception: " + e.GetType().Name + " " + e.Message); }
            if (refused != null) return refused;
            sheet.Lines.Add("RESULT menutext308 " + a + " PASS " + sheet.Pass + " / FAIL " + sheet.Fail);
            string text = string.Join("\n", sheet.Lines);
            Directory.CreateDirectory(Path.GetDirectoryName(Output(a)));
            File.WriteAllText(Output(a), text + "\n");
            return text;
        }

        // ------------------------------------------------------------------ the allow list
        static string Norm(string text) => Tags.Replace(text ?? "", "").Replace("\r", "");

        /// <summary>"ok", "text" (the object is listed, this text is not allowed there) or "unlisted" (no rule names the object).</summary>
        static string Verdict(string page, string path, string text)
        {
            bool listed = false;
            foreach (var r in Rules)
            {
                if ((r.Page != page && r.Page != "rail") || !r.PathRx.IsMatch(path)) continue;
                listed = true;
                if (r.TextRx.IsMatch(text)) return "ok";
            }
            return listed ? "text" : "unlisted";
        }

        // ------------------------------------------------------------------ static (AC-TD27.8)
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        static void Static(Sheet sheet)
        {
            var wrong = Removed.Where(x => Verdict(x.Page, x.Path, x.Text) == "ok").Select(x => x.Audit + " \"" + x.Text + "\"").ToList();
            sheet.Check(wrong.Count == 0, "AC-TD27.8 the allow list refuses every removed example (" + Removed.Length + "): " + (wrong.Count == 0 ? "all refused" : "ALLOWED " + string.Join(" | ", wrong)));
            var lost = Kept.Where(k => Verdict(k.Page, k.Path, k.Text) != "ok").Select(k => k.Path + " \"" + k.Text.Replace("\n", "\\n") + "\"").ToList();
            sheet.Check(lost.Count == 0, "AC-TD27.8 the allow list allows every kept example (" + Kept.Length + "): " + (lost.Count == 0 ? "all allowed" : "REFUSED " + string.Join(" | ", lost)));
            var open = GoneTexts.Where(g => Kept.Any(k => k.Page == g.Page && g.TextRx.IsMatch(k.Text))).Select(g => g.Audit + " /" + g.Text + "/").ToList();
            sheet.Check(open.Count == 0, "AC-TD27.8 no removed-text pattern hits a kept example: " + (open.Count == 0 ? "none" : string.Join(" | ", open)));
            // the needed table must name what answer 2 keeps and the reading matter (a table that forgets them passes everything)
            var anchors = new[]
            {
                new[] { "pause-located", "/RegionSlip/Region" }, new[] { "inventory-gear", "/EquipInfo308/SelectedGearTitle" }, new[] { "inventory-gear", @"/EquipLegend308/Legend_\d/Label" },
                new[] { "inventory-fragment", "/EquipInfo308/ItemTitle" }, new[] { "inventory-fragment", @"/EquipLegend308/Legend_\d/Label" },
                new[] { "codex-found", "/CodexDetail304/SpellEffect" }, new[] { "codex-found", "/CodexDetail304/ExampleLabel" }, new[] { "codex-found", "/CodexDetail304/ReplayHint/Label" },
            };
            var unnamed = anchors.Where(a => !Needed.Any(n => n.Page == a[0] && n.Path.IndexOf(a[1], StringComparison.Ordinal) >= 0)).Select(a => a[0] + " " + a[1]).ToList();
            sheet.Check(unnamed.Count == 0, "AC-TD27.8 the needed table names what answer 2 keeps and the reading matter (" + anchors.Length + "): " + (unnamed.Count == 0 ? "all named" : "MISSING " + string.Join(" | ", unnamed)));
            var groups = Needed.Select(n => n.Page).Where(p => p.IndexOf('-') >= 0).Distinct().Where(p => !ConditionNotes.Any(c => c[0] == p)).ToList();
            sheet.Check(groups.Count == 0, "AC-TD27.8 every conditional group of the needed table is a known condition: " + (groups.Count == 0 ? "all known" : "UNKNOWN " + string.Join(", ", groups)));

            // the members that drew the removed texts (reflection by name: a rename shows up here as a missing row, not as a pass)
            var root = typeof(PlaytestUiRoot);
            foreach (string name in new[] { "Equip308Head", "Equip308SetText", "Equip308Link", "Equip308GearKind", "equip308KindLabel", "equip308ItemLabel", "Equip308StoneLink", "Equip308FragmentNameDy" })
                sheet.Check(root.GetMember(name, Any).Length == 0, "AC-TD27.8 PlaytestUiRoot." + name + " is gone");
            var panel = typeof(MenuOptionsPanel304);
            foreach (string name in new[] { "DrawHelp", "help", "shownKey", "shownRow" })
                sheet.Check(panel.GetMember(name, Any).Length == 0, "AC-TD27.8 MenuOptionsPanel304." + name + " is gone (no 설명 칸)");
            sheet.Check(panel.GetField("ShownWhilePending") != null, "AC-TD27.8 MenuOptionsPanel304.ShownWhilePending exists (지금 쓰는 화면 only while 적용 전)");
            var text = typeof(EquipmentText308);
            sheet.Check(text.GetMember("CompareAgainst", Any).Length == 0, "AC-TD27.8 EquipmentText308.CompareAgainst is gone");
            sheet.Check(EquipmentText308.StoneWhere(false, 240) == "쉼터 · " + EquipmentText308.Coins(240) && EquipmentText308.StoneWhere(true, 240) == EquipmentText308.Coins(240),
                "AC-TD27.8 EquipmentText308.StoneWhere: \"" + EquipmentText308.StoneWhere(false, 240) + "\" away from a shelter, \"" + EquipmentText308.StoneWhere(true, 240) + "\" beside one");
            sheet.Info("tables: allow " + Rules.Length + ", removed texts " + GoneTexts.Length + ", removed objects " + GoneObjects.Length + ", removed examples " + Removed.Length
                + ", kept examples " + Kept.Length + ", needed " + Needed.Length);
            sheet.Info("data left untouched (read by nothing on a menu page now): UiStyle304SO.OptionHelp, UiStyle304SO.StrokeRules (the loading screen still cycles the stroke rules: WorldLoadingProfile270.StrokeRulesWhileWaiting)");
        }

        // ------------------------------------------------------------------ pages (AC-TD27.1 .. .7)
        static string Pages(Sheet sheet, string which, bool large)
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused) return "REFUSED pages exist only at runtime: an unpaused Play session is needed (static runs in Edit Mode)";
            var ui = PlaytestUiRoot.Instance;
            if (ui == null || ui.IsTitle || ui.Session == null || ui.Session.Progress == null) return "REFUSED no gameplay session (enter the play scene first)";
            if (EventSystem.current == null) return "REFUSED no EventSystem";
            if (ui.Busy || HarnessUiRules304.Member(ui, "confirmationRoot") as UnityEngine.Object != null) return "REFUSED the menu is busy or a confirm dialog is open";
            if (ui.Page == "상세" || ui.Page == "장비 상점" || ui.Page == "장비 강화") return "REFUSED close the " + ui.Page + " window first";
            var ids = which == "all" ? PageIds.ToList() : new List<string> { which };
            foreach (string id in ids) PageName(id);   // throws on an unknown id before anything is opened
            if (large && ui.Settings.IsPreviewing) return "REFUSED a settings preview is running: large needs none so its rollback is unambiguous";

            string originalPage = ui.Page;
            var saved = new Dictionary<string, object>();
            foreach (string name in MenuStateMembers) if (HarnessUiRules304.HasMember(ui, name)) saved[name] = HarnessUiRules304.Member(ui, name);
            if (large)
            {
                var preview = ui.Settings.Current.Clone(); preview.UiScale = 1.2f; preview.TextScale = 1.25f;
                ui.Settings.Preview(preview);
                HarnessUiRules304.InvalidateTextScaleCache();
            }
            try
            {
                sheet.Info("scale: UI " + ui.Settings.Current.UiScale.ToString("0.00") + ", text " + ui.Settings.Current.TextScale.ToString("0.00") + (large ? " (unsaved preview, reverted afterwards)" : " (current settings)"));
                foreach (string id in ids)
                {
                    if (id == "service" && !ui.Session.AtDemoShop) { sheet.Info("service (정비): not beside a shelter - the page cannot open here; run page:service at a shelter"); continue; }
                    var f = new PageFindings();
                    try { Walk(ui, id, f, sheet); }
                    catch (Exception e) { sheet.Check(false, id + ": exception while walking the page: " + e.GetType().Name + " " + e.Message); }
                    Report(sheet, id, f, large);
                }
            }
            finally
            {
                foreach (var pair in saved) HarnessUiRules304.SetMember(ui, pair.Key, pair.Value);
                if (large && ui.Settings.IsPreviewing) ui.Settings.Revert();
                if (large) HarnessUiRules304.InvalidateTextScaleCache();
                if (string.IsNullOrEmpty(originalPage)) { if (ui.Page.Length > 0) ui.CloseMenu(); }
                else { Quiet(ui); ui.OpenPage(originalPage); }
            }
            return null;
        }

        static void Report(Sheet sheet, string id, PageFindings f, bool large)
        {
            string head = id + (large ? " [UI 1.2 / text 1.25]" : "");
            sheet.Check(f.States > 0, "AC-TD27.0 " + head + ": the page opened and " + f.States + " state(s) were read (" + f.Texts + " text(s))");
            if (f.States == 0) return;
            sheet.Check(f.NotAllowed.Count == 0, "AC-TD27.1 " + head + ": every text is in the allow list" + Tail(f.NotAllowed));
            sheet.Check(f.GoneText.Count == 0, "AC-TD27.2 " + head + ": removed texts found 0" + Tail(f.GoneText));
            sheet.Check(f.GoneObject.Count == 0, "AC-TD27.3 " + head + ": objects of removed texts left 0" + Tail(f.GoneObject));
            sheet.Check(f.StateWords.Count == 0, "AC-TD27.4 " + head + ": state words 0 in a grid, at most 1 in the detail of the selected 칸" + Tail(f.StateWords));
            sheet.Check(f.IconOnly.Count == 0, "AC-TD27.4 " + head + ": no Selectable without a label (layout-check rule, with the unfound 술식 칸 exemption)" + Tail(f.IconOnly));
            var needed = Needed.Where(n => Demanded(n, id, f)).ToList();
            var missing = needed.Where(n => !f.Seen.Any(s => Matches(n, s))).Select(n => n.Path + " /" + n.Text + "/").ToList();
            sheet.Check(missing.Count == 0, "AC-TD27.5 " + head + ": every necessary text was seen (" + needed.Count + ")" + Tail(missing));
            foreach (var note in ConditionNotes)
            {
                if (!note[0].StartsWith(id + "-", StringComparison.Ordinal) || f.Conditions.Contains(note[0].Substring(id.Length + 1))) continue;
                sheet.Info("AC-TD27.5 " + head + ": NOT DEMANDED here - " + Needed.Count(n => n.Page == note[0]) + " entr(ies) of " + note[0] + " (" + note[1] + ")");
            }
            sheet.Check(f.Overflow.Count == 0, "AC-TD27.6 " + head + ": text overflow 0" + Tail(f.Overflow));
            if (f.LayoutChecks > 0) sheet.Check(f.Layout.Count == 0, "AC-TD27.7 " + head + ": the layout closed where a text went (" + f.LayoutChecks + " measure(s))" + Tail(f.Layout));
            else sheet.Info("AC-TD27.7 " + head + ": NOT MEASURED here (" + (id == "codex" ? "no found letter in this save: the film and the 받침 table are not built" : "no measure is defined for this page: read the capture") + ")");
            if (f.Empty.Count > 0) sheet.Info(head + ": childless holders without a graphic (for a person to read; anchors and empty lists are expected): " + string.Join(", ", f.Empty.Distinct().Take(12)));
        }

        static string Tail(List<string> items) => items.Count == 0 ? "" : " - " + items.Count + ": " + string.Join(" | ", items.Distinct().Take(8)) + (items.Distinct().Count() > 8 ? " | ..." : "");

        static bool Matches(Entry needed, string seen)
        {
            int cut = seen.IndexOf('\n'); string path = seen.Substring(0, cut), text = seen.Substring(cut + 1);
            return needed.PathRx.IsMatch(path) && needed.TextRx.IsMatch(text);
        }

        /// <summary>A needed entry of the page itself, or of "page-condition" while the walk found that condition.</summary>
        static bool Demanded(Entry needed, string id, PageFindings f)
            => needed.Page == id || (needed.Page.StartsWith(id + "-", StringComparison.Ordinal) && f.Conditions.Contains(needed.Page.Substring(id.Length + 1)));

        /// <summary>True when the pause slip has something to say: PlaytestUiRoot.Menu304Location gives a realm or a place (the
        /// slip is built only then). Read through the page's own method, not through the slip, so a slip that is no longer
        /// built shows as a missing needed text. A renamed method = true (the slip is then demanded and a person reads the line).</summary>
        static bool Located(PlaytestUiRoot ui)
        {
            var m = typeof(PlaytestUiRoot).GetMethod("Menu304Location", Any);
            if (m == null) return true;
            var args = new object[] { null, null };
            m.Invoke(ui, args);
            return !string.IsNullOrEmpty(args[0] as string) || !string.IsNullOrEmpty(args[1] as string);
        }

        // ------------------------------------------------------------------ walking a page
        /// <summary>The page that is open now (V.Page304: veil, rail, PageContent). Pages closed inside this same editor call are
        /// still under the menu layer, inactive, until the frame ends - they are not read.</summary>
        static Transform Scope(PlaytestUiRoot ui)
            => ui.Menu304PageRoot != null ? ui.Menu304PageRoot : HarnessUiRules304.Member(ui, "modalLayer") as Transform ?? ui.transform;
        static void Settle(PlaytestUiRoot ui) { HarnessUiRules304.SettleLayout(ui); }
        /// <summary>The walk opens pages and selects dozens of 칸 inside one frame: the page cue and the focus cue are kept silent.</summary>
        static void Quiet(PlaytestUiRoot ui) { HarnessUiRules304.SetMember(ui, "suppressNextPageSound255", true); }
        static void Select(GameObject go)
        {
            foreach (var cue in go.GetComponents<CompactUiSound255>()) cue.enabled = false;
            EventSystem.current.SetSelectedGameObject(go); Canvas.ForceUpdateCanvases();
        }
        static void Press(PlaytestUiRoot ui, Button b) { Quiet(ui); b.onClick.Invoke(); }
        static Button ButtonNamed(PlaytestUiRoot ui, string name)
            => Scope(ui).GetComponentsInChildren<Button>(false).FirstOrDefault(b => b != null && b.name == name && b.gameObject.activeInHierarchy && b.interactable);
        static List<ContentCell304> Cells(PlaytestUiRoot ui, string prefix)
            => Scope(ui).GetComponentsInChildren<ContentCell304>(false).Where(c => c != null && c.name.StartsWith(prefix, StringComparison.Ordinal)).ToList();

        static void Walk(PlaytestUiRoot ui, string id, PageFindings f, Sheet sheet)
        {
            string page = PageName(id);
            Quiet(ui); ui.OpenPage(page);
            if (ui.Page != page) throw new InvalidOperationException("OpenPage did not open " + page + " (actual '" + ui.Page + "')");
            switch (id)
            {
                case "inventory": WalkInventory(ui, f, sheet); break;
                case "codex": WalkCodex(ui, f); break;
                case "options": WalkOptions(ui, f, sheet); break;
                case "controls":
                    foreach (string tab in new[] { "이동", "작도", "메뉴" })
                    {
                        var b = ButtonNamed(ui, "ControlsTab_" + tab); if (b == null) { f.NotAllowed.Add("tab ControlsTab_" + tab + " is missing"); continue; }
                        Press(ui, b); Read(ui, id, tab, f);
                    }
                    break;
                case "chapae": Read(ui, id, "", f); LayoutChapae(ui, f); break;
                case "service": Read(ui, id, "", f); LayoutService(ui, f); break;
                case "pause": if (Located(ui)) f.Conditions.Add("located"); Read(ui, id, "", f); break;
                default: Read(ui, id, "", f); break;
            }
        }

        static void WalkInventory(PlaytestUiRoot ui, PageFindings f, Sheet sheet)
        {
            bool hasGear = ButtonNamed(ui, "EquipmentTab") != null; if (hasGear) f.Conditions.Add("gear");
            if (hasGear && HarnessUiRules304.Member(ui, "fragmentTab") is bool onFragments && onFragments) Press(ui, ButtonNamed(ui, "EquipmentTab"));
            if (hasGear)
            {
                var names = Cells(ui, "Equipped_").Concat(Cells(ui, "Stone_")).Concat(Cells(ui, "VirtueSlot_")).Select(c => c.name).ToList();
                if (names.Any(n => n.StartsWith("Stone_", StringComparison.Ordinal))) f.Conditions.Add("stone");
                foreach (string name in names)
                {
                    var cell = Cells(ui, name).FirstOrDefault(c => c.name == name); if (cell == null) continue;
                    Select(cell.gameObject); Read(ui, "inventory", "장비/" + name, f);
                    InventoryStateWord(ui, name, f);
                    if (!name.StartsWith("Equipped_", StringComparison.Ordinal)) continue;
                    // one 바꿔 낄 것 row of the slot: the 견줌 block
                    var row = Scope(ui).GetComponentsInChildren<ContentCell304>(false).FirstOrDefault(c => c != null && c.name.StartsWith("Gear_", StringComparison.Ordinal) && c.name != "Gear_" + cell.Id);
                    if (row != null) { Select(row.gameObject); Read(ui, "inventory", "장비/" + name + "/" + row.name, f); }
                }
                LayoutInventory(ui, f);
            }
            else sheet.Info("inventory: no equipment catalog in this session - only the 석경 tab exists");
            var fragments = ButtonNamed(ui, "FragmentsTab");
            if (fragments != null && !(HarnessUiRules304.Member(ui, "fragmentTab") is bool already && already)) Press(ui, fragments);
            var chips = Cells(ui, "Fragment_").Select(c => c.name).ToList();
            // the condition comes from the save (the same question the page asks: Content304FragmentGrid), not from the chips on
            // the page: held pieces without chips = missing needed texts
            var progress = ui.Session.Progress.ui;
            if (progress != null && WorldMacroCollectionCatalog.AllFragments.Any(x => progress.GetItemCount(x.ItemId) > 0)) f.Conditions.Add("fragment");
            if (chips.Count == 0) Read(ui, "inventory", "석경 (빈 봇짐)", f);
            foreach (string name in chips)
            {
                var chip = Cells(ui, name).FirstOrDefault(c => c.name == name); if (chip == null) continue;
                Select(chip.gameObject); Read(ui, "inventory", "석경/" + name, f);
            }
        }

        /// <summary>AC-TD27.4 on the 소지품 page: the selected 칸's own state word stands exactly once, in the detail head.</summary>
        static void InventoryStateWord(PlaytestUiRoot ui, string cell, PageFindings f)
        {
            var levels = HarnessUiRules304.LiveTexts(Scope(ui)).Where(g => g.name == "SelectedGearLevel").Select(g => Norm(HarnessUiRules304.TextOf(g))).ToList();
            if (cell.StartsWith("Stone_", StringComparison.Ordinal) && !(levels.Count == 1 && Regex.IsMatch(levels[0], @"\A\d+단\z")))
                f.StateWords.Add(cell + ": the detail does not carry one grade word (" + string.Join(" / ", levels) + ")");
            if (cell.StartsWith("VirtueSlot_", StringComparison.Ordinal) && !(levels.Count == 1 && Regex.IsMatch(levels[0], @"\A(새겨짐|새기지 못함|쓰임)\z")))
                f.StateWords.Add(cell + ": the detail does not carry one engraving word (" + string.Join(" / ", levels) + ")");
        }

        static void WalkCodex(PlaytestUiRoot ui, PageFindings f)
        {
            var known = ui.Session.Progress.ui.knownSpellLetters; bool measured = false;
            foreach (string name in Cells(ui, "Codex_").Select(c => c.name).ToList())
            {
                var cell = Cells(ui, name).FirstOrDefault(c => c.name == name); if (cell == null) continue;
                Select(cell.gameObject); Read(ui, "codex", name, f);
                // answer 1: a letter not found yet says so once, in the detail; a found one never
                bool found = known != null && known.Contains(cell.Id);
                // a found letter of the matrix (an extra letter outside the catalog has a glyph cell without a name)
                if (found && WorldMacroCollectionCatalog.TryGetSpell(cell.Id, out _)) f.Conditions.Add("found");
                int said = HarnessUiRules304.LiveTexts(Scope(ui)).Count(g => Norm(HarnessUiRules304.TextOf(g)) == "미발견");
                if (said != (found ? 0 : 1)) f.StateWords.Add(name + ": 미발견 stands " + said + " time(s), expected " + (found ? 0 : 1));
                if (!found && !HarnessUiRules304.IsUnfoundCodexCell(cell.GetComponent<Selectable>())) f.StateWords.Add(name + ": an unfound 칸 that is not the 묵등 block alone");
                if (found && !measured) { LayoutCodex(ui, f); measured = true; }
            }
        }

        static void WalkOptions(PlaytestUiRoot ui, PageFindings f, Sheet sheet)
        {
            foreach (string tab in new[] { "화면", "소리", "조작", "접근성" })
            {
                var b = ButtonNamed(ui, "OptionTab_" + tab); if (b == null) { f.NotAllowed.Add("tab OptionTab_" + tab + " is missing"); continue; }
                Press(ui, b); Read(ui, "options", tab, f);
                if (tab != "화면") continue;
                // 설정 ③: the "what runs now" block shows only while a row is 적용 전. The draft is stepped and stepped back: nothing is applied.
                var panel = HarnessUiRules304.Member(ui, "menu304Options") as MenuOptionsPanel304;
                var block = Scope(ui).GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "CurrentDisplayBlock");
                var row = panel != null ? panel.Rows.FirstOrDefault(r => r.Label == "화면 모드") : null;
                f.LayoutChecks++;
                if (panel == null || block == null || row == null) { f.Layout.Add("설정 ③: panel / CurrentDisplayBlock / the 화면 모드 row was not found"); continue; }
                bool pendingBefore = panel.Rows.Any(r => r.Pending != null && r.Pending());
                if (block.gameObject.activeSelf != pendingBefore) f.Layout.Add("설정 ③: the block is " + (block.gameObject.activeSelf ? "shown" : "hidden") + " while pending = " + pendingBefore);
                panel.StepRow(row, 1);
                try
                {
                    Canvas.ForceUpdateCanvases();
                    if (!block.gameObject.activeSelf) f.Layout.Add("설정 ③: the block stays hidden with a row 적용 전");
                    Read(ui, "options", tab + " (적용 전)", f);
                }
                finally { panel.StepRow(row, -1); }
                if (block.gameObject.activeSelf != pendingBefore) f.Layout.Add("설정 ③: the block did not return to its state after the draft was stepped back");
            }
        }

        // ------------------------------------------------------------------ reading one state
        static void Read(PlaytestUiRoot ui, string id, string label, PageFindings f)
        {
            Settle(ui);
            var scope = Scope(ui);
            f.States++;
            string where = label.Length > 0 ? "[" + label + "] " : "";
            var goneTexts = GoneTexts.Where(g => g.Page == id).ToList();
            var style = ui.Theme != null ? PlaytestUiView.Style(ui.Theme) : null;
            var sentences = new HashSet<string>();
            if (style != null && id == "codex" && style.StrokeRules != null) foreach (var r in style.StrokeRules) if (r != null && !string.IsNullOrWhiteSpace(r.Rule)) sentences.Add(r.Rule);
            if (style != null && id == "options" && style.OptionHelp != null)
                foreach (var h in style.OptionHelp)
                {
                    if (h == null) continue;
                    if (!string.IsNullOrWhiteSpace(h.Summary)) sentences.Add(h.Summary);
                    if (!string.IsNullOrWhiteSpace(h.Warning)) sentences.Add(h.Warning);
                    if (h.Values != null) foreach (var v in h.Values) if (v != null && !string.IsNullOrWhiteSpace(v.Meaning)) sentences.Add(v.Meaning);
                }

            // every text object, shown or hidden: removed texts (AC-TD27.2)
            foreach (var g in HarnessUiRules304.Texts(scope, true))
            {
                string text = Norm(HarnessUiRules304.TextOf(g)); if (string.IsNullOrWhiteSpace(text)) continue;
                string path = "/" + HarnessUiRules304.Path(g.transform, scope);
                if (path.Contains("/Legacy304/")) continue;   // the hidden legacy heading / status Text of the #304 frame (never drawn)
                foreach (var entry in goneTexts) if (entry.TextRx.IsMatch(text)) f.GoneText.Add(where + entry.Audit + " \"" + Short(text) + "\" at " + path);
                if (sentences.Contains(text)) f.GoneText.Add(where + (id == "codex" ? "술식 ㉮ 획 규칙" : "설정 ㉮ 설명 칸") + " \"" + Short(text) + "\" at " + path);
            }

            // live texts: the allow list (AC-TD27.1), state words (AC-TD27.4), overflow (AC-TD27.6)
            var stateInDetail = new Dictionary<string, int>();
            foreach (var g in HarnessUiRules304.LiveTexts(scope))
            {
                string text = Norm(HarnessUiRules304.TextOf(g)), path = "/" + HarnessUiRules304.Path(g.transform, scope);
                f.Texts++; f.Seen.Add(path + "\n" + text);
                string verdict = Verdict(id, path, text);
                if (verdict != "ok") f.NotAllowed.Add(where + (verdict == "unlisted" ? "unlisted object " : "text not allowed ") + path + " \"" + Short(text) + "\"");
                if (StateWord.IsMatch(text))
                {
                    if (GridCell.IsMatch(path)) f.StateWords.Add(where + "in a grid: " + path + " \"" + text + "\"");
                    else if (Detail.IsMatch(path))
                    {
                        string key = Regex.Replace(text, @"\d+", "N");
                        stateInDetail[key] = stateInDetail.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                    else f.StateWords.Add(where + "outside a detail: " + path + " \"" + text + "\"");
                }
                if (g is TMP_Text tmp) { string over = HarnessUiRules304.Overflow(tmp); if (over != null) f.Overflow.Add(where + path + " " + over); }
                else if (g is Text legacy) { string over = HarnessUiRules304.Overflow(legacy); if (over != null) f.Overflow.Add(where + path + " " + over); }
            }
            foreach (var pair in stateInDetail) if (pair.Value > 1) f.StateWords.Add(where + "\"" + pair.Key + "\" stands " + pair.Value + " times in the detail");

            // objects (AC-TD27.3) and holders left without content
            var goneObjects = GoneObjects.Where(o => o.Page == id).ToList();
            foreach (var t in scope.GetComponentsInChildren<Transform>(true))
            {
                if (t == scope) continue;
                string path = "/" + HarnessUiRules304.Path(t, scope);
                foreach (var entry in goneObjects) if (entry.PathRx.IsMatch(path)) f.GoneObject.Add(where + entry.Audit + " " + path);
                if (t.childCount == 0 && t.gameObject.activeInHierarchy && t.GetComponents<Component>().Length == 1 && t.name != "DabAnchor") f.Empty.Add(t.name);
            }
            foreach (string icon in HarnessUiRules304.IconOnlySelectables(scope)) f.IconOnly.Add(where + icon);
        }

        static string Short(string text) { text = text.Replace("\n", "\\n"); return text.Length > 48 ? text.Substring(0, 48) + "..." : text; }

        // ------------------------------------------------------------------ AC-TD27.7 measures (page px, top-left origin)
        static RectTransform Find(PlaytestUiRoot ui, string name)
            => Scope(ui).GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == name);

        /// <summary>Top-left-origin bounds of `r` in the page's own px (the Page304 rect it lives under).</summary>
        static bool PageRect(RectTransform r, out Rect rect)
        {
            rect = default;
            var fit = r != null ? r.GetComponentInParent<UiPageFit304>() : null; if (fit == null) return false;
            var page = (RectTransform)fit.transform; Rect b = HarnessUiRules304.RectIn(page, r), p = page.rect;
            rect = new Rect(b.xMin - p.xMin, p.yMax - b.yMax, b.width, b.height);
            return true;
        }

        static void Measure(PageFindings f, bool ok, string text) { f.LayoutChecks++; if (!ok) f.Layout.Add(text); }

        static void LayoutInventory(PlaytestUiRoot ui, PageFindings f)
        {
            // 소지품 ①: the two lines over the grid are gone, so the grid starts where the two other columns start (the rule's top)
            var rule = Find(ui, "DetailRule"); var first = Cells(ui, "Equipped_").Concat(Cells(ui, "Stone_")).Concat(Cells(ui, "VirtueSlot_")).FirstOrDefault();
            if (rule == null || first == null || !PageRect(rule, out Rect r) || !PageRect((RectTransform)first.transform, out Rect c)) { Measure(f, false, "소지품 ①: DetailRule / the first 칸 was not found"); return; }
            // theme: the top bar ("Head") of the topmost window under EquipGrid308/Lattice308 - looked up under the lattice, not by
            // the first object of that name on the page. No theme (no lattice): the focus frame's top stroke, 14 px over the chip.
            float top = c.yMin - 14f; string what = "the first 칸 - 14";
            var lattice = Find(ui, "Lattice308");
            if (lattice != null)
            {
                float best = float.MaxValue;
                foreach (var bar in lattice.GetComponentsInChildren<RectTransform>(true))
                    if (bar.name == "Head" && PageRect(bar, out Rect h) && h.yMin < best) best = h.yMin;
                if (best == float.MaxValue) { Measure(f, false, "소지품 ①: Lattice308 has no window head (Head) to measure"); return; }
                top = best; what = "the lattice's top bar";
            }
            Measure(f, Mathf.Abs(top - r.yMin) <= 2f, "소지품 ①: the grid's top (" + what + ", " + top.ToString("0.#") + ") is not on the columns' top (" + r.yMin.ToString("0.#") + ")");
        }

        static void LayoutCodex(PlaytestUiRoot ui, PageFindings f)
        {
            // 술식 ⑤ ㉮: the divider follows the film (26 px under its frames); 술식 ②: the 받침 head follows the 조합
            var divider = Find(ui, "EffectDivider"); var film = Find(ui, "Film_1");
            if (divider != null && film != null && PageRect(divider, out Rect d) && PageRect(film, out Rect m))
                Measure(f, d.yMin - m.yMax >= 8f && d.yMin - m.yMax <= 48f, "술식 ⑤ ㉮: " + (d.yMin - m.yMax).ToString("0.#") + " px between the film and the divider (8 - 48 expected)");
            var head = Find(ui, "FinalHead"); var composition = Find(ui, "Composition");
            if (head != null && composition != null && PageRect(head, out Rect hr) && PageRect(composition, out Rect cr))
                Measure(f, hr.yMin - cr.yMax >= 0f && hr.yMin - cr.yMax <= 40f, "술식 ②: " + (hr.yMin - cr.yMax).ToString("0.#") + " px between the 조합 and the 받침 head (0 - 40 expected)");
        }

        static void LayoutChapae(PlaytestUiRoot ui, PageFindings f)
        {
            // 차패 ①: without the state line the name sits on the seal's middle line
            for (int i = 0; i < 5; i++)
            {
                var seal = Find(ui, "Virtue_" + i); var name = Find(ui, "VirtueName_" + i);
                if (seal == null || name == null || !PageRect(seal, out Rect s) || !PageRect(name, out Rect n)) { Measure(f, false, "차패 ①: Virtue_" + i + " / VirtueName_" + i + " was not found"); continue; }
                Measure(f, Mathf.Abs(s.center.y - n.center.y) <= 3f, "차패 ①: VirtueName_" + i + " is " + (n.center.y - s.center.y).ToString("0.#") + " px off the seal's middle line");
            }
        }

        static void LayoutService(PlaytestUiRoot ui, PageFindings f)
        {
            // 정비 ①: the "next" column closed up behind the shorter "now" value - it starts 88 px right of the now value's start
            // (560 -> 648; before D308-27 the column stood at 690 = 130 px, so this measure tells the two layouts apart) and the
            // two values do not touch
            foreach (var next in Scope(ui).GetComponentsInChildren<RectTransform>(false).Where(t => t.name.StartsWith("UpgradeNext_", StringComparison.Ordinal)))
            {
                var now = next.parent.Find("UpgradeLevel_" + next.name.Substring("UpgradeNext_".Length)) as RectTransform;
                if (now == null || !PageRect(now, out Rect a) || !PageRect(next, out Rect b)) { Measure(f, false, "정비 ①: " + next.name + " has no UpgradeLevel beside it"); continue; }
                Measure(f, Mathf.Abs(b.xMin - a.xMin - 88f) <= 4f, "정비 ①: " + next.name + " starts " + (b.xMin - a.xMin).ToString("0.#") + " px right of " + now.name + " (88 expected; 130 = the column did not close up)");
                Measure(f, b.xMin - a.xMax >= 6f, "정비 ①: " + (b.xMin - a.xMax).ToString("0.#") + " px between " + now.name + " and " + next.name + " (at least 6 expected)");
            }
        }
    }
}
