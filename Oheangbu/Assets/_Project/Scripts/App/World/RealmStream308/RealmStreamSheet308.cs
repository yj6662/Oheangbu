using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.World
{
    // [SPEC-WORLD-REALM-STREAM-308] Skin streaming, stage 1. The heavy static look of each realm (buildings, walls, dressing and their
    // colliders) lives in one additive scene per realm; the play scene keeps the session, content, NavMesh, ground and vegetation.
    // This sheet is the only place the numbers live (written by the editor tool RealmStream308 from Tools/Unity/Stage308_stream rules).
    // It does not replace RealmSheetSO / AreaLoader (SPEC-COMPACT-CANON-STREAMING): no session, no content, no active-scene change here.
    public sealed class RealmStreamSheet308 : ScriptableObject
    {
        [Serializable]
        public sealed class Realm
        {
            public string Id;
            [Tooltip("Assets/... path of the additive scene (also in the build settings)")]
            public string ScenePath;
            [Tooltip("XZ outline of what the scene holds (convex, world metres). Distance to it decides loading")]
            public Vector2[] Outline = Array.Empty<Vector2>();
            public int Units;
            public int Renderers;
        }

        [Tooltip("name of the play scene these realm scenes were cut from. A loading flow for another play scene must not bring them in")]
        public string PlayScene;
        public Realm[] Realms = Array.Empty<Realm>();
        [Tooltip("a realm scene is brought in when the player is this close to its outline (m)")]
        public float LoadDistance = 700;
        [Tooltip("and let go when the player is farther than this (m). Must be larger than LoadDistance")]
        public float UnloadDistance = 1000;
        [Tooltip("the realm around the current checkpoint stays (death returns there): kept while the checkpoint is this close to the outline (m)")]
        public float AnchorDistance = 200;
        [Tooltip("seconds between two looks at the player position (unscaled)")]
        public float TickSeconds = .5f;
        [Tooltip("shortest time between two Resources.UnloadUnusedAssets after a realm was let go (s, unscaled)")]
        public float PurgeSeconds = 20;

        /// <summary>Distance from an XZ point to a realm's outline; 0 inside. An empty outline is never near.</summary>
        public static float Distance(Realm realm, Vector3 world)
        {
            var o = realm?.Outline;
            if (o == null || o.Length == 0) return float.PositiveInfinity;
            var p = new Vector2(world.x, world.z);
            if (o.Length == 1) return Vector2.Distance(o[0], p);
            bool inside = false; float best = float.PositiveInfinity;
            for (int i = 0, j = o.Length - 1; i < o.Length; j = i++)
            {
                Vector2 a = o[j], b = o[i];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
                Vector2 ab = b - a; float len = ab.sqrMagnitude;
                float t = len > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0;
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return inside && o.Length > 2 ? 0 : best;
        }

        /// <summary>Indices of the realms that must be present for a point (start of play, loading screen).</summary>
        public void Around(Vector3 world, List<int> into)
        {
            for (int i = 0; i < Realms.Length; i++)
                if (Realms[i] != null && !string.IsNullOrEmpty(Realms[i].ScenePath) && Distance(Realms[i], world) <= LoadDistance && !into.Contains(i)) into.Add(i);
        }

        public static bool IsLoaded(Realm realm)
        {
            var scene = SceneManager.GetSceneByPath(realm.ScenePath);
            return scene.IsValid() && scene.isLoaded;
        }

        /// <summary>Starts the additive load of one realm scene. Editor Play does not need the build settings entry.</summary>
        public static AsyncOperation BeginLoad(Realm realm)
        {
            try
            {
#if UNITY_EDITOR
                return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(realm.ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
#else
                return SceneManager.LoadSceneAsync(realm.ScenePath, LoadSceneMode.Additive);
#endif
            }
            catch (Exception exception) { Debug.LogError("[RealmStream308] load of " + realm.ScenePath + " did not start: " + exception.Message); return null; }
        }

        /// <summary>Starts the loads a point needs and returns them (for a loading screen that waits before the play scene comes in).</summary>
        public List<AsyncOperation> BeginAround(Vector3 world)
        {
            var wanted = new List<int>(); Around(world, wanted);
            var loads = new List<AsyncOperation>();
            foreach (int i in wanted)
            {
                if (IsLoaded(Realms[i])) continue;
                var load = BeginLoad(Realms[i]);
                if (load != null) loads.Add(load);
            }
            return loads;
        }
    }
}
