using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Oheangbu.App.World.UI
{
    /// <summary>#304 UI style: every token of IMPLEMENTATION §1.1 plus the sprite / material / TMP bundles.
    /// One asset (Assets/_Project/Art/UI/UI304/UiStyle304.asset) referenced by PlaytestUiThemeSO.Style304 and
    /// WorldMacroHudSkinProfileSO.Style304. Field initializers ARE the design values (ui304-setup copies them into the asset).
    /// Resolve it with UiStyle304SO.Resolve(theme / skin) or PlaytestUiView.Style(...); never search the scene for it.</summary>
    [CreateAssetMenu(menuName = "Oheangbu/UI/UI304 Style", fileName = "UiStyle304")]
    public sealed class UiStyle304SO : ScriptableObject
    {
        [Header("Colour (DESIGN §2.1) - only these")]
        [Tooltip("먹 #141413: text on paper, ink strokes, ink meter")] public Color Ink = Hex(0x141413);
        [Tooltip("먹장막 #0F0F0E: menu veil (alpha from veil_wash)")] public Color Veil = Hex(0x0F0F0E);
        [Tooltip("재 #5A574F: secondary text on paper")] public Color Ash = Hex(0x5A574F);
        [Tooltip("안개 #A7A398: secondary text on veil / ink")] public Color Mist = Hex(0xA7A398);
        [Tooltip("한지 #E6E2D7: text on veil / ink, focus underlay, rims")] public Color Paper = Hex(0xE6E2D7);
        [Tooltip("생지 #DFDBD0: codex / map sheet, label underlay")] public Color Sheet = Hex(0xDFDBD0);
        [Tooltip("칩 #D6D1C4: equipment chip")] public Color Chip = Hex(0xD6D1C4);
        [Tooltip("주사 #B8392B: THE accent (방점, 체력, 락온, 목적지, 부인, 적용 전)")] public Color Cinnabar = Hex(0xB8392B);
        [Tooltip("주사 밝음 #D65A43: cinnabar TEXT on ink / veil only (20px bold+)")] public Color CinnabarLift = Hex(0xD65A43);
        [Tooltip("비활성 #6E6B64: disabled text on veil (always with a reason meta)")] public Color Off = Hex(0x6E6B64);
        [Tooltip("건반 턱 #B8B3A7")] public Color KeyLip = Hex(0xB8B3A7);

        [Header("State grammar (DESIGN §5.0)")]
        public float HoverAlpha = .16f;
        public float PressTone = .86f, PressScaleY = .9f, PressDrop = 2f;
        public float ConfirmDimAlpha = .66f;

        [Header("Layout (1920x1080, top-left origin)")]
        public float SafeLeft = 64, SafeRight = 64, SafeTop = 40, SafeBottom = 64;
        public float MinHit = 48;
        /// <summary>IMPLEMENTATION §1.1 names; the authoritative values are Rail.TabGap / Rail.TabsBottom / Rail.Line.</summary>
        public float TabGap => Rail.TabGap;
        public float RailY => Rail.TabsBottom;
        public Rect RailLine => Rail.Line;
        [Tooltip("page -> veil lift x (붓 들림 자리). Unlisted pages use DefaultVeilLiftX.")]
        public List<VeilLift304> VeilLiftX = DefaultVeilLifts();
        public float DefaultVeilLiftX = 1960;

        [Header("Type (DESIGN §3, IMPLEMENTATION §3)")]
        public List<FontFamily304> Fonts = DefaultFonts();
        public List<TypeRole> Type = DefaultRoles();
        [Tooltip("hanja baked into the fallback atlas before first use (UiText304.Prewarm)")]
        public string PrewarmHanja = "五行符差牌仁禮義智信木火土金水北東南西錄";

        [Header("Motion (DESIGN §6)")]
        public MotionTokens304 Motion = new MotionTokens304();

        [Header("Brand marks")]
        [Tooltip("부인(符印) seals on menus, dialogs, slips and cards. Off at the user's request (2026-09-30: \"전부 지워\"); V.Seal draws nothing while off.")]
        public bool ShowSeals = false;

        [Header("Components")]
        public MeterSpec304 Meter = new MeterSpec304();
        public BearingSpec304 Bearing = new BearingSpec304();
        public ToastSpec304 Toast = new ToastSpec304();
        public ArrivalSpec304 Arrival = new ArrivalSpec304();
        public PromptSpec304 Prompt = new PromptSpec304();
        public LockOnSpec304 LockOn = new LockOnSpec304();
        public KeycapSpec304 Keycap = new KeycapSpec304();
        public RailSpec304 Rail = new RailSpec304();
        [Tooltip("받침 획 크기 등급 (IMPLEMENTATION §2 border table)")]
        public List<StrokeSprite304> Strokes = DefaultStrokes();
        public Vector2 DabSize = new Vector2(34, 26);
        public float DabRotation = -14;

        [Header("Channels")]
        [Tooltip("알림 채널 asset (UiNoticeChannel.asset). Senders: V.Style(Theme).Notices.Raise(...); the HUD toast stack subscribes.")]
        public UiNoticeChannelSO Notices;

        [Header("Assets (filled by ui304-setup)")]
        public UiSprites304 Sprites = new UiSprites304();
        public UiMaterials304 Materials = new UiMaterials304();

        [Header("String tables (data, not code)")]
        [Tooltip("도감: one-line stroke rule per jamo")] public List<JamoRule304> StrokeRules = DefaultStrokeRules();
        [Tooltip("설정 설명 칸: values + meanings per option label")] public List<OptionHelp304> OptionHelp = DefaultOptionHelp();

        // ------------------------------------------------------------------ resolve (no scene search, no new singleton)
        static UiStyle304SO fallback;
        static bool warned, warnedAmbiguousStroke;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { fallback = null; warned = false; warnedAmbiguousStroke = false; }

        /// <summary>theme.Style304, or an asset-less default instance (tokens only, no sprites / fonts) with one warning.</summary>
        public static UiStyle304SO Resolve(PlaytestUiThemeSO theme) => theme != null && theme.Style304 != null ? theme.Style304 : Fallback;
        public static UiStyle304SO Resolve(WorldMacroHudSkinProfileSO skin) => skin != null && skin.Style304 != null ? skin.Style304 : Fallback;
        public static bool IsFallback(UiStyle304SO style) => style == null || style == fallback;
        public static UiStyle304SO Fallback
        {
            get
            {
                if (fallback == null)
                {
                    fallback = CreateInstance<UiStyle304SO>(); fallback.name = "UiStyle304 (fallback)"; fallback.hideFlags = HideFlags.DontSave;
                    if (!warned) { warned = true; Debug.LogWarning("[UI304] Style304 is not assigned; using token defaults without sprites/fonts. Run UiOverhaul304 ui304-setup."); }
                }
                return fallback;
            }
        }

        // ------------------------------------------------------------------ lookups
        [NonSerialized] Dictionary<UiType304, TypeRole> roleCache;
        [NonSerialized] int roleCacheCount = -1;
        void OnEnable() { roleCache = null; }
        void OnValidate() { roleCache = null; }

        /// <summary>Role by id. Never null: a role missing from the asset falls back to the code default table.</summary>
        public TypeRole Role(UiType304 id)
        {
            if (roleCache == null || roleCacheCount != Type.Count)
            {
                roleCache = new Dictionary<UiType304, TypeRole>(); roleCacheCount = Type.Count;
                foreach (var r in Type) if (r != null && !roleCache.ContainsKey(r.Id)) roleCache[r.Id] = r;
            }
            if (roleCache.TryGetValue(id, out var role)) return role;
            role = DefaultRoles().Find(x => x.Id == id) ?? new TypeRole(id, UiFont304.Sans400, 22, 1.35f);
            roleCache[id] = role; return role;
        }

        /// <summary>SDF asset for a design font family (null = TMP default font).</summary>
        public TMP_FontAsset FontFor(UiFont304 family)
        {
            foreach (var f in Fonts) if (f != null && f.Family == family && f.Font != null) return f.Font;
            foreach (var r in Type) if (r != null && r.Family == family && r.Font != null) return r.Font;
            return null;
        }

        public TMP_FontAsset FontOf(TypeRole role) => role == null ? null : role.Font != null ? role.Font : FontFor(role.Family);

        /// <summary>Preset material for a font; null means "use font.material" (Paper / Ink, or a preset that is missing / stale).</summary>
        public Material TmpMaterial(TMP_FontAsset font, TmpPreset304 preset)
        {
            if (font == null || preset == TmpPreset304.Paper || preset == TmpPreset304.Ink) return null;
            foreach (var m in Materials.Tmp)
                if (m != null && m.Font == font && m.Preset == preset && MatchesAtlas(m.Material, font)) return m.Material;
            return null;
        }

        /// <summary>The material a role should render with in `font` (role.Material when it still matches that atlas).</summary>
        public Material MaterialOf(TypeRole role, TMP_FontAsset font)
        {
            if (role == null || font == null) return null;
            if (role.Material != null && MatchesAtlas(role.Material, font)) return role.Material;
            return TmpMaterial(font, role.Preset);
        }

        /// <summary>True when a TMP material samples this font's (first) atlas, i.e. it is safe to use with the font.</summary>
        public static bool MatchesAtlas(Material m, TMP_FontAsset font)
            => m != null && font != null && m.HasProperty(ShaderUtilities.ID_MainTex) && m.GetTexture(ShaderUtilities.ID_MainTex) == font.atlasTexture;

        /// <summary>Slice data for a stroke class (never null; Sprite may be null before ui304-setup).</summary>
        public StrokeSprite304 Stroke(StrokeClass304 cls)
        {
            if (cls == StrokeClass304.Auto) cls = StrokeClass304.WetS;
            foreach (var s in Strokes) if (s != null && s.Class == cls) return s;
            return DefaultStrokes().Find(s => s.Class == cls) ?? new StrokeSprite304(cls, 100, 100, 0, 0, 0, 0, false);
        }

        /// <summary>IMPLEMENTATION §2 size-class rule: 80~124 -> wet_s, 118~170 -> wet_m, 300+ -> band. The bands overlap on
        /// 118~124 and the design uses BOTH there (options focus = wet_m h118, dialogue focus = wet_s h124), so Auto resolves
        /// that range to wet_s and logs one warning: pass the class explicitly for heights 118~124.</summary>
        public StrokeClass304 StrokeFor(float height)
        {
            if (height >= Stroke(StrokeClass304.Band).MinH) return StrokeClass304.Band;
            var s = Stroke(StrokeClass304.WetS);
            if (height > s.MaxH) return StrokeClass304.WetM;
            if (height >= Stroke(StrokeClass304.WetM).MinH && !warnedAmbiguousStroke)
            {
                warnedAmbiguousStroke = true;
                Debug.LogWarning("[UI304] Stroke height " + height + " is in the wet_s / wet_m overlap (118~124); Auto picked WetS. Pass StrokeClass304.WetM or WetS explicitly (options rows = WetM, dialogue choices = WetS).");
            }
            return StrokeClass304.WetS;
        }

        public VeilLift304 VeilFor(string page)
        {
            foreach (var v in VeilLiftX) if (v != null && v.Page == page) return v;
            return new VeilLift304(page ?? "", DefaultVeilLiftX);
        }
        public float VeilLift(string page) => VeilFor(page).LiftX;

        /// <summary>도감 one-line stroke rule for a jamo, or "" when the table has none.</summary>
        public string StrokeRule(string jamo)
        {
            foreach (var r in StrokeRules) if (r != null && r.Jamo == jamo) return r.Rule ?? "";
            return "";
        }

        /// <summary>설정 설명 칸 entry for an option label, or null.</summary>
        public OptionHelp304 Help(string option)
        {
            foreach (var h in OptionHelp) if (h != null && h.Option == option) return h;
            return null;
        }

        /// <summary>Toast hold seconds by kind (보통 3.6 / 습득 5 / 오류 8) from Motion.ToastHold*Ms.</summary>
        public float ToastSeconds(UiNoticeKind304 kind)
            => (kind == UiNoticeKind304.Error ? Motion.ToastHoldErrorMs : kind == UiNoticeKind304.Pickup ? Motion.ToastHoldPickupMs : Motion.ToastHoldMs) / 1000f;

        /// <summary>Arrival card hold seconds (Motion.ArrivalHoldMs).</summary>
        public float ArrivalSeconds => Motion.ArrivalHoldMs / 1000f;

        // ------------------------------------------------------------------ colour helpers
        public static Color Hex(uint rgb) => new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
        public static Color A(Color c, float alpha) { c.a = alpha; return c; }

        // ------------------------------------------------------------------ design defaults (DESIGN.md / IMPLEMENTATION.md)
        public static List<FontFamily304> DefaultFonts()
        {
            var list = new List<FontFamily304>();
            foreach (UiFont304 f in Enum.GetValues(typeof(UiFont304))) list.Add(new FontFamily304(f));
            return list;
        }

        public static List<TypeRole> DefaultRoles()
        {
            const UiFont304 S6 = UiFont304.Serif600, S7 = UiFont304.Serif700, S8 = UiFont304.Serif800, S9 = UiFont304.Serif900;
            const UiFont304 PR = UiFont304.Prose, N4 = UiFont304.Sans400, N7 = UiFont304.Sans700;
            const TmpPreset304 UP = TmpPreset304.Ink_UnderPaper;
            return new List<TypeRole>
            {
                new TypeRole(UiType304.Lockup150, S9, 150, 1.0f, 0, TmpPreset304.Rubbing_Lite),   // 타이틀 세로 오행부
                new TypeRole(UiType304.Glyph240, S9, 240, 1.0f, 0, TmpPreset304.Rubbing),         // 도감 선택 글자
                new TypeRole(UiType304.Region72, S9, 72, 1.02f),                                  // 세로 권역명 (쪽지, 도착 카드)
                new TypeRole(UiType304.CellGlyph64, S9, 64, 1.1f),                                // 도감 칸 글자
                new TypeRole(UiType304.Speaker60, S9, 60, 1.1f),                                  // 화자, 아이템 이름, 差 牌, 여정의 끝 제목
                new TypeRole(UiType304.ChipGlyph56, S9, 56, 1.1f),                                // 석경 탭 글자 칩
                new TypeRole(UiType304.ToastGlyph50, S9, 50, 1.1f, 0, TmpPreset304.Rubbing_Lite), // 알림 조각 글자
                new TypeRole(UiType304.Region48, S9, 48, 1.1f),                                   // 로딩 권역
                new TypeRole(UiType304.Heading44, S9, 44, 1.2f),                                  // 사물 서술 제목 (화자 자리)
                new TypeRole(UiType304.MapRegion44, S9, 44, 1.2f, 12, UP),                        // 지도 권역명 .12em
                new TypeRole(UiType304.BearingMajor24, S9, 24, 1.0f, 0, UP),                      // 방위 정방위 (Inverse: Paper_UnderInk)
                new TypeRole(UiType304.Display50, S8, 50, 1.2f, -1),                              // 일시정지 머리 문장
                new TypeRole(UiType304.Headline44, S8, 44, 1.2f, -1),                             // 주 버튼, 자모 조합, 행 머리 자모
                new TypeRole(UiType304.Figure40, S8, 40, 1.0f),                                   // 수치 40 (tabular)
                new TypeRole(UiType304.Figure36, S8, 36, 1.0f),                                   // 조선통보 수치 36
                new TypeRole(UiType304.Title36, S8, 36, 1.2f),                                    // 확인 제목, 착용, 여정 기록됨
                new TypeRole(UiType304.Title34, S8, 34, 1.2f),                                    // 선택 탭, 상세 카드 제목
                new TypeRole(UiType304.Title32, S8, 32, 1.2f),                                    // 효과 줄, 설정 적용, 선택 하위 탭
                new TypeRole(UiType304.Title30, S8, 30, 1.2f),                                    // 선택지, ‹ ›, 지금 있는 곳
                new TypeRole(UiType304.Title26, S8, 26, 1.3f),                                    // 도감 태그 목
                new TypeRole(UiType304.Title24, S8, 24, 1.25f),                                   // 도감 열 머리 이름
                new TypeRole(UiType304.MapTitle24, S8, 24, 1.25f, 0, UP),                         // 강토 지도 · 청림
                new TypeRole(UiType304.Serif700_28, S7, 28, 1.2f),                                // 비초점 선택지
                new TypeRole(UiType304.Serif700_24, S7, 24, 1.2f),                                // 의뢰 제목
                new TypeRole(UiType304.Serif700_22, S7, 22, 1.14f),                               // 세로 지명, 도감 행 분류
                new TypeRole(UiType304.MapLabel21, S7, 21, 1.25f, 0, UP),                         // 지도 장소 라벨
                new TypeRole(UiType304.Serif700_20, S7, 20, 1.2f),                                // 오덕 한자 메타
                new TypeRole(UiType304.BearingMinor20, S7, 20, 1.0f, 0, UP),                      // 방위 간방위
                new TypeRole(UiType304.BearingLabel20, S7, 20, 1.2f, 0, UP),                      // 방위 목적 라벨
                new TypeRole(UiType304.Label28, S6, 28, 1.25f),                                   // 메뉴 항목
                new TypeRole(UiType304.Label27, S6, 27, 1.25f),                                   // HUD 프롬프트 동사구
                new TypeRole(UiType304.Label26, S6, 26, 1.25f),                                   // 설정 행, 보조 버튼, 목록 이름
                new TypeRole(UiType304.Label24, S6, 24, 1.25f),                                   // 비선택 탭, 범례 이름, 알림 제목
                new TypeRole(UiType304.Label22, S6, 22, 1.25f),                                   // 장비 이름, 지도 버튼 라벨
                new TypeRole(UiType304.Prose28, PR, 28, 1.7f, 1),                                 // 대사 (한 줄 36음절, 폭 1000)
                new TypeRole(UiType304.Body24, N4, 24, 1.55f),                                    // 설정 값, 효과 본문 24
                new TypeRole(UiType304.ValueBold24, N7, 24, 1.2f),                                // 초점 행의 값
                new TypeRole(UiType304.Body22, N4, 22, 1.55f),                                    // 설명, 상세 카드, 경고
                new TypeRole(UiType304.Meta20, N4, 20, 1.35f, 2),                                 // 메타 (최소 크기)
                new TypeRole(UiType304.MetaBold20, N7, 20, 1.35f, 2),                             // 적용 전, 굵은 메타
                new TypeRole(UiType304.Keycap18, N7, 18, 1.0f),                                   // 건반 글자
                new TypeRole(UiType304.KeycapSm16, N7, 16, 1.0f),                                 // 작은 건반 글자
                new TypeRole(UiType304.OrderNo16, N7, 16, 1.0f),                                  // 획순 번호 (주사 원 24)
                new TypeRole(UiType304.Title28, S8, 28, 1.2f),                                    // 일시정지 진행 중인 의뢰 제목 (t-title 28)
                new TypeRole(UiType304.Serif900_28, S9, 28, 1.0f),                                // 도감 열 머리 한자 (도장 안)
                new TypeRole(UiType304.Serif900_22, S9, 22, 1.0f),                                // 도감 태그 木, 받침 표 글자, 지도 북
                new TypeRole(UiType304.Serif800_20, S8, 20, 1.2f),                                // 일시정지 차패 메타 仁禮義智信
            };
        }

        public static List<StrokeSprite304> DefaultStrokes() => new List<StrokeSprite304>
        {
            new StrokeSprite304(StrokeClass304.WetS, 900, 110, 90, 300, 80, 124, true),
            new StrokeSprite304(StrokeClass304.WetM, 1200, 170, 140, 400, 118, 170, true),
            new StrokeSprite304(StrokeClass304.Band, 1600, 400, 200, 520, 300, 100000, true),
            new StrokeSprite304(StrokeClass304.Dry, 1800, 90, 80, 700, 0, 90, true),
            new StrokeSprite304(StrokeClass304.Line, 2000, 40, 0, 0, 0, 40, false),
            new StrokeSprite304(StrokeClass304.Hair, 1400, 36, 0, 0, 0, 36, false),
            new StrokeSprite304(StrokeClass304.HairRim, 1400, 36, 0, 0, 0, 36, false),
            new StrokeSprite304(StrokeClass304.Swell, 420, 80, 0, 0, 0, 80, false),
            new StrokeSprite304(StrokeClass304.Short, 520, 44, 0, 0, 0, 44, false),
            new StrokeSprite304(StrokeClass304.Spine, 760, 1500, 0, 0, 0, 1640, false),
            new StrokeSprite304(StrokeClass304.Sweep, 1700, 620, 0, 0, 0, 620, false),
        };

        public static List<VeilLift304> DefaultVeilLifts() => new List<VeilLift304>
        {
            new VeilLift304("일시정지", 1100, new Vector3(-420, 610, .35f)),
            new VeilLift304("옵션", 1700, new Vector3(-400, 660, .28f)),
            new VeilLift304("조작 안내", 1700, new Vector3(-400, 660, .28f)),   // 조작 = 설정과 같은 하위 탭 문법 (DESIGN §7.9)
            new VeilLift304("소지품", 1960, new Vector3(-380, 640, .3f)),
            new VeilLift304("술식 도감", 1960),
            new VeilLift304("지도", 1960),
            new VeilLift304("장비 상점", 1400),
            new VeilLift304("장비 강화", 1400),
        };

        public static List<JamoRule304> DefaultStrokeRules() => new List<JamoRule304>
        {
            new JamoRule304("ㄱ", "가로에서 꺾어 한 번에 내린다"),
            new JamoRule304("ㄴ", "위에서 내려 꺾어 오른쪽으로 한 번에 긋는다"),
            new JamoRule304("ㅁ", "왼쪽 세로, 위에서 꺾어 내리는 획, 아래 가로 순으로 닫는다"),
            new JamoRule304("ㅅ", "왼쪽으로 삐친 뒤 가운데에서 오른쪽으로 내린다"),
            new JamoRule304("ㅇ", "위에서 왼쪽으로 돌려 한 번에 닫는다"),
            new JamoRule304("ㅏ", "세로를 먼저, 짧은 가로를 뒤에"),
            new JamoRule304("ㅓ", "짧은 가로를 먼저, 세로를 뒤에"),
            new JamoRule304("ㅗ", "짧은 세로를 먼저, 긴 가로를 뒤에"),
            new JamoRule304("ㅜ", "긴 가로를 먼저, 짧은 세로를 뒤에"),
        };

        public static List<OptionHelp304> DefaultOptionHelp()
        {
            const string revert = "적용한 뒤 15초 안에 확인하지 않으면 이전 화면 설정으로 돌아간다.";
            OptionValueHelp304 V(string v, string m) => new OptionValueHelp304(v, m);
            return new List<OptionHelp304>
            {
                new OptionHelp304("해상도", "화면을 그리는 픽셀 수. 높을수록 선명하고 무겁다.", revert),
                new OptionHelp304("화면 모드", "", revert,
                    V("창 모드", "다른 창과 나란히 둔다"),
                    V("전체 화면", "화면을 혼자 쓴다. 전환이 느리다"),
                    V("테두리 없는 전체 화면", "전체 화면처럼 보이고 창 전환이 빠르다")),
                new OptionHelp304("품질", "그림자와 먹 번짐의 정밀도. 낮추면 프레임이 오른다.", revert),
                new OptionHelp304("수직동기화", "", revert,
                    V("켜기", "화면 주사율을 우선한다. 찢김이 없다"),
                    V("끄기", "프레임 제한을 따른다")),
                new OptionHelp304("프레임 제한", "초당 그리는 장면 수의 상한. 수직동기화가 켜지면 쓰지 않는다.", revert,
                    V("제한 없음", "기기가 낼 수 있는 만큼 그린다")),
                new OptionHelp304("전체 음량", "모든 소리의 크기.", ""),
                new OptionHelp304("게임 효과음", "세계와 전투의 소리 크기.", ""),
                new OptionHelp304("UI 효과음", "메뉴와 알림의 소리 크기.", ""),
                new OptionHelp304("마우스 감도", "시점이 도는 빠르기. 작도 좌표와 필세는 감도의 영향을 받지 않는다.", ""),
                new OptionHelp304("시점 Y축 반전", "", "",
                    V("켜기", "마우스를 올리면 시점이 내려간다"),
                    V("끄기", "마우스를 올리면 시점이 올라간다")),
                new OptionHelp304("UI 크기", "메뉴와 HUD의 크기. 본문 크기와 따로 적용된다.", ""),
                new OptionHelp304("본문 크기", "28px 이하 글자의 배율.", ""),
                new OptionHelp304("방위선", "", "",
                    V("표시", "화면 위쪽에 방위 먹선과 쉼터·목적 방향을 보인다"),
                    V("숨기기", "방위 먹선을 숨긴다")),
                new OptionHelp304("지도 펼침 동작 줄이기", "", "",
                    V("켜기", "접힘 대신 짧은 전환으로 지도를 연다. 획 긋기도 짧은 페이드가 된다"),
                    V("끄기", "지도가 두 번 접힌 한지로 펼쳐진다")),
            };
        }
    }
}
