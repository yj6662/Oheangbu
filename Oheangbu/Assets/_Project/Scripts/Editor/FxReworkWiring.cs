using System.Text;
using Oheangbu.App;
using UnityEditor;
using UnityEngine;

namespace Oheangbu.EditorTools
{
    // [SPEC-SPELL-FX-REWORK §4] 결합 프리팹 재배선의 단일 출처(재실행 가능) — 배리언트 오버라이드·추가 GO만 손댄다(유료 원본 무접촉).
    // 메뉴 또는 reflection-method-call(WireAll)로 실행. 결과 문자열=무엇을 찾고 바꿨는지(검증 기록용).
    public static class FxReworkWiring
    {
        private const string Prefabs = "Assets/_Project/Prefabs/";
        private const string Mats = "Assets/_Project/Art/Materials/";
        private const string Gen = "Assets/_Project/Art/Generated/";

        [MenuItem("Oheangbu/Dev/FX 재작업 프리팹 재배선 (5자)")]
        public static void WireAllMenu() => Debug.Log(WireAll());

        public static string WireAll()
        {
            var sb = new StringBuilder();
            sb.AppendLine(WireSo());
            sb.AppendLine(WireO());
            sb.AppendLine(WireGo());
            sb.AppendLine(WireNo());
            sb.AppendLine(WireMo());
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        // 소 — 재질 격리(InkMetal)·트레일 투명 재질. 메시는 0단계 그대로(CrystalSpike_So)
        public static string WireSo()
        {
            return Edit("SpellFx_So", root =>
            {
                var fx = root.GetComponentInChildren<SpikeVolleyEffect>(true);
                if (fx == null) return "SpikeVolleyEffect 없음";
                var so = new SerializedObject(fx);
                SetRef(so, "_material", Load<Material>(Mats + "M_SpellBodySpike_So.mat"));
                SetRef(so, "_trailMaterial", Load<Material>(Mats + "M_SpellTrail_So.mat"));
                so.ApplyModifiedPropertiesWithoutUndo();
                return "Volley: _material=M_SpellBodySpike_So · _trailMaterial=M_SpellTrail_So";
            });
        }

        // 오 — Crest 삭제, GroundWaveEffect → WaterWaveEffect(로브·물몸·재질)
        public static string WireO()
        {
            return Edit("SpellFx_O", root =>
            {
                var note = new StringBuilder();
                var crest = FindDeep(root.transform, "Crest");
                if (crest != null) { Object.DestroyImmediate(crest.gameObject); note.Append("Crest 삭제 · "); }
                var gw = root.GetComponentInChildren<GroundWaveEffect>(true);
                GameObject host = gw != null ? gw.gameObject
                    : (FindDeep(root.transform, "WaterWave") ?? FindDeep(root.transform, "GroundWave"))?.gameObject; // 재실행 시 중복 생성 방지
                if (host == null)
                {
                    host = new GameObject("WaterWave");
                    host.transform.SetParent(root.transform, false);
                    note.Append("WaterWave GO 신설 · ");
                }
                if (gw != null) { Object.DestroyImmediate(gw); note.Append("GroundWaveEffect 제거 · "); }
                host.name = "WaterWave";
                var fx = host.GetComponent<WaterWaveEffect>() ?? host.AddComponent<WaterWaveEffect>();
                var so = new SerializedObject(fx);
                SetRef(so, "_lobeMesh", Load<Mesh>(Gen + "O/WaveLobe_O.fbx"));
                SetRef(so, "_swellMesh", Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx"));
                SetRef(so, "_bodyMaterial", Load<Material>(Mats + "M_SpellBodyWater.mat"));
                SetRef(so, "_sprayMaterial", Load<Material>(Mats + "M_InkSpray.mat"));
                var scale = so.FindProperty("_lobeScale");
                if (scale != null) scale.floatValue = 2.0f; // 정규 높이 0.653 × 2.0 ≈ 1.3m(§8 파도 높이 ~1.5m 근사), 3로브 전면 ≈4m
                so.ApplyModifiedPropertiesWithoutUndo();
                note.Append("WaterWaveEffect: _lobeMesh=WaveLobe_O · _swellMesh=Sphere · _bodyMaterial=M_SpellBodyWater · _sprayMaterial=M_InkSpray · _lobeScale 2.0");
                var charge = FindDeep(root.transform, "Charge"); // Fly05 원본 Charge(백색 플레어·라이트 PS) — 물 룩을 덮는 드레싱, 오버라이드로 비활성
                if (charge != null && charge.gameObject.activeSelf) { charge.gameObject.SetActive(false); note.Append(" · Charge 비활성"); }
                return note.ToString();
            });
        }

        // 고 — 덩굴 메시·전용 재질·흙 재질·본 수·홀드
        // 고 — 마디 체인(예준 2026-09-05: 사방 발아·얽힘·지면 관통·감속·굵기/형태 다양·산발 성장).
        // 메시 3종은 굵기와 형태가 갈린다: A 굵은 옹이(go4 ×2.1) / B 중간(go4 ×1.45) / C 가는 결(go6 ×1.25)
        public static string WireGo()
        {
            return Edit("SpellFx_Go", root =>
            {
                var fx = root.GetComponentInChildren<ThornRiseEffect>(true);
                if (fx == null) return "ThornRiseEffect 없음";
                var so = new SerializedObject(fx);
                var arr = so.FindProperty("_vineMeshes");
                arr.arraySize = 3;
                arr.GetArrayElementAtIndex(0).objectReferenceValue = Load<Mesh>(Gen + "Go/RootSeg_Go_A.fbx");
                arr.GetArrayElementAtIndex(1).objectReferenceValue = Load<Mesh>(Gen + "Go/RootSeg_Go_B.fbx");
                arr.GetArrayElementAtIndex(2).objectReferenceValue = Load<Mesh>(Gen + "Go/RootSeg_Go_C.fbx");
                SetRef(so, "_material", Load<Material>(Mats + "M_SpellBodyThorn.mat"));
                SetRef(so, "_soilMaterial", Load<Material>(Mats + "M_SpellSoil.mat"));

                // 사방에서·더 많이·산발적으로
                so.FindProperty("_count").intValue = 18;                 // 방위 슬롯 = 360/18. 마디 예산이 18본을 정확히 채운다
                so.FindProperty("_startSpread").floatValue = 0.45f;
                SetV2(so, "_segRange", 3f, 5f);                          // 평균 4.0 → 72마디 ÷ 4.0 = 18본
                so.FindProperty("_maxSegments").intValue = 72;           // 폴리 하드 잠금: 72×210 + 36×48 = 16,848 < 17,000
                so.FindProperty("_runLength").floatValue = 3.43f;        // ×1.714 — 범위 확대(예준 2026-09-05)
                SetV2(so, "_ringRange", 0.62f, 0.88f);                   // 무차원 — 확대에서 불변
                so.FindProperty("_swirlStart").floatValue = 0.75f;       // 무차원 — 얽힘 구조 보존
                so.FindProperty("_swirlMax").floatValue = 0.75f;
                so.FindProperty("_spreadFraction").floatValue = 0.95f;   // ★ 연출 반경의 유일 손잡이(본은 안쪽으로 진행한다). 예준 2026-09-05 「더 크게」
                so.FindProperty("_placeJitter").floatValue = 0.26f;      // 미터 단위 → ×1.714

                // 위로 솟고 땅을 파고든다 — 길이 차원은 전부 같은 배수로 재척도해야
                // 「샘플/파장」 비가 보존된다(안 하면 지면 관통이 앨리어싱된다)
                SetV2(so, "_weaveAmp", 0.24f, 0.33f);                    // ×1.52(마디 두께와 같은 배율)
                SetV2(so, "_weaveLambda", 1.97f, 2.66f);                 // ×1.714
                SetV2(so, "_meanderLambda", 1.37f, 2.40f);               // ×1.714
                so.FindProperty("_weaveBias").floatValue = 0.35f;        // 무차원
                so.FindProperty("_rootSink").floatValue = 0.076f;        // ×1.52 (마디 스케일)
                so.FindProperty("_sinkDrop").floatValue = 0.23f;         // ×1.52

                // 천천히 — fastRate는 **초당 마디**라 마디가 1.52배 길어지면 월드 속도가 1.52배 빨라진다.
                // 절정 이후는 _slowFactor로 정확히 되사고, 절정 이전은 보정 수단이 없다(판정 고정).
                so.FindProperty("_slowFactor").floatValue = 4.4f;        // 2.9×1.52 — 월드 성장 속도 보존
                so.FindProperty("_climaxSegs").intValue = 2;
                so.FindProperty("_writheFreq").floatValue = 0.73f;       // 1.1÷1.52 — 진폭이 커진 만큼 주파수를 내려 월드 꿈틀 속도 보존
                so.FindProperty("_holdTime").floatValue = 1.1f;

                // 굵기 다양 — 본별 균일 스케일 지터(비균일 아님)
                SetV2(so, "_scaleJitter", 0.85f, 1.35f);
                so.FindProperty("_serpFreq").floatValue = 2f;   // 정수 필수(비정수면 관절 어긋남)
                so.FindProperty("_serpAmp").floatValue = 0.07f; // 曲은 배치가 가져갔다
                so.FindProperty("_curlAmp").floatValue = 0.03f;
                so.FindProperty("_leafCount").intValue = 6;     // 첨단 마디에만 붙는다

                // 급속 성장 4단계(예준 2026-09-05) — 얇게 시작 → 굵어짐 → 50%부터 풀·잔가지 → 최종 초록
                so.FindProperty("_girthMin").floatValue = 0.28f;      // 발아 지름 = 성숙의 28%
                so.FindProperty("_girthTip").floatValue = 0.78f;      // 체인 끝으로 가늘어짐
                so.FindProperty("_sproutStart").floatValue = 0.5f;    // 예준 「50% 정도 자라난 시점부터」
                so.FindProperty("_greenFrom").floatValue = 0.5f;
                so.FindProperty("_greenMax").floatValue = 0.85f;      // 줄기 녹화 상한(밑동은 _GreenFromZ가 목질로 남긴다)
                so.FindProperty("_sproutSites").intValue = 6;
                so.FindProperty("_grassPerSite").intValue = 4;        // 풀날 1tri
                so.FindProperty("_twigPerSite").intValue = 1;         // 잔가지 Y자 3tri — 「가지」의 판독 신호는 분기다
                SetV2(so, "_sproutZ", 0.12f, 0.86f);
                SetV2(so, "_grassLen", 0.30f, 0.52f);   // 풀 면적은 폴리가 아니라 크기로 산다(1tri 그대로)
                SetV2(so, "_twigLen", 0.26f, 0.44f);
                so.FindProperty("_grassWidth").floatValue = 0.055f;
                so.FindProperty("_sproutSegsPerRun").intValue = 2;   // 끝이 아니라 **가장 드러난** 2마디
                so.FindProperty("_twigWidth").floatValue = 0.018f;

                // 마디 메시 실측표 — Blender 실측(런 로그). 정규화가 z=0 단면 중심을 원점으로 보증하지 **않는다**
                var spine = so.FindProperty("_meshSpine");
                spine.arraySize = 3;
                spine.GetArrayElementAtIndex(0).vector4Value = new Vector4(0.0211f, 0.0182f, 0.0071f, 0.0343f); // A
                spine.GetArrayElementAtIndex(1).vector4Value = new Vector4(0.0146f, 0.0126f, 0.0049f, 0.0237f); // B
                spine.GetArrayElementAtIndex(2).vector4Value = new Vector4(-0.0003f, -0.0072f, 0.0044f, 0.0524f); // C
                var half = so.FindProperty("_meshHalf");
                half.arraySize = 3;
                half.GetArrayElementAtIndex(0).vector2Value = new Vector2(0.1294f, 0.1939f);
                half.GetArrayElementAtIndex(1).vector2Value = new Vector2(0.0893f, 0.1339f);
                half.GetArrayElementAtIndex(2).vector2Value = new Vector2(0.0619f, 0.0750f);

                so.ApplyModifiedPropertiesWithoutUndo();
                return "ThornRise(급속성장): 본 18 · 마디 3~5(상한 72) · spread 0.95 · 굵기 0.28→1.0 · 새싹 50%부터(부착점 6 · 풀 4 · 잔가지 1) · 녹화 0.85 · 척추/반폭 실측표 3종";
            });
        }

        private static void SetV2(SerializedObject so, string path, float x, float y)
        {
            var pr = so.FindProperty(path);
            if (pr != null) pr.vector2Value = new Vector2(x, y);
        }

        // 노 — Bottom06 FlameBurst 중첩 인스턴스 제거, 불혀 메시·InkFlame 재질
        public static string WireNo()
        {
            return Edit("SpellFx_No", root =>
            {
                var note = new StringBuilder();
                var burst = FindDeep(root.transform, "FlameBurst");
                if (burst != null)
                {
                    try { Object.DestroyImmediate(burst.gameObject); note.Append("FlameBurst 삭제 · "); }
                    catch (System.Exception e) { note.Append("FlameBurst 삭제 실패: ").Append(e.Message).Append(" · "); }
                }
                else note.Append("FlameBurst 없음 · ");
                var fx = root.GetComponentInChildren<FlameJetEffect>(true);
                if (fx == null) return note + "FlameJetEffect 없음";
                var so = new SerializedObject(fx);
                SetRef(so, "_tongueMesh", Load<Mesh>(Gen + "No/FlameTongue_No.fbx"));
                SetRef(so, "_flameMaterial", Load<Material>(Mats + "M_SpellFlame_No.mat"));
                so.ApplyModifiedPropertiesWithoutUndo();
                note.Append("FlameJet: _tongueMesh=FlameTongue_No · _flameMaterial=M_SpellFlame_No");
                return note.ToString();
            });
        }

        // 모 — GroundWave(+Bottom20 SandStorm 중첩) 제거 → SandStorm(SandStormEffect) 신설, Charge 비활성
        public static string WireMo()
        {
            return Edit("SpellFx_Mo", root =>
            {
                var note = new StringBuilder();
                var old = FindDeep(root.transform, "GroundWave");
                if (old != null)
                {
                    try { Object.DestroyImmediate(old.gameObject); note.Append("GroundWave(+SandStorm 중첩) 삭제 · "); }
                    catch (System.Exception e) { note.Append("GroundWave 삭제 실패: ").Append(e.Message).Append(" · "); }
                }
                var host = FindDeep(root.transform, "SandStorm")?.gameObject;
                if (host == null)
                {
                    host = new GameObject("SandStorm");
                    host.transform.SetParent(root.transform, false);
                    note.Append("SandStorm GO 신설 · ");
                }
                var fx = host.GetComponent<SandStormEffect>() ?? host.AddComponent<SandStormEffect>();
                var so = new SerializedObject(fx);
                SetRef(so, "_frontMesh", Load<Mesh>(Gen + "Mo/SandFront_Mo.fbx"));
                SetRef(so, "_frontMaterial", Load<Material>(Mats + "M_SpellBodySand.mat"));
                SetRef(so, "_dustMaterial", Load<Material>(Mats + "M_InkDust.mat"));
                SetRef(so, "_chunkMesh", Load<Mesh>(Gen + "Ma/Boulder_Ma.fbx"));
                SetRef(so, "_chunkMaterial", Load<Material>(Mats + "M_SpellBodySpike.mat"));
                so.ApplyModifiedPropertiesWithoutUndo();
                note.Append("SandStormEffect: _frontMesh=SandFront_Mo · _frontMaterial=M_SpellBodySand · _dustMaterial=M_InkDust · _chunkMesh=Boulder_Ma · _chunkMaterial=M_SpellBodySpike");
                var charge = FindDeep(root.transform, "Charge");
                if (charge != null && charge.gameObject.activeSelf) { charge.gameObject.SetActive(false); note.Append(" · Charge 비활성"); }
                return note.ToString();
            });
        }

        // ---- 공통 ----
        private static string Edit(string prefabName, System.Func<GameObject, string> body)
        {
            string path = Prefabs + prefabName + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) return $"[{prefabName}] 로드 실패";
            string note;
            try
            {
                note = body(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return $"[{prefabName}] {note}";
        }

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogWarning($"[FxReworkWiring] 에셋 없음: {path}");
            return asset;
        }

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning($"[FxReworkWiring] 필드 없음: {so.targetObject.GetType().Name}.{field}"); return; }
            p.objectReferenceValue = value;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
