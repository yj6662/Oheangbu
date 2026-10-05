using System;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Core.Domain;
using Oheangbu.Spellcraft;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    public enum FieldLiftState { None, Prepared, Rising, Holding, Descending, SafetyHold }

    // Demo TEST utility. The central CombatLoopWiring owns recognition and the one ink transaction.
    // PlayerMotor keeps input ownership; this service contributes only collision-resolved support motion.
    [DisallowMultipleComponent, DefaultExecutionOrder(100)]
    public sealed class FieldSpellService : MonoBehaviour
    {
        // #308 WP-00: the lift numbers are row data (lift.height / lift.rise / lift.descent / lift.hold in Rules308_WP00.csv).
        // These constants are what a cast without row data uses (a book with no imported table, the existing checks).
        public const float MaximumHeight = 2.4f;
        public const float RiseSpeed = 1.2f, DescentSpeed = .9f, HoldSeconds = 20f;
        public static FieldLiftSpec LegacyLift => new FieldLiftSpec(MaximumHeight, RiseSpeed, DescentSpeed, HoldSeconds);
        FieldLiftSpec lift = LegacyLift;
        public FieldLiftSpec Lift => lift;
        static readonly Vector2 DefaultDeckHalfSize = new Vector2(.42f, .35f);
        Vector2 DeckHalfSize = DefaultDeckHalfSize;
        float deckScale=1;
        CharacterController body;
        PlayerMotor motor;
        PlayerVitals vitals, subscribedVitals;
        Vfx120Profile visualProfile;
        Func<bool> unlocked, seated, paused;
        GameObject prepared, active;
        BoxCollider deck;
        Vfx120Effect visual;
        FieldSpellSafetyHold retired;
        Vector3 basePoint;
        float height, held, elapsed;
        float maximumHeight=MaximumHeight, riseSpeed=RiseSpeed, descentSpeed=DescentSpeed;
        GukLiftSite selectedSite;
        bool amplified;
        public string ActiveSiteId=>HasPlatform&&selectedSite!=null?selectedSite.Id:null;
        public float PlannedHeight=>maximumHeight;
        public Func<bool> CombatBlocked;
        RaycastHit[] groundHits;
        readonly Collider[] overlaps = new Collider[64];

        public FieldLiftState State { get; private set; }
        public string LastFailure { get; private set; }
        public bool IsUnlocked => isActiveAndEnabled && unlocked != null && unlocked();
        public bool HasPlatform => active != null || prepared != null || retired != null;
        public GameObject PlatformObject => active != null ? active : retired != null ? retired.gameObject : prepared;
        public float CurrentHeight => height;
        public Vector3 BasePoint => basePoint;
        public Func<Vector3,bool> DryPlacementAllowed;
        public Func<Vector3,Collider,bool> PermanentSupportAllowed;
        public bool PassengerSupported => deck != null && NearDeck(body, deck);
        public event Action<FieldLiftState> StateChanged;

        public void Configure(CharacterController playerBody, PlayerMotor playerMotor, PlayerVitals playerVitals,
            Vfx120Profile woodLiftProfile, Func<bool> hasDemoGuk, Func<bool> isSeated, Func<bool> isPaused)
        {
            ReleaseSafely(); Unbind();
            body = playerBody; motor = playerMotor; vitals = playerVitals; visualProfile = woodLiftProfile;
            unlocked = hasDemoGuk; seated = isSeated; paused = isPaused;
            if (isActiveAndEnabled) Bind();
        }

        void OnEnable() { Bind(); if (retired == null && State == FieldLiftState.SafetyHold) { height = 0; SetState(FieldLiftState.None); } }
        void OnDisable() { Unbind(); ReleaseSafely(); }
        void OnDestroy() { Unbind(); ReleaseSafely(); }
        void Bind()
        {
            if (subscribedVitals == vitals) return;
            Unbind(); subscribedVitals = vitals;
            if (subscribedVitals != null) subscribedVitals.Died += OnDeath;
        }
        void Unbind() { if (subscribedVitals != null) subscribedVitals.Died -= OnDeath; subscribedVitals = null; }
        void OnDeath() { RemoveAll(); }
        bool CanCast => IsUnlocked && !(CombatBlocked?.Invoke()??false) && body != null && body.enabled && body.gameObject.activeInHierarchy &&
            vitals != null && vitals.isActiveAndEnabled && vitals.Hp01 > 0f && !(seated?.Invoke() ?? false) &&
            !(paused?.Invoke() ?? false) && (motor == null || (!motor.IsSitting && !motor.IsDodging));

        // Builds an inactive validated candidate before the caller spends ink. Never replaces an occupied lift.
        public bool TryPrepare(SpellCast cast, out string failure) => TryPrepare(cast, null, out failure);

        // row = the table row of the cast (null = no row data: the constants above).
        public bool TryPrepare(SpellCast cast, SpellRow row, out string failure)
        {
            LastFailure = null;
            if (cast.Letter != '국' || cast.Kind != SpellKind.Field || cast.Element != Element.Wood)
                return Fail("국 승강 술식이 아닙니다.", out failure);
            if (!CanCast) return Fail("국을 얻은 뒤 보행 중 쓸 수 있다.", out failure);
            if (HasPlatform) return Fail("현재 발판을 내려오거나 해제한 뒤 다시 쓸 수 있다.", out failure);
            if (!ValidVisual()) return Fail("국 외형 연결이 준비되지 않았다.", out failure);
            lift = FieldLiftSpec.From(row, LegacyLift); // no platform exists at this point, so the numbers of a running lift never change
            if (!float.IsFinite(body.radius) || body.radius <= 0 || Mathf.Abs(body.transform.lossyScale.x - 1f) > .001f ||
                Mathf.Abs(body.transform.lossyScale.y - 1f) > .001f || Mathf.Abs(body.transform.lossyScale.z - 1f) > .001f ||
                Vector3.Dot(body.transform.up, Vector3.up) < .999f)
                return Fail("플레이어 충돌 크기와 발판 규격이 맞지 않다.", out failure);
            // Keep the authored minimum footprint; widen both visible branches and collision support for larger rigs.
            deckScale=Mathf.Max(1,(body.radius+.04f)/DefaultDeckHalfSize.y);
            DeckHalfSize=DefaultDeckHalfSize*deckScale;
            Vector3 feet = Feet(body);
            if(!GukLiftSite.Resolve(gameObject.scene,feet,out selectedSite))return Fail("지맥 발판의 연결이 맞지 않습니다.",out failure);
            amplified=selectedSite!=null;
            maximumHeight=amplified?selectedSite.Height:lift.Height;
            riseSpeed=selectedSite!=null?maximumHeight/selectedSite.RiseSeconds:lift.RiseSpeed;
            descentSpeed=selectedSite!=null?riseSpeed:lift.DescentSpeed;
            if(selectedSite!=null&&(!Ground(selectedSite.Upper.position,out var landing)||Mathf.Abs(landing.y-selectedSite.Upper.position.y)>.16f||!ClearCapsule(selectedSite.Upper.position)))
                return Fail("상단의 착지 공간이 막혀 있습니다.",out failure);
            if (!Ground(feet, out basePoint) || Mathf.Abs(feet.y - basePoint.y) > .16f || DryPlacementAllowed!=null&&!DryPlacementAllowed(basePoint))
                return Fail("안정된 지면에서 국을 써야 한다.", out failure);
            foreach (var offset in new[] { Vector3.right * DeckHalfSize.x, Vector3.left * DeckHalfSize.x,
                Vector3.forward * DeckHalfSize.y, Vector3.back * DeckHalfSize.y })
                if (!Ground(basePoint + offset, out var edge) || Mathf.Abs(edge.y - basePoint.y) > .1f || DryPlacementAllowed!=null&&!DryPlacementAllowed(edge))
                    return Fail("발판 아래 지면이 고르지 않거나 가장자리입니다.", out failure);
            // A finite set of overlapping capsule samples covers the complete vertical swept volume.
            int samples=Mathf.CeilToInt(maximumHeight/.1f);
            for (int i = 0; i <= samples; i++)
                if (!ClearCapsule(feet + Vector3.up * (maximumHeight * i / samples)))
                    return Fail("승강할 머리 위 공간이나 옆 공간이 막혀 있다.", out failure);
            try
            {
                prepared = new GameObject("Demo_GukLift"); SceneManager.MoveGameObjectToScene(prepared, body.gameObject.scene); prepared.SetActive(false);
                var support = new GameObject("GukSupport"); support.transform.SetParent(prepared.transform, false);
                support.AddComponent<Oheangbu.App.World.WorldTemporarySupport>();
                deck = support.AddComponent<BoxCollider>(); deck.size = new Vector3(DeckHalfSize.x * 2f, .08f, DeckHalfSize.y * 2f);
                deck.transform.position = basePoint - Vector3.up * .04f;
                var effect = new GameObject("GukApprovedWoodLift"); effect.SetActive(false); SceneManager.MoveGameObjectToScene(effect, body.gameObject.scene);
                visual = effect.AddComponent<Vfx120Effect>(); visual.Profile = visualProfile;
                visual.PreviewControlled = true; visual.DemonstrationCues = false; // External service clock, never demonstration movement.
                visual.SetGuardClock(60f, 0f); visual.Begin(basePoint + Vector3.up, null, basePoint + Vector3.forward, Color.white);
                effect.transform.SetParent(prepared.transform, true); effect.SetActive(true);
                if (!visual.ConfigureWoodLift(basePoint, maximumHeight, selectedSite!=null)) throw new InvalidOperationException("Existing WoodLift rejected its authoritative plan.");
                visual.WoodLiftInstance.transform.localScale=new Vector3(deckScale,1,deckScale);
                foreach (var collider in effect.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                height = held = elapsed = 0f; visual.SetWoodLiftHeight(0); visual.Sample(0); SetState(FieldLiftState.Prepared);
                failure = null; return true;
            }
            catch (Exception e)
            {
                if (visual != null && (prepared == null || !visual.transform.IsChildOf(prepared.transform))) Remove(visual.gameObject);
                CancelPrepared(); return Fail("국 배치 준비 실패: " + e.Message, out failure);
            }
        }

        public bool CommitPrepared()
        {
            if (prepared == null || !CanCast) { CancelPrepared(); return false; }
            // The prepare/commit pair is synchronous in CombatLoopWiring; still recheck the player and node.
            if(Vector3.Distance(Feet(body),basePoint)>.2f||amplified&&(selectedSite==null||!selectedSite.Valid||!selectedSite.Contains(Feet(body)))){CancelPrepared();return false;}
            if(selectedSite!=null&&(!Ground(selectedSite.Upper.position,out var landing)||Mathf.Abs(landing.y-selectedSite.Upper.position.y)>.16f||!ClearCapsule(selectedSite.Upper.position))){CancelPrepared();return false;}
            for(int i=0,n=Mathf.CeilToInt(maximumHeight/.1f);i<=n;i++)if(!ClearCapsule(Feet(body)+Vector3.up*(maximumHeight*i/n))){CancelPrepared();return false;}
            active = prepared; prepared = null; active.SetActive(true); Physics.SyncTransforms(); SetState(FieldLiftState.Rising); return true;
        }

        public void CancelPrepared()
        {
            if (prepared == null) return;
            Remove(prepared); prepared = null; deck = null; visual = null; height = held = elapsed = 0f; SetState(FieldLiftState.None);
        }

        // Gameplay/interaction can request descent; nobody removes support from under a living passenger.
        public void RequestRelease()
        {
            CancelPrepared();
            if (active == null) return;
            if (!NeedsSupport(body, deck)) { RemoveActive(); return; }
            SetState(FieldLiftState.Descending);
        }

        void Update() { Tick(Time.deltaTime); }
        public void Tick(float scaledDelta)
        {
            if (!isActiveAndEnabled) return;
            if (active == null)
            { if (retired == null && State == FieldLiftState.SafetyHold) { height = 0; SetState(FieldLiftState.None); } return; }
            if (body == null || vitals == null || vitals.Hp01 <= 0f || !body.gameObject.activeInHierarchy ||
                body.gameObject.scene != active.scene || !body.enabled || (seated?.Invoke() ?? false)) { RemoveAll(); return; }
            if (!IsUnlocked || (CombatBlocked?.Invoke()??false) || amplified&&selectedSite==null) RequestRelease();
            if (active == null) return;
            if ((paused?.Invoke() ?? false) || !float.IsFinite(scaledDelta) || scaledDelta <= 0f) return;
            // Do not turn a late frame into a large teleport. Collision movement is always <= 4 cm per substep.
            float remaining = Mathf.Min(scaledDelta, 1f);
            while (remaining > .00001f && active != null)
            {
                float step = Mathf.Min(remaining, Mathf.Min(1f / 30f,.04f/Mathf.Max(riseSpeed,descentSpeed))); remaining -= step;
                Step(step);
            }
        }

        void Step(float dt)
        {
            bool riding = NearDeck(body, deck), retain = NeedsSupport(body, deck);
            if (!retain) { RemoveActive(); return; }
            if (!riding)
            {
                if (Ground(Feet(body), out var otherFloor) && Mathf.Abs(Feet(body).y - otherFloor.y) <= .16f)
                { RemoveActive(); return; } // A ledge/new obstacle already supports the passenger.
                SetState(FieldLiftState.Holding); SampleVisual(); return; // Jumping above support: retain until landed/off.
            }
            elapsed += dt;
            if (State == FieldLiftState.Holding)
            {
                held += dt; if (held >= lift.HoldSeconds) SetState(FieldLiftState.Descending);
            }
            if (State == FieldLiftState.Rising)
            {
                float requested = Mathf.Min(riseSpeed * dt, maximumHeight - height);
                Vector3 before = body.transform.position;
                var flags = body.Move(Vector3.up * requested);
                float moved = Mathf.Clamp(body.transform.position.y - before.y, 0f, requested);
                height += moved; PositionDeck();
                if ((flags & CollisionFlags.Above) != 0 || moved + .003f < requested)
                { LastFailure = "머리 위 장애물로 승강을 멈추고 안전하게 내려갑니다."; SetState(FieldLiftState.Descending); }
                else if (height >= maximumHeight - .001f) { height = maximumHeight; PositionDeck(); SetState(FieldLiftState.Holding); }
            }
            else if (State == FieldLiftState.Descending)
            {
                float drop = Mathf.Min(descentSpeed * dt, height);
                // Lower the support first so CharacterController can follow it down, respecting any new real floor.
                height -= drop; PositionDeck(); body.Move(Vector3.down * drop);
                if (height <= .001f) { height = 0; PositionDeck(); RemoveActive(); return; }
            }
            SampleVisual();
        }

        void PositionDeck() { if (deck != null) { deck.transform.position = basePoint + Vector3.up * (height - .04f); Physics.SyncTransforms(); } }
        void SampleVisual()
        {
            if (visual == null) return;
            visual.SetWoodLiftHeight(height);
            // An occupied safety hold may last longer than the cosmetic effect's nominal lifetime.
            visual.Sample(Mathf.Min(elapsed, visual.Life - 1f));
        }

        // Called on disable/configuration/scene ownership changes. If the live player still needs this
        // support, transfer just the stationary platform to a tiny self-cleaning safety holder.
        // It has no input, timer, movement or cost path and disappears immediately after safe departure.
        public void ReleaseSafely()
        {
            CancelPrepared();
            if (active == null) return;
            if (vitals != null && vitals.Hp01 > 0f && body != null && body.enabled && NeedsSupport(body, deck))
            {
                retired = active.AddComponent<FieldSpellSafetyHold>(); retired.Configure(body, vitals, deck);
                active = null; deck = null; visual = null; SetState(FieldLiftState.SafetyHold);
            }
            else RemoveActive();
        }

        void RemoveActive()
        { Remove(active); active = null; deck = null; visual = null; height = held = elapsed = 0f; SetState(FieldLiftState.None); }
        void RemoveAll()
        { CancelPrepared(); RemoveActive(); if (retired != null) Remove(retired.gameObject); retired = null; }
        bool Fail(string error, out string failure) { failure = LastFailure = error; return false; }
        void SetState(FieldLiftState state) { if (State == state) return; State = state; StateChanged?.Invoke(state); }
        bool ValidVisual() => Vfx120Effect.IsWoodLift(visualProfile) && visualProfile.AccentMesh != null && visualProfile.BodyMaterial != null &&
            visualProfile.InkMaterial != null && visualProfile.PatternMaterial != null && visualProfile.GuardianMeshes != null &&
            visualProfile.GuardianMeshes.Length > 0 && visualProfile.GuardianMeshes[0] != null;

        bool Ground(Vector3 point, out Vector3 ground)
        {
            ground = default; float nearest = float.PositiveInfinity; bool found = false;
            int count = ScenePhysicsQuery.RaycastAll(body.gameObject.scene, point + Vector3.up * .25f, Vector3.down, .6f, ~0, ref groundHits);
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (PermanentSupportAllowed!=null&&!PermanentSupportAllowed(hit.point,hit.collider))continue;
                if (Ignore(hit.collider) || hit.normal.y < Mathf.Cos(20f * Mathf.Deg2Rad) || hit.distance >= nearest) continue;
                nearest = hit.distance; ground = hit.point; found = true;
            }
            return found;
        }
        bool ClearCapsule(Vector3 feet)
        {
            var physics = body.gameObject.scene.GetPhysicsScene();
            float radius = body.radius + .025f;
            Vector3 low = feet + Vector3.up * (radius + .04f), high = feet + Vector3.up * (Mathf.Max(radius, body.height - radius) + .04f);
            int count = physics.OverlapCapsule(low, high, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false; // Saturation fails closed rather than omitting a blocker.
            for (int i = 0; i < count; i++) if (!Ignore(overlaps[i])) return false;
            return true;
        }
        bool Ignore(Collider collider) => collider == null || collider.transform.IsChildOf(body.transform) ||
            (active != null && collider.transform.IsChildOf(active.transform)) || (prepared != null && collider.transform.IsChildOf(prepared.transform));
        internal static Vector3 Feet(CharacterController controller) => controller.transform.TransformPoint(controller.center) - Vector3.up * controller.height * .5f;
        internal static bool NearDeck(CharacterController controller, BoxCollider support)
        {
            if (!NeedsSupport(controller, support)) return false;
            float gap = Feet(controller).y - support.bounds.max.y;
            return gap >= -.12f && gap <= .18f;
        }
        internal static bool NeedsSupport(CharacterController controller, BoxCollider support)
        {
            if (controller == null || !controller.enabled || support == null || !support.enabled) return false;
            Vector3 feet = Feet(controller); Bounds bounds = support.bounds;
            return Mathf.Abs(feet.x - bounds.center.x) <= bounds.extents.x + controller.radius &&
                Mathf.Abs(feet.z - bounds.center.z) <= bounds.extents.z + controller.radius &&
                feet.y >= bounds.max.y - .15f && feet.y <= bounds.max.y + 3f;
        }
        internal static void Remove(UnityEngine.Object obj)
        { if (obj == null) return; if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }

    // A disabled/unloaded service cannot run descent. Retaining stationary support avoids a forced fall.
    // The holder lives in the same scene as the platform/player and owns no persistent/global objects.
    public sealed class FieldSpellSafetyHold : MonoBehaviour
    {
        CharacterController body; PlayerVitals vitals; BoxCollider support;
        public void Configure(CharacterController passenger, PlayerVitals health, BoxCollider deck)
        { body = passenger; vitals = health; support = deck; }
        void LateUpdate()
        {
            if (body == null || vitals == null || vitals.Hp01 <= 0f || !body.gameObject.activeInHierarchy ||
                body.gameObject.scene != gameObject.scene || !FieldSpellService.NeedsSupport(body, support))
                FieldSpellService.Remove(gameObject);
        }
    }
}
