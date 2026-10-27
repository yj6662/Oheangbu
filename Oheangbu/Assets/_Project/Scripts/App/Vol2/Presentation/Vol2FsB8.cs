using System.Collections.Generic;
using Oheangbu.Vol2.Rules;
using UnityEngine;
using R = Oheangbu.Vol2.Rules.ReactionRulesV3;

namespace Oheangbu.App
{
    // D308-150 V12 free showcase batch 8 (2026-10-27, the user's order "쇼케이스 방향이 낫다. 이 방식으로 다른 술식도 만들어 가자." / "잘 진행해둬"): the 25 directions of the
    // wuxing reaction table (acting body -> contacted body, 5 x 5) - 비화 x5 (Same), 먹임 x5 (Feed), 옮겨붙음 x5 (Transfer, 화>목 = 들불), 꺾음 x5 (Break: 목>토 뿌리 붕괴 ·
    // 화>금 달군 쇠 · 토>수 진창 · 금>목 가시 · 수>화 김), 막힘 x5 (Block: 목>금 잘림 · 화>수 꺼짐 · 토>목 걸림 · 금>화 무뎌짐 · 수>토 막힘).
    // A reaction is no cast: its look is not a Vol2FsRun. Like 걱 (Vol2FsGeok) it is made and ticked by the showcase installer, from the reaction engine's COMMITTED
    // event (Vol2ReactionEngine.ReactionResolved -> Vol2ReactionVisual) and its ledger line, then follows only the facts on the scope clock:
    //   the commit (t = 0) -> what each side did (consumed / thickened (비화) / halved (막힘) / dried (a child met again)) -> the product: the CHILD material
    //   (먹임 / 옮겨붙음: body, amount, 10 s life - read live from the registry to its own end), the SLOW (뿌리 붕괴 2 s / 진창 3 s on the bodies standing in the
    //   contacted footprint), the DAMAGE (가시 / 김 once; 달군 쇠 = the ledger's tick count growing every 0.5 s for 2 s) -> the surviving contact material of 비화 / 막힘
    //   to its own end. Nothing here decides a reaction, an amount, a body or a lifetime. Presentation only: nothing here is read by a rule.

    /// <summary>One reaction's showcase look (one committed reaction). A cell's class fills the hooks; the base gives it the facts as numbers and the
    /// contacted material's stand-in to consume / thicken / halve.</summary>
    public abstract class Vol2FsReact
    {
        protected static readonly Color InkC = new Color(0.03f, 0.025f, 0.02f, 1f);
        protected Vol2FsKit Kit; protected GameObject Root; protected Vol2CastCoordinator Co;
        protected Vol2ReactionVisual V; protected Vol2ReactionRecord Rec;
        public string Note = "";
        public long EventId => V != null ? V.EventId : 0;
        /// <summary>"목>화" - the cell (for the status).</summary>
        public string Cell => V == null ? "?" : Vol2FsB8.Han(V.ActingBody) + ">" + Vol2FsB8.Han(V.ContactedBody);

        protected R.Kind Kind; protected R.Product Product; protected Body Acting, Contacted, ChildBody;
        /// <summary>The two bodies' accents (Phantom Blade principle 1: one accent per body).</summary>
        protected Color AccA, AccC, AccChild;
        /// <summary>The contact: the contacted material's footprint centre on the ground, its ground height, its size (disc radius; ring middle; fan depth x 0.4).</summary>
        protected Vector3 P; protected float Ground, Radius = 0.8f;
        /// <summary>Where the acting side came from (the moving shape's cast origin / the acting material's centre) and the incoming direction at P (flat), its right.</summary>
        protected Vector3 From, Dir = Vector3.forward, Side = Vector3.right;
        protected int Seed;
        /// <summary>The acting side was a moving shape (a cast in flight), not a standing material.</summary>
        protected bool FromShape;
        /// <summary>The product (child) belongs to the enemy: an enemy's newer material acted (the 점화자 표식 is the enemy's).</summary>
        protected bool EnemyIgniter;
        /// <summary>The contacted material was the enemy's (its stand-in is drawn in the enemy's ink).</summary>
        protected bool EnemyContact = true;
        /// <summary>A child met again: it only dried (ChildConsumed, 0.3 s, no direction, no new child).</summary>
        protected bool Dried;
        /// <summary>The product's duration (s): slow 2 / 3, the lasting damage 2; 0 = instant.</summary>
        protected float Dur;
        /// <summary>김's sight cover (s).</summary>
        protected float Occl;
        protected float DryRel;
        /// <summary>The surviving contact material (비화 / 막힘) and the child (먹임 / 옮겨붙음): their end on the look clock (-1 = alive), why, and the live left time.</summary>
        protected float ContactEndAt = -1f, ChildEndAt = -1f;
        protected Vol2MaterialEnd ContactWhy = Vol2MaterialEnd.None, ChildWhy = Vol2MaterialEnd.None;
        protected bool ContactSurvives => V != null && V.ContactEnd == Vol2MaterialEnd.None;
        protected bool HasChild => V != null && V.ChildId > 0;
        /// <summary>The child's / surviving contact's expiry on the look clock (live: a 비화 renews it).</summary>
        protected float ChildExpireRel = -1f, ContactExpireRel = -1f;
        /// <summary>Thickened (비화: x1.2 of the sum) / halved (막힘: x0.5): the surviving contact's amount now against what it held at the commit.</summary>
        protected float AmountBefore, AmountNow;
        /// <summary>The contacted material's stand-in, built at the commit with the same seed as the lab's proxy (a disc footprint only; null otherwise).</summary>
        protected Vol2FsPatch Patch;
        protected long NowUs;
        float _stopUntil = -1f; int _hitsSeen; bool _hard;

        /// <summary>The bodies standing in the contacted footprint at the commit (the product's bodies: the ledger's AffectedIds).</summary>
        public sealed class Struck { public long Id; public Transform T; public Bounds B; public Vector3 Feet; public Vector3 Dir; public int Index; public object Data; }
        protected readonly List<Struck> Bodies = new List<Struck>();
        /// <summary>The lasting damage's ticks seen so far (달군 쇠: the ledger's Hits grows every 0.5 s).</summary>
        protected int DamageTicks;

        // ---- hooks ----
        /// <summary>At the commit (t = 0): build everything.</summary>
        protected abstract void OnBuild();
        /// <summary>Every frame (t = seconds since the commit).</summary>
        protected abstract void OnLive(float t);
        /// <summary>A damage request of the product reached the bodies (k = 0 for an instant one; 0..3 for 달군 쇠).</summary>
        protected virtual void OnDamageTick(int k, float t) { }
        /// <summary>The surviving contact material (비화 / 막힘) ended (Expired / Consumed by another reaction / Cleansed ...). Aborted never reaches here (hard stop).</summary>
        protected virtual void OnContactEnd(Vol2MaterialEnd why, float t) { }
        /// <summary>The child ended (Expired after 10 s / Consumed / ChildConsumed by a later contact ...). Aborted never reaches here (hard stop).</summary>
        protected virtual void OnChildEnd(Vol2MaterialEnd why, float t) { }
        /// <summary>How long the reaction moment itself shows at least (s).</summary>
        protected virtual float Burst => 1.8f;
        protected virtual float Tail => 0.9f;

        /// <summary>Called once by the installer right after the commit.</summary>
        public void Begin(Vol2FsKit kit, Transform parent, Vol2CastCoordinator co, Vol2ReactionVisual v)
        {
            Kit = kit; Co = co; V = v;
            Root = new GameObject("Vol2FreeShowcase_Rx_" + v.EventId + "_" + v.ActingBody + "_" + v.ContactedBody);
            if (parent != null) Root.transform.SetParent(parent, false);
            Root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Root.transform.localScale = Vector3.one;
            Kind = v.Relation; Product = v.Product; Acting = v.ActingBody; Contacted = v.ContactedBody; ChildBody = v.ChildBody;
            AccA = Vol2FsRun.Accent(Acting); AccC = Vol2FsRun.Accent(Contacted); AccChild = Vol2FsRun.Accent(HasChild ? ChildBody : Contacted);
            Seed = (int)((v.EventId * 7919 + v.ContactId) & 0x7fff);
            NowUs = v.AtUs;
            var led = co != null ? co.Reactions.Ledger : null;
            if (led != null) for (int i = led.Count - 1; i >= 0; i--) if (led[i].EventId == v.EventId) { Rec = led[i]; break; }
            FromShape = v.ActingIsShape; EnemyIgniter = v.FactionId == Vol2Faction.Enemy; EnemyContact = Rec == null || Rec.ContactFaction != Vol2Faction.Player;
            Dried = v.DryUs > 0; DryRel = v.DryUs / 1e6f; Dur = v.DurationUs / 1e6f; Occl = v.OcclusionUs / 1e6f;
            // the place
            var fp = v.Where;
            P = fp != null ? fp.Centre : v.Point;
            if (fp != null)
                switch (fp.Kind)
                {
                    case Vol2FootprintKind.Disc: Radius = Mathf.Clamp(fp.Radius, 0.3f, 3f); break;
                    case Vol2FootprintKind.Ring: Radius = Mathf.Clamp((fp.RingInner + fp.RingOuter) * 0.5f, 0.5f, 5f); break;
                    case Vol2FootprintKind.Fan: Radius = Mathf.Clamp(fp.FanDepth * 0.4f, 0.5f, 3f); break;
                }
            Ground = P.y;
            // where the acting side came from: the moving shape's cast (its spec origin), else the acting material
            From = P - CamFwd() * 4f;
            if (FromShape && co != null && co.TryGetLog(v.ActingCast, out var log) && log != null && log.Spec != null) From = log.Spec.Origin;
            else if (!FromShape && co != null && co.Materials.TryGet(new MaterialId(v.ActingId), out var am) && am != null && am.Footprint != null) From = am.Footprint.Centre;
            var d = P - From; d.y = 0f;
            if (d.sqrMagnitude < 0.04f) { d = CamFwd(); d.y = 0f; }
            Dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            Side = Vector3.Cross(Vector3.up, Dir);
            // the surviving contact / the child
            if (co != null && co.Materials.TryGet(new MaterialId(v.ContactId), out var cm) && cm != null)
            {
                AmountNow = (float)cm.Remaining; AmountBefore = Rec != null ? (float)(Rec.ParentSum) : AmountNow;
                if (ContactSurvives) ContactExpireRel = Rel(cm.ExpiresUs);
            }
            if (HasChild && co != null && co.Materials.TryGet(new MaterialId(v.ChildId), out var ch) && ch != null) ChildExpireRel = Rel(ch.ExpiresUs);
            // the bodies of the product
            if (Rec != null && co != null)
                foreach (long id in Rec.AffectedIds)
                {
                    var tr = co.Registry.Find(id) is Vol2EnemyTarget e && e.Vitals != null ? e.Vitals.transform : null;
                    if (tr == null) continue;
                    var feet = tr.position;
                    var dir = feet - P; dir.y = 0f;
                    Bodies.Add(new Struck { Id = id, T = tr, Feet = feet, B = Vol2FsRunBounds.Of(tr, feet, 0.5f), Dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Dir, Index = Bodies.Count });
                }
            // the contacted material's stand-in (a disc only: the lab's proxy is hidden the frame the reaction commits; this copy carries on from it)
            if (fp != null && fp.Kind == Vol2FootprintKind.Disc && kit != null && kit.Ok)
                Patch = Vol2FsPatch.Build(kit, Root.transform, Contacted, P, Radius, v.ContactId, EnemyContact);
            Note = Cell + " " + Kind + "/" + Product + (FromShape ? " shape" : " standing") + (v.Returning ? " returning" : "")
                + (HasChild ? " child " + Vol2FsB8.Han(ChildBody) + " " + v.ChildAmount.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " life " + (v.ChildLifeUs / 1e6f).ToString("0.0") + "s" : "")
                + (Dried ? " dried " + DryRel.ToString("0.0") + "s" : "") + (ContactSurvives ? " contact " + v.ContactReason : "") + (Dur > 0f ? " dur " + Dur.ToString("0.0") + "s" : "")
                + (Occl > 0f ? " occl " + Occl.ToString("0.0") + "s" : "") + (Rec != null ? " bodies " + Rec.Affected + " slows " + Rec.SlowsApplied + " hits " + Rec.Hits : "") + (EnemyIgniter ? " ENEMY igniter" : "");
            try { OnBuild(); } catch (System.Exception e) { Debug.LogException(e); }
        }

        protected float Rel(long us) => V == null ? 0f : (float)((us - V.AtUs) / 1e6);
        protected Vector3 CamPos { get { var c = Camera.main; return c != null ? c.transform.position : P - Dir * 6f + Vector3.up * 2.2f; } }
        static Vector3 CamFwd() { var c = Camera.main; return c != null ? c.transform.forward : Vector3.forward; }
        protected Vector3 G(Vector3 p) { p.y = Ground; return p; }
        /// <summary>A point of the contacted footprint at polar (angle from Dir, distance 0..1 of the radius), on the ground.</summary>
        protected Vector3 At(float angRad, float r01) => G(P + (Dir * Mathf.Cos(angRad) + Side * Mathf.Sin(angRad)) * Radius * r01);
        protected static Vol2FsMat M(string tex, Color tint, float intensity, float addMix) => Vol2FsMat.Make(tex, tint, intensity, addMix);

        /// <summary>A short hit-stop of this look's particles from now.</summary>
        protected void HitStop(float t, float seconds = Vol2FsRun.HitStopSeconds)
        {
            if (Root == null) return;
            _stopUntil = Mathf.Max(_stopUntil, t + seconds);
            foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>()) { var m = ps.main; m.simulationSpeed = 0.04f; }
        }

        public void Tick(long nowUs)
        {
            if (Root == null || _hard) return;
            NowUs = nowUs;
            float t = Rel(nowUs);
            if (_stopUntil > 0f && t >= _stopUntil)
            {
                _stopUntil = -1f;
                foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>()) { var m = ps.main; m.simulationSpeed = 1f; }
            }
            if (Co == null) { Hard("no scope"); return; }
            if (ContactSurvives && ContactEndAt < 0f)
            {
                if (!Co.Materials.TryGet(new MaterialId(V.ContactId), out var cm) || cm == null || !cm.Alive)
                {
                    ContactWhy = cm != null ? cm.End : Vol2MaterialEnd.Aborted; ContactEndAt = t;
                    Note += " | contact end " + ContactWhy + " @" + t.ToString("0.00");
                    if (ContactWhy == Vol2MaterialEnd.Aborted) { Hard("contact aborted"); return; }
                    try { OnContactEnd(ContactWhy, t); } catch (System.Exception e) { Debug.LogException(e); }
                }
                else { ContactExpireRel = Rel(cm.ExpiresUs); AmountNow = (float)cm.Remaining; }
            }
            if (HasChild && ChildEndAt < 0f)
            {
                if (!Co.Materials.TryGet(new MaterialId(V.ChildId), out var ch) || ch == null || !ch.Alive)
                {
                    ChildWhy = ch != null ? ch.End : Vol2MaterialEnd.Aborted; ChildEndAt = t;
                    Note += " | child end " + ChildWhy + " @" + t.ToString("0.00");
                    if (ChildWhy == Vol2MaterialEnd.Aborted) { Hard("child aborted"); return; }
                    try { OnChildEnd(ChildWhy, t); } catch (System.Exception e) { Debug.LogException(e); }
                }
                else ChildExpireRel = Rel(ch.ExpiresUs);
            }
            if (Rec != null && Rec.Hits > _hitsSeen)
            {
                while (_hitsSeen < Rec.Hits) { int k = _hitsSeen++; DamageTicks = _hitsSeen; try { OnDamageTick(k, t); } catch (System.Exception e) { Debug.LogException(e); } }
            }
            // a body that went away (the lab's teardown) is dropped from the look
            for (int i = Bodies.Count - 1; i >= 0; i--) if (Bodies[i].T == null) { Bodies[i].Data = null; }
            OnLive(t);
        }

        void Hard(string why)
        {
            _hard = true;
            if (Root != null) Root.SetActive(false);
            Note += " | stop (" + why + ")";
        }

        /// <summary>The installer drops the look when this is true.</summary>
        public bool Done(long nowUs)
        {
            if (Root == null || _hard) return true;
            float t = Rel(nowUs);
            if (t > 40f) return true;
            float by = Mathf.Max(Burst, Dur + 0.5f);
            if (ContactSurvives) { if (ContactEndAt < 0f) return false; by = Mathf.Max(by, ContactEndAt + 0.6f); }
            if (HasChild) { if (ChildEndAt < 0f) return false; by = Mathf.Max(by, ChildEndAt + 0.6f); }
            return t > by + Tail && (!ParticlesAlive() || t > by + Tail + 2.5f);
        }

        protected bool ParticlesAlive()
        {
            if (Root == null) return false;
            foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>()) if (ps.IsAlive(false)) return true;
            return false;
        }

        public void Dispose()
        {
            if (Patch != null) { Patch.FreeMeshes(Kit); Patch = null; }
            if (Root != null)
            {
                foreach (var f in Root.GetComponentsInChildren<MeshFilter>(true)) if (f.sharedMesh != null && (Kit == null || !Kit.Owns(f.sharedMesh))) Object.Destroy(f.sharedMesh);
                Object.Destroy(Root);
            }
            Root = null;
        }
    }

    /// <summary>The contacted material's stand-in: an enemy's (or my) patch of a body on the ground - a dark ink pool with the body's form on it (wood: low thorny
    /// vines and leaves; fire: licking flames on a charred bed; earth: jutting slabs; metal: standing blades; water: a dark pool and ripples). LAB-SIMULATED in the
    /// capture (in the game the material's own look stands there); the reaction looks build the SAME patch (same seed) at the commit and consume / thicken / halve it.</summary>
    public sealed class Vol2FsPatch
    {
        public GameObject Go; public Body Body; public Vector3 Centre; public float Radius; public bool Enemy;
        /// <summary>The props (vines / slabs / blades / lumps) with their rest pose - a look may move them one by one.</summary>
        public readonly List<Transform> Props = new List<Transform>();
        public readonly List<Vector3> RestPos = new List<Vector3>();
        public readonly List<Quaternion> RestRot = new List<Quaternion>();
        public readonly List<Vector3> RestScale = new List<Vector3>();
        public readonly List<ParticleSystem> Ps = new List<ParticleSystem>();
        public Transform Pool, Form;
        /// <summary>The tallest part (m) - for looks that put things on top of the patch.</summary>
        public float Height = 0.5f;
        readonly List<Mesh> _own = new List<Mesh>();

        public static Vol2FsPatch Build(Vol2FsKit kit, Transform parent, Body body, Vector3 centre, float radius, long materialId, bool enemy)
        {
            var p = new Vol2FsPatch { Body = body, Centre = centre, Radius = radius, Enemy = enemy };
            p.Go = new GameObject("Patch_" + body + (enemy ? "_enemy" : "_mine"));
            if (parent != null) p.Go.transform.SetParent(parent, false);
            p.Go.transform.SetPositionAndRotation(centre, Quaternion.identity);
            int seed = (int)(materialId % 9973) + 17;
            var pool = Vol2FsB1.Prop(p.Go.transform, "Pool", Vol2FsB7.PoolMesh(kit, seed), Vol2FsB8.PoolMat(kit, body, enemy), false);
            pool.transform.localScale = new Vector3(radius * 1.05f, 1f, radius * 1.05f);
            p.Pool = pool.transform;
            var form = new GameObject("Form"); form.transform.SetParent(p.Go.transform, false); p.Form = form.transform;
            switch (body)
            {
                case Body.Wood:
                    {
                        var tubes = new Vol2FsTubes();
                        for (int k = 0; k < 7; k++)
                        {
                            float a0 = Vol2Fs.Hash(seed, k) * Mathf.PI * 2f, a1 = a0 + 1.4f + 1.6f * Vol2Fs.Hash(seed, k + 20);
                            float r0 = radius * (0.25f + 0.65f * Vol2Fs.Hash(seed, k + 40)), r1 = radius * (0.3f + 0.6f * Vol2Fs.Hash(seed, k + 60)), h = 0.38f + 0.45f * Vol2Fs.Hash(seed, k + 80);
                            Vector3 A = new Vector3(Mathf.Cos(a0) * r0, -0.03f, Mathf.Sin(a0) * r0), B = new Vector3(Mathf.Cos(a1) * r1, -0.03f, Mathf.Sin(a1) * r1);
                            Vector3 m = (A + B) * 0.5f + Vector3.up * h;
                            var tb = tubes.Add(new[] { A, Vector3.Lerp(A, m, 0.55f) + Vector3.up * h * 0.25f, m, Vector3.Lerp(m, B, 0.45f) + Vector3.up * h * 0.25f, B }, 0.065f + 0.03f * Vol2Fs.Hash(seed, k + 90), 0.025f, 18);
                            tb.Knots = 0.35f; tb.Sides = 6; tb.Grow = 1f; tb.Sharp = 0.05f;
                            p.Height = Mathf.Max(p.Height, h);
                        }
                        // thorns: short spikes along the arcs
                        var thorns = new Vol2FsTubes();
                        foreach (var tb in tubes.Tubes)
                            for (int j = 0; j < 4; j++)
                            {
                                Vector3 q = Vol2FsTubes.CatmullRom(tb.Ctrl, 0.15f + 0.22f * j);
                                Vector3 o = new Vector3(Vol2Fs.Hash(seed, j * 13 + 300) - 0.5f, 0.6f, Vol2Fs.Hash(seed, j * 17 + 400) - 0.5f).normalized;
                                var th = thorns.Add(new[] { q, q + o * 0.16f }, 0.024f, 0.002f, 3); th.Grow = 1f; th.Sharp = 0.08f; th.Sides = 4;
                            }
                        var bark = enemy ? kit.Bark(new Color(0.55f, 0.46f, 0.52f), Color.black, 0.15f, "fs_vine", "fs_vein") : kit.Bark(new Color(0.75f, 0.95f, 0.55f), new Color(0.3f, 1.1f, 0.3f) * 2f, 0.25f, "fs_vine", "fs_vein");
                        var tm = Vol2Fs.MeshObject(form.transform, "Vines", bark, out var mesh); tubes.Write(mesh); p._own.Add(mesh); tm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        Vol2Fs.MeshObject(form.transform, "Thorns", bark, out var mesh2); thorns.Write(mesh2); p._own.Add(mesh2);
                        var leaves = new Vol2FsLeaves();
                        for (int k = 0; k < 10; k++)
                        {
                            var tb = tubes.Tubes[k % tubes.Tubes.Count]; Vector3 q = Vol2FsTubes.CatmullRom(tb.Ctrl, 0.2f + 0.6f * Vol2Fs.Hash(seed, k + 500));
                            Vector3 n = new Vector3(Vol2Fs.Hash(seed, k + 520) - 0.5f, 0.3f, Vol2Fs.Hash(seed, k + 540) - 0.5f);
                            leaves.Items.Add(new Vol2FsLeaves.Leaf { Pos = q, Normal = n, Up = (Vector3.up + n).normalized, Size = 0.24f, Grow = 1f, Cell = k % 4 });
                        }
                        Vol2Fs.MeshObject(form.transform, "Leaves", enemy ? kit.Leaf(new Color(0.55f, 0.62f, 0.45f), Color.black) : kit.Leaf(new Color(0.55f, 1f, 0.5f), new Color(0.4f, 1f, 0.45f) * 0.6f), out var mesh3);
                        leaves.Write(mesh3); p._own.Add(mesh3);
                        break;
                    }
                case Body.Fire:
                    {
                        for (int k = 0; k < 5; k++)
                        {
                            float a = Vol2Fs.Hash(seed, k) * Mathf.PI * 2f, r = radius * 0.6f * Vol2Fs.Hash(seed, k + 10);
                            var lump = Vol2FsB1.Prop(form.transform, "Coal" + k, kit.SharedMesh("b8coal" + (k % 3), () => Vol2FsB1.Rock(820 + k % 3, 0.5f, 0.55f, 10, 7)),
                                Vol2FsB1.Lit(kit, "fs_rock_crack", new Color(0.09f, 0.07f, 0.07f), 0f, 0.1f, new Color(2.2f, 0.55f, 0.12f), "fs_rock_crack"), false);
                            lump.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0.02f, Mathf.Sin(a) * r); lump.transform.localScale = Vector3.one * (0.3f + 0.2f * Vol2Fs.Hash(seed, k + 20)) * radius;
                            lump.transform.localRotation = Quaternion.Euler(0f, Vol2Fs.Hash(seed, k + 30) * 360f, 0f);
                        }
                        var lick = Vol2FsPolish.Lick(enemy ? 1.15f : 1.4f, 0.55f, enemy ? 0.95f : 1.1f);
                        var flames = Vol2FsPolish.Licks(kit.Mat(lick), 0.34f, 0.62f, 0.5f, 0.9f, 1.0f, 260);
                        flames.Loop = true; flames.Rate = 48f * radius; flames.Shape = ParticleSystemShapeType.Sphere; flames.Radius = radius * 0.75f; flames.ShapeScale = new Vector3(1f, 0.05f, 1f); flames.World = false;
                        p.Ps.Add(Vol2Fs.Build(flames, form.transform, "Flames"));
                        var ember = M("fs_dot", new Color(1f, 0.5f, 0.18f), 3f, 1f);
                        p.Ps.Add(Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(ember), Loop = true, Rate = 20f, Life0 = 0.8f, Life1 = 1.6f, Speed0 = 0.2f, Speed1 = 0.8f, Size0 = 0.02f, Size1 = 0.045f, Radius = radius * 0.7f, ShapeScale = new Vector3(1f, 0.1f, 1f), Gravity = -0.25f, Noise = 0.8f, NoiseFreq = 1.4f, World = false, Max = 120 }, form.transform, "Embers"));
                        var soot = Vol2FsPolish.Soot(enemy ? 0.5f : 0.35f);
                        p.Ps.Add(Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(soot), Loop = true, Rate = 6f, Life0 = 1.4f, Life1 = 2.2f, Speed0 = 0.2f, Speed1 = 0.5f, Size0 = 0.35f, Size1 = 0.7f, Radius = radius * 0.5f, ShapeScale = new Vector3(1f, 0.1f, 1f), VelOverLife = new Vector3(0f, 0.45f, 0f), SizeStart = 0.6f, SizePeak = 1f, SizeEnd = 1.6f, World = false, Max = 40 }, form.transform, "Soot"));
                        foreach (var ps in p.Ps) { var tr = ps.transform; tr.localPosition = new Vector3(0f, 0.08f, 0f); ps.Play(false); }
                        p.Height = 0.7f;
                        break;
                    }
                case Body.Earth:
                    {
                        var mat = Vol2FsB1.Lit(kit, "fs_rock", enemy ? new Color(0.55f, 0.47f, 0.47f) : new Color(0.72f, 0.6f, 0.48f), 0f, 0.12f);
                        for (int k = 0; k < 6; k++)
                        {
                            float a = (k / 6f + 0.12f * Vol2Fs.Hash(seed, k)) * Mathf.PI * 2f, r = radius * (0.25f + 0.55f * Vol2Fs.Hash(seed, k + 10));
                            var size = new Vector3(0.4f + 0.25f * Vol2Fs.Hash(seed, k + 20), 0.55f + 0.5f * Vol2Fs.Hash(seed, k + 30), 0.24f + 0.14f * Vol2Fs.Hash(seed, k + 40));
                            int sk = (seed * 7 + k) % 23;
                            var slab = Vol2FsB1.Prop(form.transform, "Slab" + k, kit.SharedMesh("b8slab" + sk, () => Vol2FsB1.Slab(sk * 5 + 3, new Vector3(1f, 1f, 1f), 0.75f)), mat);
                            Vector3 outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                            slab.transform.localPosition = outw * r + Vector3.down * 0.05f;
                            slab.transform.localRotation = Quaternion.LookRotation(outw, Vector3.up) * Quaternion.Euler(-14f - 18f * Vol2Fs.Hash(seed, k + 50), 0f, (Vol2Fs.Hash(seed, k + 60) - 0.5f) * 30f);
                            slab.transform.localScale = size;
                            p.Height = Mathf.Max(p.Height, size.y);
                        }
                        for (int k = 0; k < 8; k++)
                        {
                            float a = Vol2Fs.Hash(seed, k + 200) * Mathf.PI * 2f, r = radius * Vol2Fs.Hash(seed, k + 210);
                            var pb = Vol2FsB1.Prop(form.transform, "Pebble" + k, kit.SharedMesh("b8peb" + (k % 3), () => Vol2FsB1.Rock(840 + k % 3, 0.5f)), mat, false);
                            pb.transform.localPosition = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r); pb.transform.localScale = Vector3.one * (0.08f + 0.1f * Vol2Fs.Hash(seed, k + 220));
                        }
                        break;
                    }
                case Body.Metal:
                    {
                        var mat = kit.Metal(enemy ? 0.85f : 1.1f, 0f, enemy ? new Color(0.01f, 0.006f, 0.014f) : Vol2FsRun.Accent(Body.Metal) * 0.06f, enemy ? new Color(0.8f, 0.76f, 0.86f) : Color.white) ?? Vol2FsB1.Lit(kit, "fs_steel", new Color(0.7f, 0.7f, 0.75f), 1f, 0.8f);
                        var blade = kit.SharedMesh("b8spike", () => Vol2FsB6.Spike(0.22f, 0.045f, 0.3f, 0.12f));
                        for (int k = 0; k < 8; k++)
                        {
                            float a = (k / 8f + 0.1f * Vol2Fs.Hash(seed, k)) * Mathf.PI * 2f, r = radius * (0.2f + 0.65f * Vol2Fs.Hash(seed, k + 10));
                            float h = 0.7f + 0.55f * Vol2Fs.Hash(seed, k + 20);
                            var b = Vol2FsB1.Prop(form.transform, "Blade" + k, blade, mat);
                            Vector3 outw = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                            b.transform.localPosition = outw * r + Vector3.down * 0.04f;
                            // the spike runs along +Y: lean it outward, turn it about its own axis
                            b.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up + outw * (0.25f + 0.3f * Vol2Fs.Hash(seed, k + 30))).normalized) * Quaternion.Euler(0f, Vol2Fs.Hash(seed, k + 40) * 180f, 0f);
                            b.transform.localScale = new Vector3(1f, h, 1f);
                            p.Height = Mathf.Max(p.Height, h);
                        }
                        break;
                    }
                default:
                    {
                        // water: the pool itself is the water (smooth, dark); a few slow rings and a rising drip
                        var rings = new Vol2FsStrips();
                        for (int k = 0; k < 3; k++) Vol2FsB4A.GroundArc(rings, Vector3.zero, 0.03f + 0.005f * k, radius * (0.35f + 0.22f * k), 0.035f, Vol2Fs.Hash(seed, k) * 6f, Mathf.PI * (1.2f + 0.5f * k), 0.85f, 0.3f * k, Vector3.up * 50f, 28);
                        var wm = Vol2FsB1.M("fs_ring", enemy ? new Color(0.6f, 0.72f, 0.9f) : new Color(0.7f, 0.92f, 1f), enemy ? 1.4f : 1.8f, 0.6f); wm.NoiseAmt = 0.2f; wm.Soft = 0.1f;
                        Vol2Fs.MeshObject(form.transform, "Ripples", kit.Mat(wm), out var rm); rings.Write(rm); p._own.Add(rm);
                        var drip = M("fs_dot", enemy ? new Color(0.45f, 0.55f, 0.7f) : new Color(0.6f, 0.9f, 1f), 1.8f, 0.6f);
                        p.Ps.Add(Vol2Fs.Build(new Vol2FsPs { Mat = kit.Mat(drip), Loop = true, Rate = 8f, Life0 = 0.6f, Life1 = 1.1f, Speed0 = 0.6f, Speed1 = 1.4f, Size0 = 0.025f, Size1 = 0.05f, Radius = radius * 0.6f, ShapeScale = new Vector3(1f, 0.05f, 1f), Gravity = 1.4f, World = false, Max = 40 }, form.transform, "Drips"));
                        foreach (var ps in p.Ps) ps.Play(false);
                        p.Height = 0.15f;
                        break;
                    }
            }
            foreach (Transform c in form.transform) { p.Props.Add(c); p.RestPos.Add(c.localPosition); p.RestRot.Add(c.localRotation); p.RestScale.Add(c.localScale); }
            return p;
        }

        static Vol2FsMat M(string tex, Color tint, float i, float a) => Vol2FsMat.Make(tex, tint, i, a);

        /// <summary>The whole form grown / squashed (y) and sunk into the ground (m); the pool spread (x z).</summary>
        public void Pose(float grow, float sink, float spread = 1f)
        {
            if (Form == null) return;
            Form.localScale = new Vector3(Mathf.Max(0.01f, spread), Mathf.Max(0.001f, grow), Mathf.Max(0.01f, spread));
            Form.localPosition = Vector3.down * Mathf.Max(0f, sink);
            if (Pool != null) Pool.localScale = new Vector3(Radius * 1.05f * spread, 1f, Radius * 1.05f * spread);
        }

        /// <summary>Stops the patch's own particles (flames, embers, drips): they die out.</summary>
        public void StopParticles() { foreach (var ps in Ps) if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

        public void SetActive(bool on) { if (Go != null) Go.SetActive(on); }

        /// <summary>The meshes this patch wrote (not the kit's shared ones).</summary>
        public void FreeMeshes(Vol2FsKit kit) { foreach (var m in _own) if (m != null && (kit == null || !kit.Owns(m))) Object.Destroy(m); _own.Clear(); }

        /// <summary>For a stand-in the caller owns (the capture's proxy): destroy everything.</summary>
        public void Destroy(Vol2FsKit kit) { FreeMeshes(kit); if (Go != null) Object.Destroy(Go); Go = null; }
    }

    /// <summary>The placeholder look of a cell (until the cell has its own): a burst of the acting accent at the contact, then the product as a plain mark -
    /// 비화 a thick ring, 막힘 a half ring, the child a ring of its body for its life, a slow / damage ring for its duration. Proves the facts chain.</summary>
    public class Vol2FsRxPlain : Vol2FsReact
    {
        Mesh _mesh; readonly Vol2FsStrips _s = new Vol2FsStrips();
        protected override void OnBuild()
        {
            var l = M("fs_streak", Color.white, 2.4f, 1f); l.NoiseAmt = 0.2f; l.Soft = 0.03f;
            Vol2Fs.MeshObject(Root.transform, "Plain", Kit.Mat(l), out _mesh);
            if (Patch != null && Kind == R.Kind.Block) Patch.Pose(0.6f, 0f, 0.8f);
            if (Patch != null && Kind == R.Kind.Same) Patch.Pose(1.35f, 0f, 1.12f);
        }
        protected override void OnLive(float t)
        {
            var cam = CamPos; _s.Clear();
            float burst = 1f - Vol2Fs.Smooth(0.15f, 0.9f, t);
            if (burst > 0.01f) Vol2FsB4A.GroundArc(_s, P, Ground + 0.04f, Radius * (0.6f + 0.9f * Vol2Fs.Smooth(0f, 0.3f, t)), 0.12f, 0f, Mathf.PI * 2f, burst, 0.2f, cam, 40);
            if (Patch != null && !ContactSurvives) { float k = Vol2Fs.Smooth(0.1f, 0.5f, t); Patch.Pose(1f - k, k * Patch.Height); if (t > 0.1f) Patch.StopParticles(); }
            float live = 0f;
            if (HasChild) live = ChildEndAt < 0f ? 1f : 1f - Vol2Fs.Smooth(ChildEndAt, ChildEndAt + 0.5f, t);
            else if (ContactSurvives) live = ContactEndAt < 0f ? 1f : 1f - Vol2Fs.Smooth(ContactEndAt, ContactEndAt + 0.5f, t);
            else if (Dur > 0f) live = 1f - Vol2Fs.Smooth(Dur, Dur + 0.4f, t);
            if (live > 0.01f)
            {
                float span = Kind == R.Kind.Block ? Mathf.PI : Mathf.PI * 2f;
                Vol2FsB4A.GroundArc(_s, P, Ground + 0.05f, Radius * 1.05f, Kind == R.Kind.Same ? 0.16f : 0.07f, 0f, span, live, 0.5f, cam, 40);
            }
            _s.Write(_mesh);
        }
    }

    /// <summary>Batch 8 helpers: the cell factory, the enemy patch stand-in for the lab capture, names.</summary>
    public static class Vol2FsB8
    {
        public static string Han(Body b) => b == Body.Wood ? "목" : b == Body.Fire ? "화" : b == Body.Earth ? "토" : b == Body.Metal ? "금" : "수";

        /// <summary>The look of a cell (acting -> contacted).</summary>
        public static Vol2FsReact Make(Body acting, Body contacted)
        {
            switch ((int)acting * 5 + (int)contacted)
            {
                case 0: return new Vol2FsRxWoodWood();      // 비화
                case 1: return new Vol2FsRxWoodFire();      // 먹임
                case 2: return new Vol2FsRxWoodEarth();     // 뿌리 붕괴 (꺾음, slow 2 s)
                case 3: return new Vol2FsRxWoodMetal();     // 잘림 (막힘)
                case 4: return new Vol2FsRxWoodWater();     // 옮겨붙음
                case 5: return new Vol2FsRxFireWood();      // 들불 (옮겨붙음)
                case 6: return new Vol2FsRxFireFire();      // 비화
                case 7: return new Vol2FsRxFireEarth();     // 먹임
                case 8: return new Vol2FsRxFireMetal();     // 달군 쇠 (꺾음, damage 2 s)
                case 9: return new Vol2FsRxFireWater();     // 꺼짐 (막힘)
                case 10: return new Vol2FsRxEarthWood();    // 걸림 (막힘)
                case 11: return new Vol2FsRxEarthFire();    // 옮겨붙음
                case 12: return new Vol2FsRxEarthEarth();   // 비화
                case 13: return new Vol2FsRxEarthMetal();   // 먹임
                case 14: return new Vol2FsRxEarthWater();   // 진창 (꺾음, slow 3 s)
                case 15: return new Vol2FsRxMetalWood();    // 가시 (꺾음, damage once)
                case 16: return new Vol2FsRxMetalFire();    // 무뎌짐 (막힘)
                case 17: return new Vol2FsRxMetalEarth();   // 옮겨붙음
                case 18: return new Vol2FsRxMetalMetal();   // 비화
                case 19: return new Vol2FsRxMetalWater();   // 먹임
                case 20: return new Vol2FsRxWaterWood();    // 먹임
                case 21: return new Vol2FsRxWaterFire();    // 김 (꺾음, damage once + 0.3 s cover)
                case 22: return new Vol2FsRxWaterEarth();   // 막힘 (막힘)
                case 23: return new Vol2FsRxWaterMetal();   // 옮겨붙음
                default: return new Vol2FsRxWaterWater();   // 비화
            }
        }

        /// <summary>The cell's name (the reaction table's).</summary>
        public static string CellName(Body acting, Body contacted)
        {
            switch ((int)acting * 5 + (int)contacted)
            {
                case 0: case 6: case 12: case 18: case 24: return "비화";
                case 1: case 7: case 13: case 19: case 20: return "먹임";
                case 4: case 11: case 17: case 23: return "옮겨붙음";
                case 5: return "들불";
                case 2: return "뿌리 붕괴";
                case 8: return "달군 쇠";
                case 14: return "진창";
                case 15: return "가시";
                case 21: return "김";
                case 3: return "잘림";
                case 9: return "꺼짐";
                case 10: return "걸림";
                case 16: return "무뎌짐";
                default: return "막힘";
            }
        }

        /// <summary>The patch's ground: an enemy's is a dark violet-black ink tinted by its body (no light: 오염은 빛나지 않는다), mine a lighter ink.</summary>
        public static Material PoolMat(Vol2FsKit kit, Body body, bool enemy) => kit.Custom("b8pool|" + body + "|" + enemy, () =>
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Fs_B8Pool_" + body };
            Color c;
            switch (body)
            {
                case Body.Wood: c = new Color(0.07f, 0.075f, 0.06f); break;
                case Body.Fire: c = new Color(0.07f, 0.035f, 0.03f); break;
                case Body.Earth: c = new Color(0.13f, 0.09f, 0.075f); break;
                case Body.Metal: c = new Color(0.075f, 0.07f, 0.085f); break;
                default: c = new Color(0.03f, 0.06f, 0.09f); break;
            }
            if (body == Body.Water) c = enemy ? new Color(0.12f, 0.15f, 0.22f) : new Color(0.16f, 0.3f, 0.36f); else if (enemy) c = Color.Lerp(c, new Color(0.11f, 0.05f, 0.13f), 0.45f); else c *= 1.8f;
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", body == Body.Water ? 0.93f : body == Body.Metal ? 0.55f : 0.35f);
            return m;
        });

        /// <summary>The lab's stand-in of an enemy patch of a body (LAB-SIMULATED: in the game the patch's own look stands there). Owner: the caller - destroy it with
        /// <see cref="Vol2FsPatch.Destroy"/>. The reaction look builds the same patch (same material id = same seed) at the commit.</summary>
        public static Vol2FsPatch PatchProxy(Vol2FsKit kit, Body body, Vector3 centre, float radius, long materialId, bool enemy = true)
        {
            var p = Vol2FsPatch.Build(kit, null, body, centre, radius, materialId, enemy);
            if (p.Go != null) p.Go.name = "Vol2VfxPatch_" + body + "_" + materialId;
            return p;
        }
    }
}
