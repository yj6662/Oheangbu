using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-REALM-STREAM-308] One per play scene. Looks at where the player stands and keeps the realm skin scenes of the sheet
    // loaded or let go (additive, hysteresis from the sheet). No singleton, no static state: everything is on this instance.
    // What must be there before the session starts (the realm under the spawn point) is NOT this component's job:
    //   - lobby flow: PlaytestUiRoot.LoadWithRegionScreen brings those scenes in before the play scene (WorldLoadingProfile270.RealmStream);
    //   - editor Play straight from the play scene: the editor hook RealmStream308 opens them before Play starts.
    // Scenes that are already loaded when this wakes are adopted.
    // A teleport onto a floor of a realm that is not in yet (escort restore, inn return, dev routes) lands on the ground below it: the
    // arrival is remembered and the player is put back on it once the realm is in (sheet ReseatRadius / ReseatDrop).
    public sealed class RealmStreamLoader308 : MonoBehaviour
    {
        public enum State { Out, Loading, In, Leaving }

        public RealmStreamSheet308 Sheet;
        [Tooltip("the session of this scene: its player and its checkpoint are followed. Empty = Target, else the main camera")]
        public WorldMacroPlaytestSession Session;
        [Tooltip("followed when there is no session")]
        public Transform Target;
        [Tooltip("far band (low meshes that stand in for a realm while its scene is out): realm ids and their roots in this scene, same order. Written by RealmStream308 far:build")]
        public string[] FarRealms = Array.Empty<string>();
        public GameObject[] FarRoots = Array.Empty<GameObject>();

        State[] states = Array.Empty<State>();
        float[] distances = Array.Empty<float>();
        readonly List<int> wanted = new List<int>();
        CancellationTokenSource life;
        bool purgeOwed; float lastPurge = float.NegativeInfinity;
        Camera fallbackCamera;
        // the longest frame while a scene comes in or goes out (AC-P7), newest last
        public readonly struct Passage
        {
            public readonly string Realm; public readonly bool In; public readonly float Seconds, WorstFrameMs;
            public Passage(string realm, bool @in, float seconds, float worstFrameMs) { Realm = realm; In = @in; Seconds = seconds; WorstFrameMs = worstFrameMs; }
        }
        const int PassagesKept = 16;
        readonly List<Passage> passages = new List<Passage>();
        bool timing; float timingWorst;
        bool hasLast, arrivalPending; Vector3 lastPosition, arrival; float arrivalAt;

        /// <summary>True once every realm the first look asked for is in.</summary>
        public bool InitialReady { get; private set; }
        public int Loads { get; private set; }
        public int Unloads { get; private set; }
        public int Purges { get; private set; }
        public string LastError { get; private set; }
        public IReadOnlyList<Passage> Passages => passages;
        public int Reseats { get; private set; }

        void OnEnable()
        {
            if (Sheet == null) { LastError = "no sheet"; return; }
            states = new State[Sheet.Realms.Length]; distances = new float[Sheet.Realms.Length];
            for (int i = 0; i < states.Length; i++) states[i] = Usable(i) && RealmStreamSheet308.IsLoaded(Sheet.Realms[i]) ? State.In : State.Out;
            SyncFar();
            life = new CancellationTokenSource();
            Follow(life.Token).Forget();
        }

        void OnDisable()
        {
            if (life != null) { life.Cancel(); life.Dispose(); life = null; }
        }

        void Update()
        {
            if (timing) timingWorst = Mathf.Max(timingWorst, Time.unscaledDeltaTime);
            if (Sheet == null || Session == null || !TryPosition(out var position)) { hasLast = false; return; }
            if (hasLast && (position - lastPosition).sqrMagnitude > Sheet.TeleportJump * Sheet.TeleportJump && !AllIn(position))
            {
                arrival = position; arrivalAt = Time.unscaledTime; arrivalPending = true;
            }
            lastPosition = position; hasLast = true;
        }

        /// <summary>True when every realm the point needs is in.</summary>
        bool AllIn(Vector3 position)
        {
            for (int i = 0; i < states.Length; i++)
                if (Usable(i) && states[i] != State.In && RealmStreamSheet308.Distance(Sheet.Realms[i], position) <= Sheet.LoadDistance) return false;
            return true;
        }

        void Reseat()
        {
            if (!arrivalPending) return;
            if (Time.unscaledTime - arrivalAt > Sheet.ReseatSeconds || !TryPosition(out var position)) { arrivalPending = false; return; }
            if (!AllIn(arrival)) return;
            arrivalPending = false;
            var flat = new Vector2(position.x - arrival.x, position.z - arrival.z);
            if (Session.Walker.Seated || flat.magnitude > Sheet.ReseatRadius || arrival.y - position.y <= Sheet.ReseatDrop) return;
            Session.Teleport(arrival, Session.Walker.Body.transform.eulerAngles.y);
            lastPosition = arrival; Reseats++;
        }

        void BeginPassage() { timing = true; timingWorst = 0; }

        void EndPassage(string realm, bool @in, float started)
        {
            timing = false;
            if (passages.Count >= PassagesKept) passages.RemoveAt(0);
            var passage = new Passage(realm, @in, Time.unscaledTime - started, timingWorst * 1000);
            passages.Add(passage);
            // one line per scene coming in or going out: the only way to read AC-P7 from a player log
            Debug.Log("[RealmStream308] passage " + realm + (@in ? " in " : " out ") + passage.Seconds.ToString("F2", CultureInfo.InvariantCulture) + " s | longest frame "
                + passage.WorstFrameMs.ToString("F1", CultureInfo.InvariantCulture) + " ms");
        }

        bool Usable(int i) => Sheet.Realms[i] != null && !string.IsNullOrEmpty(Sheet.Realms[i].ScenePath) && Sheet.Realms[i].Units > 0;

        bool TryPosition(out Vector3 position)
        {
            position = default;
            if (Session != null)
            {
                // The session teleports the player to the saved place in its Start: before that the body stands at its authored spot.
                if (!Session.InitializationComplete || Session.Walker == null || Session.Walker.Body == null) return false;
                position = Session.Walker.Body.transform.position; return true;
            }
            if (Target != null) { position = Target.position; return true; }
            if (fallbackCamera == null) fallbackCamera = Camera.main;
            if (fallbackCamera == null) return false;
            position = fallbackCamera.transform.position; return true;
        }

        bool TryAnchor(out Vector3 anchor)
        {
            anchor = default;
            if (Session == null || Session.Progress == null || Session.Progress.ledger == null) return false;
            if (!WorldMacroCheckpointRules.TryResolve(Session.Content, Session.Progress, Session.Progress.ledger.checkpoint, out var checkpoint)) return false;
            anchor = checkpoint.Feet; return true;
        }

        async UniTaskVoid Follow(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (TryPosition(out var position)) await Step(position, token);
                    await UniTask.Delay(TimeSpan.FromSeconds(Mathf.Max(.05f, Sheet.TickSeconds)), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { LastError = exception.Message; Debug.LogException(exception, this); }
        }

        async UniTask Step(Vector3 position, CancellationToken token)
        {
            bool anchored = TryAnchor(out var anchor);
            wanted.Clear();
            for (int i = 0; i < states.Length; i++)
            {
                if (!Usable(i)) continue;
                var realm = Sheet.Realms[i];
                float d = distances[i] = RealmStreamSheet308.Distance(realm, position);
                bool keep = d <= Sheet.LoadDistance || (states[i] != State.Out && d <= Mathf.Max(Sheet.UnloadDistance, Sheet.LoadDistance));
                if (!keep && anchored && RealmStreamSheet308.Distance(realm, anchor) <= Sheet.AnchorDistance) keep = true;
                if (keep) wanted.Add(i);
            }
            // nearest first, one at a time: two large scenes integrating in the same frames would stall longer
            wanted.Sort((a, b) => distances[a].CompareTo(distances[b]));
            foreach (int i in wanted)
            {
                if (states[i] != State.Out) continue;
                var realm = Sheet.Realms[i];
                if (RealmStreamSheet308.IsLoaded(realm)) { states[i] = State.In; continue; }
                var load = RealmStreamSheet308.BeginLoad(realm);
                if (load == null) { LastError = "load did not start: " + realm.Id; continue; }
                states[i] = State.Loading;
                float started = Time.unscaledTime; BeginPassage();
                var priority = Application.backgroundLoadingPriority; Application.backgroundLoadingPriority = Sheet.StreamPriority;
                try { await load.ToUniTask(cancellationToken: token); }
                finally { Application.backgroundLoadingPriority = priority; }
                states[i] = RealmStreamSheet308.IsLoaded(realm) ? State.In : State.Out; Loads++;
                SyncFar();   // the scene is in: its far band goes in the same frame
                await UniTask.Yield(PlayerLoopTiming.Update, token);   // the frame that brought the scene in is counted too
                EndPassage(realm.Id, true, started);
            }
            InitialReady = true;
            Reseat();
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] != State.In || wanted.Contains(i)) continue;
                var scene = SceneManager.GetSceneByPath(Sheet.Realms[i].ScenePath);
                if (!scene.IsValid() || !scene.isLoaded) { states[i] = State.Out; continue; }
                var unload = SceneManager.UnloadSceneAsync(scene);
                if (unload == null) continue;
                states[i] = State.Leaving;
                float started = Time.unscaledTime; BeginPassage();
                await unload.ToUniTask(cancellationToken: token);
                states[i] = State.Out; Unloads++; purgeOwed = true;
                SyncFar();
                await UniTask.Yield(PlayerLoopTiming.Update, token);
                EndPassage(Sheet.Realms[i].Id, false, started);
            }
            if (purgeOwed && Time.unscaledTime - lastPurge >= Sheet.PurgeSeconds)
            {
                purgeOwed = false; lastPurge = Time.unscaledTime; Purges++;
                await Resources.UnloadUnusedAssets().ToUniTask(cancellationToken: token);
            }
        }

        /// <summary>A realm's far band is shown exactly while its scene is not in.</summary>
        void SyncFar()
        {
            if (Sheet == null || FarRealms == null || FarRoots == null) return;
            for (int k = 0; k < FarRealms.Length && k < FarRoots.Length; k++)
            {
                if (FarRoots[k] == null) continue;
                bool present = false;
                for (int i = 0; i < states.Length; i++) if (Sheet.Realms[i] != null && Sheet.Realms[i].Id == FarRealms[k]) present = states[i] == State.In || states[i] == State.Leaving;
                if (FarRoots[k].activeSelf == present) FarRoots[k].SetActive(!present);
            }
        }

        /// <summary>One line per realm for the editor tool and logs.</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            bool has = TryPosition(out var p);
            sb.Append("loader ").Append(isActiveAndEnabled ? "on" : "off").Append(" | initial ready ").Append(InitialReady)
              .Append(" | loads ").Append(Loads).Append(" unloads ").Append(Unloads).Append(" purges ").Append(Purges).Append(" reseats ").Append(Reseats)
              .Append(" | player ").Append(has ? F(p.x) + "," + F(p.y) + "," + F(p.z) : "unknown");
            if (Sheet != null) sb.Append(" | load ").Append(F(Sheet.LoadDistance)).Append(" unload ").Append(F(Sheet.UnloadDistance)).Append(" anchor ").Append(F(Sheet.AnchorDistance));
            if (!string.IsNullOrEmpty(LastError)) sb.Append(" | error ").Append(LastError);
            sb.Append('\n');
            if (Sheet == null) return sb.ToString();
            for (int i = 0; i < Sheet.Realms.Length; i++)
            {
                var realm = Sheet.Realms[i]; if (realm == null) continue;
                float d = has ? RealmStreamSheet308.Distance(realm, p) : float.NaN;
                sb.Append("  ").Append((realm.Id ?? "?").PadRight(12)).Append(i < states.Length ? states[i].ToString().PadRight(8) : "?       ")
                  .Append(" scene loaded ").Append(RealmStreamSheet308.IsLoaded(realm) ? "yes" : "no ")
                  .Append(" far band ").Append(FarText(realm.Id))
                  .Append(" distance ").Append(float.IsNaN(d) ? "?" : F(d)).Append(" m | units ").Append(realm.Units).Append(" renderers ").Append(realm.Renderers).Append('\n');
            }
            foreach (var passage in passages)
                sb.Append("  passage ").Append((passage.Realm ?? "?").PadRight(12)).Append(passage.In ? "in  " : "out ").Append(passage.Seconds.ToString("F2", CultureInfo.InvariantCulture))
                  .Append(" s | longest frame ").Append(passage.WorstFrameMs.ToString("F1", CultureInfo.InvariantCulture)).Append(" ms\n");
            return sb.ToString();
        }

        string FarText(string id)
        {
            for (int k = 0; FarRealms != null && FarRoots != null && k < FarRealms.Length && k < FarRoots.Length; k++)
                if (FarRealms[k] == id && FarRoots[k] != null) return FarRoots[k].activeSelf ? "on " : "off";
            return "-  ";
        }

        static string F(float v) => v.ToString("F0", CultureInfo.InvariantCulture);
    }
}
