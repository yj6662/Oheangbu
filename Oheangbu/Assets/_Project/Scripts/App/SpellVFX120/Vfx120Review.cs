using UnityEngine;
using UnityEngine.InputSystem;
using Oheangbu.Spellcraft;

namespace Oheangbu.App.SpellVFX120
{
    public sealed class Vfx120Review : MonoBehaviour
    {
        public Vfx120Catalog Catalog;
        public int Index;
        public bool AutoReplay = true;
        public bool DemonstrationCues = true;
        private Vfx120Effect _active;
        private float _replayAt;
        private float _playStart;
        private GameObject _fixtureRoot;
        private Font _font;
        public Vfx120Effect Active => _active;

        private void Start()
        {
            _font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 17);
            Play(Index);
        }
        public void Play(int index)
        {
            if (Catalog == null || Catalog.Entries.Length == 0) return;
            Index = (index + Catalog.Entries.Length) % Catalog.Entries.Length;
            if (_active != null) Destroy(_active.gameObject);
            DestroyFixture(_fixtureRoot); _fixtureRoot = null;
            var entry = Catalog.Entries[Index];
            var instance = Instantiate(entry.Prefab);
            _active = instance.GetComponent<Vfx120Effect>();
            if (_active == null) { Destroy(instance); return; }
            _active.PreviewControlled = true;
            _active.DemonstrationCues = DemonstrationCues;
            Transform primary = GameObject.Find("VFX Target")?.transform;
            if (DemonstrationCues)
            {
                PrepareDemonstrationTargets(entry.Glyph == "안", out primary, out var secondary,
                    out var split, out _fixtureRoot);
                _active.SetSecondaryTargets(secondary, split);
                _active.SetAreaPlan(CreateDemonstrationAreaPlan(entry.Profile));
            }
            _active.Begin(new Vector3(0, 1, 0), primary, new Vector3(0, 1, 4), Color.white);
            _playStart = Time.time;
            _replayAt = Time.time + _active.Life + .7f;
        }
        private void Update()
        {
            if (_active != null) _active.Sample(Time.time - _playStart);
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.rightArrowKey.wasPressedThisFrame) Play(Index + 1);
                if (keys.leftArrowKey.wasPressedThisFrame) Play(Index - 1);
                if (keys.spaceKey.wasPressedThisFrame) Play(Index);
            }
            if (AutoReplay && Time.time > _replayAt) Play(Index);
        }
        private void OnGUI()
        {
            if (Catalog == null || Catalog.Entries.Length == 0) return;
            Font previous = GUI.skin.font;
            try
            {
                if (_font != null) GUI.skin.font = _font;
                DrawControls();
            }
            finally { GUI.skin.font = previous; }
        }

        private void DrawControls()
        {
            var e = Catalog.Entries[Index];
            GUILayout.BeginArea(new Rect(18, 18, 430, 230), GUI.skin.box);
            GUILayout.Label($"{Index + 1:000} / {Catalog.Entries.Length}   {e.Glyph}  {e.Profile.Title}");
            GUILayout.Label(e.Profile.Assigned ? "배정 어휘 · " + (e.GameplayConnected ? "기존 게임 연결" : "VFX 자산 검수") : "미배정 슬롯 · 시각 후보");
            GUILayout.Label(e.Profile.Intent);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("← 이전")) Play(Index - 1);
            if (GUILayout.Button("다시 재생")) Play(Index);
            if (GUILayout.Button("다음 →")) Play(Index + 1);
            GUILayout.EndHorizontal();
            AutoReplay = GUILayout.Toggle(AutoReplay, "반복 재생");
            bool demonstrations = GUILayout.Toggle(DemonstrationCues, "검수용 명중·전이·해제 예시");
            if (demonstrations != DemonstrationCues) { DemonstrationCues = demonstrations; Play(Index); }
            if (DemonstrationCues) GUILayout.Label("표적과 이벤트는 연출 검수용입니다. 게임 규칙 연결을 증명하지 않습니다.");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            DestroyFixture(_fixtureRoot);
            if (_active != null) Destroy(_active.gameObject);
            if (_font != null) Destroy(_font);
        }

        // Shared by the isolated review and edit-mode capture. No game scene calls this.
        // Existing review targets are reused; temporary figures have no gameplay/collision components.
        public static void PrepareDemonstrationTargets(bool split, out Transform primary,
            out Transform secondary, out Transform[] splitTargets, out GameObject temporaryRoot)
        {
            temporaryRoot = new GameObject("VFX120 Demonstration Fixtures") { hideFlags = HideFlags.DontSave };
            primary = GameObject.Find("VFX Target")?.transform;
            secondary = GameObject.Find("VFX Secondary Target")?.transform;
            Material material = primary != null ? primary.GetComponentInChildren<Renderer>()?.sharedMaterial : null;
            if (primary == null) primary = MakeTarget(temporaryRoot.transform, "Primary", new Vector3(0, 0, 4), material);
            if (secondary == null) secondary = MakeTarget(temporaryRoot.transform, "Secondary", new Vector3(2.3f, 0, 5.1f), material);
            splitTargets = null;
            if (!split) return;
            splitTargets = new Transform[6];
            splitTargets[0] = secondary;
            for (int i = 1; i < splitTargets.Length; i++)
            {
                float x = new[] { 0f, -2.4f, -1.45f, -.5f, .5f, 1.45f }[i];
                var point = new Vector3(x, 0, 5.55f + (i % 2) * .65f);
                splitTargets[i] = MakeTarget(temporaryRoot.transform, "Split " + (i + 1), point, material);
            }
        }

        // Profile-sized illustration only. Combat creates the actual AreaImpactPlan elsewhere.
        // No EnemyVitals, hit/power calculation or gameplay event is created by this review.
        public static AreaImpactPlan CreateDemonstrationAreaPlan(Vfx120Profile profile)
        {
            if (profile == null) return null;
            var plan = new AreaImpactPlan { Direction = Vector3.forward, Delay = profile.Flight, Radius = profile.Size };
            switch (profile.Glyph)
            {
                case "고":
                    plan.Shape = AreaShape.Circle; plan.Point = new Vector3(0, 0, 4); return plan;
                case "노":
                    plan.Shape = AreaShape.Cone; plan.Point = new Vector3(0, 1, 0);
                    plan.Angle = 32; plan.Length = Mathf.Max(2.5f, profile.Size * 2); return plan;
                case "모": case "오":
                    plan.Shape = AreaShape.Path; plan.Point = new Vector3(0, 0, .8f);
                    plan.Length = 5; plan.Speed = 2.2f; return plan;
                case "소":
                    plan.Shape = AreaShape.Volley; plan.Point = new Vector3(0, 1, 4); plan.Speed = 12;
                    for (int i = 0; i < Mathf.Clamp(profile.Count, 1, 32); i++)
                        plan.Shots.Add(new PlannedHit { ImpactTime = Time.time + profile.Flight + i * .13f });
                    return plan;
                default: return null;
            }
        }

        private static Transform MakeTarget(Transform parent, string label, Vector3 point, Material material)
        {
            var root = new GameObject("Review-only " + label) { hideFlags = HideFlags.DontSave };
            root.transform.SetParent(parent, false); root.transform.position = point;
            var torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Neutral torso"; torso.transform.SetParent(root.transform, false);
            torso.transform.localPosition = new Vector3(0, .95f, 0);
            torso.transform.localScale = new Vector3(.4f, .55f, .3f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Neutral head"; head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 1.62f, 0); head.transform.localScale = Vector3.one * .29f;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
                if (material != null) renderer.sharedMaterial = material;
            foreach (var collider in root.GetComponentsInChildren<Collider>())
            {
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
            }
            return root.transform;
        }

        public static void DestroyFixture(GameObject root)
        {
            if (root == null) return;
            root.SetActive(false);
            if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
        }
    }
}
