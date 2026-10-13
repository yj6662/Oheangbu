using System;
using Oheangbu.App.Prologue;
using Oheangbu.Data.World;
using UnityEngine;

namespace Oheangbu.App.World
{
    // #308 D308-77 (TEST): the silent townspeople of ONE city root. One component owns all of them: N logical people (route walkers
    // and standing figures, plain structs) share at most Profile.HardCap pooled bodies (TownWalkerBody308), handed to whoever is
    // nearest to the player. No per-person component, no coroutine, no static state, no singleton. It stands on a root of the play scene
    // (Hanyang308_WalkersHost - the realm scenes hold static parts only), and it does nothing while the player is outside the routes'
    // box + HideDistance (one box test a frame).
    //   - walkers follow authored polylines with baked heights (no ground ray, no NavMesh), keep to the left of their own direction,
    //     turn round at the ends, and sometimes step to a stall / shop front and stand there;
    //   - near the player they step aside (wider when the player rides), or stop and look when there is no room, and are nudged out
    //     of the player's own space - they have no collider, so they can never block a street;
    //   - while an enemy chases the player nearby they turn their backs and walk off; they never follow, greet or speak.
    // Bodies appear only off screen or beyond PopDistance (no fade: the body shaders have no dither; occlusion is not tested).
    [DisallowMultipleComponent]
    public sealed class TownWalkers308 : MonoBehaviour
    {
        public TownWalkerProfile308 Profile;
        public TownWalkerRoutes308 Routes;
        [Tooltip("몸 프리팹. TownWalkerRoutes308.Bodies 와 같은 순서.")]
        public TownWalkerBody308[] BodyPrefabs = Array.Empty<TownWalkerBody308>();
        [Tooltip("같은 씬의 세션(없으면 깨어날 때 찾는다 - 강토 씬은 플레이 씬의 세션을 직접 가리킬 수 없다).")]
        public WorldMacroPlaytestSession Session;

        enum Mode : byte { Walk, Browse, Post }
        struct Person
        {
            public Mode Mode; public bool IsPost, Averted;
            public int Route, Segment, Direction, Stop, Slot, Body, Tint, Job, Seed;
            public float S, Lane, Lat, Half, Speed, Until, NextBrowse, GlanceUntil, GlanceReady, LastTick, Moved, Yaw, HomeYaw, Distance, Look;
            public Vector3 Position, Home; public Vector2 Push; public uint Rng;
        }
        Person[] people = Array.Empty<Person>();
        float[][] cumulative = Array.Empty<float[]>(); int[] stopFirst = Array.Empty<int>(), stopCount = Array.Empty<int>();
        TownWalkerBody308[] slotBody = Array.Empty<TownWalkerBody308>(); int[] slotPerson = Array.Empty<int>(), slotType = Array.Empty<int>(); int slots;
        int[] activeOfType = Array.Empty<int>(), madeOfType = Array.Empty<int>();
        Transform player; Camera view; bool built, inside, threat; int active, cursor;
        float nextAssign, nextThreat, nextSession, threatUntil, insideSince;

        public int Total => people.Length;
        public int Active => active;
        public bool Threatened => threat;
        public bool Inside => inside;

        void OnEnable() { inside = false; nextAssign = nextThreat = nextSession = 0; threat = false; threatUntil = 0; }   // the pool (children of this object) is kept across a disable
        void OnDisable() { ReleaseAll(); }

        bool Ready()
        {
            if (Profile == null || !Profile.Usable || Routes == null) return false;
            if (Session == null && Time.unscaledTime >= nextSession) { Session = FindFirstObjectByType<WorldMacroPlaytestSession>(); nextSession = Time.unscaledTime + 1f; }
            var walker = Session != null ? Session.Walker : null; if (walker == null || walker.Body == null) return false;
            player = walker.Body.transform; view = walker.ViewCamera;
            if (!built) Build();
            return people.Length > 0;
        }

        void Build()
        {
            built = true; var rs = Routes.Routes; int rn = rs.Length;
            cumulative = new float[rn][]; stopFirst = new int[rn]; stopCount = new int[rn];
            for (int r = 0; r < rn; r++)
            {
                var p = rs[r].Points; var c = new float[p.Length];
                for (int i = 1; i < p.Length; i++) c[i] = c[i - 1] + Flat(p[i], p[i - 1]);
                cumulative[r] = c; stopFirst[r] = -1;
            }
            for (int i = 0; i < Routes.Stops.Length; i++) { int r = Routes.Stops[i].Route; if (r < 0 || r >= rn) continue; if (stopFirst[r] < 0) stopFirst[r] = i; stopCount[r]++; }
            int types = BodyPrefabs.Length; activeOfType = new int[types]; madeOfType = new int[types];
            int cap = 0; for (int t = 0; t < types; t++) if (BodyPrefabs[t] != null && BodyPrefabs[t].Usable) cap += Mathf.Min(BodyPrefabs[t].MaxActive, Profile.HardCap);
            slotBody = new TownWalkerBody308[cap]; slotPerson = new int[cap]; slotType = new int[cap]; slots = 0;
            var list = new Person[Routes.Walkers.Length + Routes.Posts.Length]; int n = 0; float now = Time.time;
            foreach (var w in Routes.Walkers)
            {
                if (w.Route < 0 || w.Route >= rn || rs[w.Route].Points.Length < 2 || w.Body < 0 || w.Body >= types) continue;
                float length = cumulative[w.Route][rs[w.Route].Points.Length - 1]; if (length <= 0) continue;
                var a = new Person { Mode = Mode.Walk, Route = w.Route, Direction = w.Direction < 0 ? -1 : 1, Stop = -1, Slot = -1, Body = w.Body, Tint = w.Tint, Seed = w.Seed,
                    S = w.Start * length, Lane = Mathf.Min(w.Lane, rs[w.Route].HalfWidth), Speed = Mathf.Lerp(Profile.WalkSpeedMin, Profile.WalkSpeedMax, w.Speed), LastTick = now, Rng = (uint)w.Seed | 1u };
                Place(ref a, out _); a.Lat = OwnLane(ref a); Place(ref a, out _); a.Yaw = a.HomeYaw; list[n++] = a;
            }
            foreach (var p in Routes.Posts)
            {
                if (p.Body < 0 || p.Body >= types) continue;
                list[n++] = new Person { Mode = Mode.Post, IsPost = true, Route = -1, Stop = -1, Slot = -1, Body = p.Body, Tint = p.Tint, Job = p.Job, Seed = p.Seed, Position = p.Position, Home = p.Position,
                    Yaw = p.Yaw, HomeYaw = p.Yaw, LastTick = now, Rng = (uint)p.Seed | 1u };
            }
            Array.Resize(ref list, n); people = list;
        }

        void Update()
        {
            using (Perf307Markers.NpcJob.Auto())
            {
                float dt = Time.deltaTime; if (dt <= 0) return;
                if (!Ready()) { if (inside) { ReleaseAll(); inside = false; } return; }   // the profile was switched off (or lost) while bodies stood: hand them back
                Vector3 at = player.position; float reach = Profile.HideDistance;
                bool now = at.x > Routes.BoundsMin.x - reach && at.x < Routes.BoundsMax.x + reach && at.z > Routes.BoundsMin.y - reach && at.z < Routes.BoundsMax.y + reach;
                if (!now) { if (inside) { ReleaseAll(); inside = false; } return; }
                float time = Time.time;
                if (!inside) { inside = true; insideSince = time; nextAssign = 0; nextThreat = 0; }
                if (time >= nextThreat) { nextThreat = time + Profile.ThreatScanSeconds; ScanThreat(at, time); }
                bool seated = Session.Walker.Seated;
                for (int s = 0; s < slots; s++) { int i = slotPerson[s]; if (i >= 0) Step(ref people[i], time, at, seated, true); }
                for (int k = 0, n = Mathf.Min(Profile.LogicPerFrame, people.Length); k < n; k++)
                {
                    cursor = (cursor + 1) % people.Length; if (people[cursor].Slot < 0) Step(ref people[cursor], time, at, seated, false);
                }
                if (time >= nextAssign) { nextAssign = time + Profile.AssignSeconds; Assign(at, time); }
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime; if (dt <= 0 || !inside || active == 0 || Profile == null || player == null || Session == null || Session.Walker == null) return;
            using (Perf307Markers.RigEvaluate.Auto())
            {
                Vector3 eye = player.position + Vector3.up * Session.Walker.EyeHeight; float far = 1f / Profile.FarPoseHz;
                for (int s = 0; s < slots; s++)
                {
                    int i = slotPerson[s]; if (i < 0) continue; ref var a = ref people[i]; var body = slotBody[s];
                    body.transform.SetPositionAndRotation(a.Position + new Vector3(a.Push.x, 0, a.Push.y), Quaternion.Euler(0, a.Yaw, 0));
                    bool walking = a.Moved > .0005f; bool working = a.IsPost && !(threat && a.Distance < Profile.ThreatReactRadius);
                    body.Pose(dt, a.Moved, walking, working, eye, a.Look * Profile.HeadWeight, a.Distance > Profile.FullRateDistance ? far : 0, Profile.CrossFadeSeconds, Profile.HeadBlendSeconds, Profile.HeadYawClamp, Profile.HeadPitchClamp);
                    a.Moved = 0;
                }
            }
        }

        // an enemy that is chasing while it stands near the player (bosses: a wider ring) = a fight the townspeople turn away from
        void ScanThreat(Vector3 at, float time)
        {
            var actors = Session.Actors;
            if (actors != null && Session.CombatActive)
                for (int i = 0; i < actors.Length; i++)
                {
                    var e = actors[i]; if (e == null || !e.isActiveAndEnabled || e.Current != PrologueEncounter.Behaviour.Chase) continue;
                    float radius = Array.IndexOf(Profile.WideThreatIds, e.Id) >= 0 ? Profile.WideThreatRadius : Profile.ThreatRadius;
                    if (Flat(e.transform.position, at) < radius) { threatUntil = time + Profile.ThreatHoldSeconds; break; }
                }
            threat = time < threatUntil;
        }

        void Step(ref Person a, float time, Vector3 at, bool seated, bool fine)
        {
            float dt = Mathf.Min(time - a.LastTick, 600f); a.LastTick = time; if (dt <= 0) return;
            var P = Profile;
            if (a.IsPost)
            {
                if (!fine) return;
                float d = Flat(at, a.Home); a.Distance = d; bool turned = threat && d < P.ThreatReactRadius; float want = a.HomeYaw;
                if (turned) { Vector3 away = a.Home - at; if (away.x * away.x + away.z * away.z > .01f) want = Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg; }
                a.Yaw = Mathf.MoveTowardsAngle(a.Yaw, want, P.TurnDegreesPerSecond * dt);
                Glance(ref a, time, d, !turned, false); PushOut(ref a, at, dt); return;
            }
            var route = Routes.Routes[a.Route]; var pts = route.Points; float length = cumulative[a.Route][pts.Length - 1];
            if (!fine)
            {
                // no body: only the place moves (a long gap just lands the person somewhere else along the route)
                if (a.Mode == Mode.Browse) { if (time < a.Until) return; a.Mode = Mode.Walk; a.Stop = -1; }
                float travel = a.Speed * dt;
                if (route.Loop) a.S = Mathf.Repeat(a.S + travel * a.Direction, length);
                else { float u = Mathf.Repeat((a.Direction > 0 ? a.S : 2 * length - a.S) + travel, 2 * length); if (u <= length) { a.S = u; a.Direction = 1; } else { a.S = 2 * length - u; a.Direction = -1; } }
                a.Stop = -1; a.Averted = false; a.Segment = 0; Place(ref a, out _); a.Lat = OwnLane(ref a); Place(ref a, out _); a.Yaw = a.HomeYaw; a.Push = Vector2.zero; a.Look = 0;
                return;
            }
            Place(ref a, out Vector2 tangent);
            Vector2 forward = tangent * a.Direction, toPlayer = new Vector2(at.x - a.Position.x, at.z - a.Position.z); float dist = toPlayer.magnitude; a.Distance = dist;
            bool turnedAway = threat && dist < P.ThreatReactRadius; float speed = a.Speed, latTarget = OwnLane(ref a), yawTarget = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg; bool stopped = false;
            if (turnedAway)
            {
                if (a.Mode == Mode.Browse) { a.Mode = Mode.Walk; a.Stop = -1; }
                if (!a.Averted && Vector2.Dot(forward, toPlayer) > 0) { a.Direction = -a.Direction; forward = -forward; latTarget = -latTarget; yawTarget += 180; }
                a.Averted = true; a.Stop = -1; speed *= P.AvertSpeedScale;
            }
            else
            {
                a.Averted = false;
                if (a.Mode == Mode.Browse)
                {
                    var stop = Routes.Stops[a.Stop]; latTarget = stop.Lateral; yawTarget = stop.Yaw; speed = 0;
                    if (time >= a.Until) { a.Mode = Mode.Walk; a.Stop = -1; a.NextBrowse = time + P.BrowseCheckSeconds * 3; }
                }
                else
                {
                    if (a.Stop < 0 && time >= a.NextBrowse && stopCount[a.Route] > 0) { a.NextBrowse = time + P.BrowseCheckSeconds; PickStop(ref a); }
                    if (a.Stop >= 0)
                    {
                        var stop = Routes.Stops[a.Stop]; latTarget = stop.Lateral; float ahead = (stop.Distance - a.S) * a.Direction;
                        if (ahead < -1f) a.Stop = -1;
                        else if (ahead <= speed * dt + .05f && Mathf.Abs(a.Lat - stop.Lateral) < .25f) { a.S = stop.Distance; a.Mode = Mode.Browse; a.Until = time + Mathf.Lerp(P.BrowseSecondsMin, P.BrowseSecondsMax, Next(ref a)); speed = 0; }
                        else if (ahead <= speed * dt + .05f) speed = 0;   // at the stall's distance but still stepping across to it
                    }
                    // the player ahead: step to the side that is already further from the player; no room -> stand and look
                    float yield = seated ? P.SeatedYieldRadius : P.YieldRadius;
                    if (dist < yield && Vector2.Dot(forward, toPlayer) > -.5f)
                    {
                        Vector2 right = new Vector2(tangent.y, -tangent.x); float playerLat = a.Lat + Vector2.Dot(toPlayer, right);
                        float side = a.Lat >= playerLat ? 1 : -1; latTarget = Mathf.Clamp(playerLat + side * (P.SideStepMetres + (seated ? P.SideStepMetres : 0)), -a.Half, a.Half);
                        if (Mathf.Abs(latTarget - playerLat) < P.SideStepMetres * .6f) latTarget = Mathf.Clamp(playerLat - side * P.SideStepMetres, -a.Half, a.Half);
                        if (dist < P.StopRadius && Vector2.Dot(forward, toPlayer) > 0) { speed = 0; stopped = true; }
                    }
                }
            }
            float before = a.Lat; a.Lat = Mathf.MoveTowards(a.Lat, latTarget, P.SideStepSpeed * dt);
            float moved = speed * dt;
            if (moved > 0)
            {
                a.S += moved * a.Direction;
                if (route.Loop) a.S = Mathf.Repeat(a.S, length);
                else if (a.S >= length) { a.S = length; a.Direction = -1; a.Stop = -1; }
                else if (a.S <= 0) { a.S = 0; a.Direction = 1; a.Stop = -1; }
            }
            a.Moved += moved + Mathf.Abs(a.Lat - before) * .5f;
            Place(ref a, out _);
            a.Yaw = Mathf.MoveTowardsAngle(a.Yaw, yawTarget, P.TurnDegreesPerSecond * dt);
            Glance(ref a, time, dist, !turnedAway, stopped); PushOut(ref a, at, dt);
        }

        // the first stop ahead within StopLookAhead, taken with BrowseChancePerCheck
        void PickStop(ref Person a)
        {
            int first = stopFirst[a.Route], n = stopCount[a.Route]; float best = Profile.StopLookAhead; int pick = -1;
            for (int i = first; i < first + n; i++) { float ahead = (Routes.Stops[i].Distance - a.S) * a.Direction; if (ahead > 1.5f && ahead < best) { best = ahead; pick = i; } }
            if (pick >= 0 && Next(ref a) < Profile.BrowseChancePerCheck) a.Stop = pick;
        }

        void Glance(ref Person a, float time, float dist, bool allowed, bool hold)
        {
            if (allowed && dist < Profile.GlanceRadius && time >= a.GlanceReady)
            { a.GlanceUntil = time + Mathf.Lerp(Profile.GlanceHoldMin, Profile.GlanceHoldMax, Next(ref a)); a.GlanceReady = a.GlanceUntil + Profile.GlanceCooldownSeconds; }
            a.Look = allowed && (time < a.GlanceUntil || hold) ? 1 : 0;
        }

        // the body gives way to the player's own space (a visual offset: the person's place on the route does not change)
        void PushOut(ref Person a, Vector3 at, float dt)
        {
            Vector2 off = new Vector2(a.Position.x - at.x, a.Position.z - at.z), want = Vector2.zero; float m = off.magnitude;
            if (m < Profile.SoftPushRadius) want = (m > .01f ? off / m : new Vector2(Mathf.Sin(a.Yaw * Mathf.Deg2Rad), Mathf.Cos(a.Yaw * Mathf.Deg2Rad)) * -1f) * (Profile.SoftPushRadius - m);
            a.Push = Vector2.MoveTowards(a.Push, want, Profile.SoftPushSpeed * dt);
        }

        // the walker's own lane: left of its direction of travel, inside the usable half width where the street narrows (bridge, stalls)
        static float OwnLane(ref Person a) { float lane = Mathf.Min(a.Lane, a.Half); return a.Direction > 0 ? -lane : lane; }

        // position on the route at S (+ lateral); HomeYaw = the facing along the direction of travel; Half = the usable half width there
        void Place(ref Person a, out Vector2 tangent)
        {
            var pts = Routes.Routes[a.Route].Points; var c = cumulative[a.Route]; int last = pts.Length - 2; int i = Mathf.Clamp(a.Segment, 0, last);
            while (i < last && a.S > c[i + 1]) i++;
            while (i > 0 && a.S < c[i]) i--;
            a.Segment = i; float len = c[i + 1] - c[i], t = len > 0 ? Mathf.Clamp01((a.S - c[i]) / len) : 0; Vector3 p = Vector3.LerpUnclamped(pts[i], pts[i + 1], t);
            tangent = new Vector2(pts[i + 1].x - pts[i].x, pts[i + 1].z - pts[i].z); float m = tangent.magnitude; tangent = m > 0 ? tangent / m : Vector2.up;
            var route = Routes.Routes[a.Route]; Vector2 right = route.RightAt(i, t, tangent); p.x += right.x * a.Lat; p.z += right.y * a.Lat; p.y += route.Rise(i, t, a.Lat); a.Half = route.HalfAt(i, t); a.Position = p; a.HomeYaw = Mathf.Atan2(tangent.x * a.Direction, tangent.y * a.Direction) * Mathf.Rad2Deg;
        }

        void Assign(Vector3 at, float time)
        {
            var P = Profile;
            for (int s = 0; s < slots; s++)
            {
                int i = slotPerson[s]; if (i < 0) continue; float d = Flat(people[i].Position, at); people[i].Distance = d;
                if (d > P.HideDistance) Release(s); else slotBody[s].SetShadows(d < P.ShadowDistance);
            }
            bool first = time - insideSince < P.FirstFillSeconds;
            for (int k = 0; k < P.SpawnsPerAssign && active < P.HardCap; k++)
            {
                int pick = -1; float best = P.ShowDistance;
                for (int i = 0; i < people.Length; i++)
                {
                    if (people[i].Slot >= 0) continue; int type = people[i].Body; var prefab = BodyPrefabs[type];
                    if (prefab == null || !prefab.Usable || activeOfType[type] >= prefab.MaxActive) continue;
                    float d = Flat(people[i].Position, at); if (d >= best) continue;
                    if (!first && d < P.PopDistance && InView(people[i].Position)) continue;
                    best = d; pick = i;
                }
                if (pick < 0) break;
                Give(pick, best);
            }
        }

        bool InView(Vector3 p)
        {
            if (view == null) return true;
            Vector3 v = view.WorldToViewportPoint(p + Vector3.up); float m = Profile.ViewMargin;
            return v.z > 0 && v.x > -m && v.x < 1 + m && v.y > -m && v.y < 1 + m;
        }

        void Give(int index, float distance)
        {
            ref var a = ref people[index]; int type = a.Body, slot = -1;
            for (int s = 0; s < slots; s++) if (slotPerson[s] < 0 && slotType[s] == type) { slot = s; break; }
            if (slot < 0)
            {
                if (slots >= slotBody.Length) return;
                var body = Instantiate(BodyPrefabs[type], transform); body.name = BodyPrefabs[type].name + "_" + madeOfType[type]++; body.gameObject.SetActive(false);
                slot = slots++; slotBody[slot] = body; slotType[slot] = type; slotPerson[slot] = -1;
            }
            var b = slotBody[slot]; slotPerson[slot] = index; a.Slot = slot; a.Distance = distance; a.Moved = 0; a.Push = Vector2.zero; a.Look = 0; a.LastTick = Time.time; active++; activeOfType[type]++;
            b.transform.SetPositionAndRotation(a.Position, Quaternion.Euler(0, a.Yaw, 0)); b.gameObject.SetActive(true);
            Color tint = Profile.Tints != null && a.Tint >= 0 && a.Tint < Profile.Tints.Length ? Profile.Tints[a.Tint] : Color.white;
            uint h = (uint)a.Seed * 2246822519u; b.Begin(tint, a.Tint, Mathf.Lerp(Profile.ScaleMin, Profile.ScaleMax, (h >> 8 & 0xffff) / 65535f), a.Seed, a.IsPost ? a.Job : 0);
            b.SetShadows(distance < Profile.ShadowDistance);
        }

        void Release(int slot)
        {
            int i = slotPerson[slot]; if (i < 0) return;
            people[i].Slot = -1; slotPerson[slot] = -1; active--; activeOfType[slotType[slot]]--;
            if (slotBody[slot] != null) slotBody[slot].gameObject.SetActive(false);
        }

        void ReleaseAll() { for (int s = 0; s < slots; s++) Release(s); active = 0; for (int t = 0; t < activeOfType.Length; t++) activeOfType[t] = 0; }

        static float Next(ref Person a) { uint x = a.Rng; x ^= x << 13; x ^= x >> 17; x ^= x << 5; a.Rng = x; return (x & 0xffffff) / 16777216f; }
        static float Flat(Vector3 a, Vector3 b) { float x = a.x - b.x, z = a.z - b.z; return Mathf.Sqrt(x * x + z * z); }
    }
}
