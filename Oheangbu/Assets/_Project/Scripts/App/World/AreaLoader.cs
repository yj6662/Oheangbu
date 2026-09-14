using System.Threading;
using Cysharp.Threading.Tasks;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-MAP §4 층 11] 구역 로더 — FarSet 상주 씬에 1개. RealmSheetSO에서 areaId로 AreaSheetSO를 찾아
    // scenePath를 Additive 로드(UniTask) → 활성 씬 지정 → 리그를 pois[MineExit] 위치·yaw로 놓는다(진입 행: 허브 포탈 → FarSet → LoadAreaAsync).
    // 에디터 플레이 = LoadSceneAsyncInPlayMode(Build Settings 무관 — DevSceneFlow 규약) / 빌드 = SceneManager(등재 전제).
    // 싱글턴 0 · 전역 정적 상태 0: 로드된 씬 경로는 인스턴스 필드. 리셋 훅 0(A8).
    public sealed class AreaLoader : MonoBehaviour
    {
        // 스폰 POI ID(§5-1 폐광 출구 E0 — 층 11 진입 행 「리그를 pois[MineExit] yaw 지정」).
        public const string SpawnPoiId = "MineExit";
        // 리그 루트 이름(DevSceneKit.InstantiateRig = PlayerRig.prefab 인스턴스). 직렬화 참조가 비면 이 이름으로 찾는다.
        public const string RigRootName = "PlayerRig";

        [Tooltip("강토 시트 — areas[]에서 areaId로 검색. 플레이 시 RealmLifetimeScope 주입이 같은 에셋으로 덮어쓸 수 있다")]
        [SerializeField] private RealmSheetSO _realmSheet;
        [Tooltip("플레이어 리그 루트(비우면 GameObject.Find(\"PlayerRig\"))")]
        [SerializeField] private GameObject _playerRig;

        private string _loadedScenePath;

        public RealmSheetSO RealmSheet => _realmSheet;
        public string LoadedScenePath => _loadedScenePath;
        public bool IsAreaLoaded => !string.IsNullOrEmpty(_loadedScenePath);

        [Inject]
        public void Construct(RealmSheetSO realmSheet)
        {
            if (realmSheet != null) _realmSheet = realmSheet;
        }

        // 구역 Additive 로드 → 활성화 → 리그 배치. 실패는 예외가 아니라 로그 + false(시트 오류는 빌더/감사가 FAIL 문자열로 잡는다).
        public UniTask<bool> LoadAreaAsync(string areaId) => LoadAreaAsync(areaId,SpawnPoiId);
        public async UniTask<bool> LoadAreaAsync(string areaId,string spawnPoiId)
        {
            var sheet = FindSheet(areaId);
            if (sheet == null) return false;
            if (sheet.FindPoi(spawnPoiId) == null)
            {
                Debug.LogError($"[World] AreaSheet '{areaId}'에 진입 POI '{spawnPoiId}' 없음", sheet);
                return false;
            }
            if (string.IsNullOrEmpty(sheet.ScenePath))
            {
                Debug.LogError($"[World] AreaSheet '{areaId}' scenePath 공란", sheet);
                return false;
            }
            if (IsAreaLoaded)
            {
                await UnloadAreaAsync();
            }

            var token = this.GetCancellationTokenOnDestroy();
            var op = BeginLoad(sheet.ScenePath);
            if (op == null)
            {
                Debug.LogError($"[World] 씬 로드 시작 실패: {sheet.ScenePath}", sheet);
                return false;
            }
            await op.ToUniTask(cancellationToken: token);

            var scene = SceneManager.GetSceneByPath(sheet.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError($"[World] 로드 후 씬 무효: {sheet.ScenePath}", sheet);
                return false;
            }
            SceneManager.SetActiveScene(scene);
            _loadedScenePath = sheet.ScenePath;

            PlaceRigAtSpawn(sheet,spawnPoiId);
            return true;
        }

        public async UniTask UnloadAreaAsync()
        {
            if (!IsAreaLoaded) return;
            var scene = SceneManager.GetSceneByPath(_loadedScenePath);
            _loadedScenePath = null;
            if (!scene.IsValid() || !scene.isLoaded) return;
            var op = SceneManager.UnloadSceneAsync(scene);
            if (op == null) return;
            await op.ToUniTask(cancellationToken: this.GetCancellationTokenOnDestroy());
        }

        private AreaSheetSO FindSheet(string areaId)
        {
            if (_realmSheet == null)
            {
                Debug.LogError("[World] AreaLoader: RealmSheetSO 미지정", this);
                return null;
            }
            var sheet = _realmSheet.FindArea(new AreaId(areaId));
            if (sheet == null)
            {
                Debug.LogError($"[World] RealmSheet '{_realmSheet.name}'에 areaId '{areaId}' 없음", _realmSheet);
            }
            return sheet;
        }

        private static AsyncOperation BeginLoad(string scenePath)
        {
#if UNITY_EDITOR
            return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
#else
            return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
#endif
        }

        // 리그를 pois[MineExit]에 놓는다. yaw = 북 0° 시계 = Unity Y 회전과 동일. CharacterController는 활성 상태에서 transform 덮어쓰기를
        // 무시할 수 있어 이동 동안만 끈다(콜라이더 재빌드 0 — 컴포넌트 토글뿐).
        private void PlaceRigAtSpawn(AreaSheetSO sheet,string spawnPoiId)
        {
            var poi = sheet.FindPoi(spawnPoiId);
            if (poi == null)
            {
                Debug.LogError($"[World] AreaSheet '{sheet.AreaIdString}'에 POI '{spawnPoiId}' 없음 — 리그 미배치", sheet);
                return;
            }
            var rig = _playerRig != null ? _playerRig : GameObject.Find(RigRootName);
            if (rig == null)
            {
                Debug.LogError($"[World] 리그 '{RigRootName}' 없음 — FarSet 씬에 리그 1벌(DevSceneKit.InstantiateRig) 전제", this);
                return;
            }

            var controller = rig.GetComponentInChildren<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null) controller.enabled = false;
            // The rig may contain an authored non-zero player child offset.
            // Place the actual motor anchor, otherwise the old offset leaks into arrival.
            var anchor=controller!=null?controller.transform:rig.transform;
            anchor.SetPositionAndRotation(poi.position, Quaternion.Euler(0f, poi.yaw, 0f));
            anchor.GetComponent<Oheangbu.Combat.PlayerMotor>()?.ResetMotion();
            if (controller != null) controller.enabled = wasEnabled;
        }
    }
}
