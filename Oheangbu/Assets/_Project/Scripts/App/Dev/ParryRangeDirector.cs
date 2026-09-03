using System;
using System.Collections.Generic;
using System.Text;
using Oheangbu.BrushRender;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using UnityEngine;

namespace Oheangbu.App
{
    // [패링장 하네스 — SPEC-DEV-TEST-HUB §2·§5] 적 5속성을 한 번에 하나만 깨워 패링 표본을 뽑는다.
    // 규칙 무접촉 — CombatLoopWiring.ParryResolved(재방송)와 PlayerVitals.Damaged를 읽어 판정판·콘솔에 남길 뿐.
    // 정답표는 ElementRelations·CombatConfig에서 런타임 합성 — 관계·수치를 여기 적지 않는다(정본은 코드·SO).
    public sealed class ParryRangeDirector : MonoBehaviour
    {
        private enum Result { Success, Half, Fail, Block, Hit }

        private static readonly string[] ResultNames = { "성공", "반성공", "실패", "블록", "피격" };
        private static readonly Element[] Elements = { Element.Wood, Element.Fire, Element.Earth, Element.Metal, Element.Water };

        [SerializeField] private DevEnemyWakePedestal[] _pedestals = Array.Empty<DevEnemyWakePedestal>();
        [SerializeField] private CombatLoopWiring _wiring;
        [SerializeField] private PlayerVitals _playerVitals;
        [SerializeField] private CombatConfigSO _config;
        [SerializeField] private ElementPaletteSO _palette;
        [SerializeField] private DevInfoBoard _answerBoard;
        [SerializeField] private DevInfoBoard _outcomeBoard;
        [SerializeField, Min(1)] private int _historyLines = 4;

        private readonly Dictionary<Element, int[]> _tally = new Dictionary<Element, int[]>();
        private readonly List<string> _history = new List<string>();
        private readonly List<TextMesh> _enemyLabels = new List<TextMesh>();
        private readonly List<Material> _materials = new List<Material>();
        private DevEnemyWakePedestal _awake;
        private int _lastImpactFrame = -1;
        private int _eventCount;

        private void Start()
        {
            foreach (var element in Elements) _tally[element] = new int[ResultNames.Length];

            foreach (var pedestal in _pedestals)
            {
                if (pedestal == null || pedestal.Enemy == null) continue;
                var enemy = pedestal.Enemy;
                var label = DevLabel.Create(enemy.transform, Vector3.up * 1.5f, 0.045f, TextAnchor.LowerCenter, DevLabel.Paper);
                _enemyLabels.Add(label);
                // 바닥 속성색 고리 — 팔레트가 단일 출처(색=의미). 적의 렌더러는 EnemyController가 틴트하므로 건드리지 않는다
                if (_palette != null)
                {
                    var material = DevLabel.CreateUnlit(_palette.GetBaseColor(DevElement.Initial(enemy.RangedElement)));
                    _materials.Add(material);
                    var ring = DevLabel.CreateRing(enemy.transform, "ElementRing", 0.9f, 0.06f, material, -1.07f);
                    ring.useWorldSpace = false;
                }
            }
            RefreshEnemyLabels();

            if (_answerBoard != null) _answerBoard.SetText(ComposeAnswerBoard());
            RefreshOutcomeBoard();
            Debug.Log("[Parry] 패링장 준비 — 선택대 F로 적을 하나만 깨운다 · 판정은 판정판·콘솔 [Parry]");
        }

        private void OnEnable()
        {
            foreach (var pedestal in _pedestals)
            {
                if (pedestal != null) pedestal.Toggled += OnToggled;
            }
            if (_wiring != null) _wiring.ParryResolved += OnParryResolved;
            if (_playerVitals != null) _playerVitals.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            foreach (var pedestal in _pedestals)
            {
                if (pedestal != null) pedestal.Toggled -= OnToggled;
            }
            if (_wiring != null) _wiring.ParryResolved -= OnParryResolved;
            if (_playerVitals != null) _playerVitals.Damaged -= OnDamaged;
        }

        private void OnDestroy()
        {
            foreach (var material in _materials)
            {
                if (material != null) Destroy(material);
            }
        }

        private void Update()
        {
            foreach (var label in _enemyLabels) DevLabel.FaceCamera(label);
        }

        // 한 번에 하나 — 깨어난 적이 생기면 나머지는 재운다(표본의 공격 속성이 하나로 확정된다)
        private void OnToggled(DevEnemyWakePedestal pedestal, bool awake)
        {
            if (awake)
            {
                foreach (var other in _pedestals)
                {
                    if (other != null && other != pedestal && other.IsAwake) other.Sleep();
                }
                _awake = pedestal;
                Debug.Log($"[Parry] {DevElement.Name(pedestal.Enemy.RangedElement)} 적 깨움 — 나머지 잠듦");
            }
            else if (_awake == pedestal)
            {
                _awake = null;
            }
            RefreshEnemyLabels();
        }

        private void OnParryResolved(ParryOutcome outcome, Element guardElement, Vector3 impactPoint)
        {
            _lastImpactFrame = Time.frameCount;
            Result result;
            switch (outcome)
            {
                case ParryOutcome.Success: result = Result.Success; break;
                case ParryOutcome.Half: result = Result.Half; break;
                case ParryOutcome.Fail: result = Result.Fail; break;
                case ParryOutcome.Block: result = Result.Block; break;
                default: return;
            }
            Record(result, guardElement);
        }

        // 같은 프레임에 판정 이벤트가 없었던 피해 = 방어막 없이 맞았다(무속성 근접은 이 방에 없다 — 원거리 전용 설정)
        private void OnDamaged(float amount)
        {
            if (Time.frameCount == _lastImpactFrame) return;
            Record(Result.Hit, null);
        }

        private void Record(Result result, Element? guard)
        {
            var attacker = _awake != null && _awake.Enemy != null ? _awake.Enemy : null;
            string attack = attacker != null ? DevElement.Name(attacker.RangedElement) : "미상";
            if (attacker != null) _tally[attacker.RangedElement][(int)result]++;
            _eventCount++;
            string guardText = guard.HasValue ? $" vs {DevElement.GuardLetter(guard.Value)}({DevElement.Name(guard.Value)}) 방어막" : "";
            string line = $"#{_eventCount} {attack} 공격{guardText} → {ResultNames[(int)result]}";
            if (result == Result.Hit) line += "(방어막 없음)";
            _history.Add(line);
            while (_history.Count > _historyLines) _history.RemoveAt(0);
            Debug.Log("[Parry] " + line);
            RefreshOutcomeBoard();
        }

        private void RefreshEnemyLabels()
        {
            int i = 0;
            foreach (var pedestal in _pedestals)
            {
                if (pedestal == null || pedestal.Enemy == null) continue;
                if (i >= _enemyLabels.Count) break;
                var element = pedestal.Enemy.RangedElement;
                _enemyLabels[i].text = $"{DevElement.Name(element)}({DevElement.Initial(element)}) — {(pedestal.IsAwake ? "활동" : "잠듦")}";
                i++;
            }
        }

        private string ComposeAnswerBoard()
        {
            var sb = new StringBuilder();
            sb.Append("[패링 정답표 — 방어막(글자)이 공격을 이기면 성공(상극)]\n");
            foreach (var attack in Elements)
            {
                Element success = attack, fail = attack;
                var others = new List<string>();
                foreach (var guard in Elements)
                {
                    if (ElementRelations.Overcomes(guard, attack)) success = guard;
                    else if (ElementRelations.Generates(guard, attack)) fail = guard;
                    else others.Add(DevElement.GuardLetter(guard).ToString());
                }
                sb.Append(DevElement.Name(attack)).Append('(').Append(DevElement.Initial(attack)).Append(") 공격 ← ")
                    .Append(DevElement.GuardLetter(success)).Append('(').Append(DevElement.Name(success)).Append(") 성공 · ")
                    .Append(DevElement.GuardLetter(fail)).Append('(').Append(DevElement.Name(fail)).Append(") 실패 · ")
                    .Append(string.Join("·", others)).Append(" 반성공\n");
            }
            float window = _config != null ? _config.ParryWindow : 0f;
            float guardDuration = _config != null ? _config.GuardDuration : 0f;
            sb.Append($"완성 후 {window:0.##}s 안 임팩트=3단 판정 · {guardDuration:0.#}s까지=블록(경감만) · 방어막 없음=피격");
            return sb.ToString();
        }

        private void RefreshOutcomeBoard()
        {
            if (_outcomeBoard == null) return;
            var sb = new StringBuilder();
            sb.Append("[판정판]\n");
            foreach (var element in Elements)
            {
                var counts = _tally[element];
                sb.Append(DevElement.Name(element)).Append(": ");
                for (int i = 0; i < ResultNames.Length; i++)
                {
                    if (i > 0) sb.Append(" · ");
                    sb.Append(ResultNames[i]).Append(' ').Append(counts[i]);
                }
                sb.Append('\n');
            }
            sb.Append("최근:\n");
            if (_history.Count == 0) sb.Append(" (아직 없음)");
            foreach (var line in _history) sb.Append(' ').Append(line).Append('\n');
            _outcomeBoard.SetText(sb.ToString());
        }
    }
}
