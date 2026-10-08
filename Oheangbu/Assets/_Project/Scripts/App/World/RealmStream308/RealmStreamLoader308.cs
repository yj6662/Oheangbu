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

        /// <summary>True once every realm the first look asked for is in.</summary>
        public bool InitialReady { get; private set; }
        public int Loads { get; private set; }
        public int Unloads { get; private set; }
        public int Purges { get; private set; }
        public string LastError { get; private set; }

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
                await load.ToUniTask(cancellationToken: token);
                states[i] = RealmStreamSheet308.IsLoaded(realm) ? State.In : State.Out; Loads++;
                SyncFar();   // the scene is in: its far band goes in the same frame
            }
            InitialReady = true;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i] != State.In || wanted.Contains(i)) continue;
                var scene = SceneManager.GetSceneByPath(Sheet.Realms[i].ScenePath);
                if (!scene.IsValid() || !scene.isLoaded) { states[i] = State.Out; continue; }
                var unload = SceneManager.UnloadSceneAsync(scene);
                if (unload == null) continue;
                states[i] = State.Leaving;
                await unload.ToUniTask(cancellationToken: token);
                states[i] = State.Out; Unloads++; purgeOwed = true;
                SyncFar();
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
              .Append(" | loads ").Append(Loads).Append(" unloads ").Append(Unloads).Append(" purges ").Append(Purges)
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
