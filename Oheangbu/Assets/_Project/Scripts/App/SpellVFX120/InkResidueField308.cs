using Oheangbu.Data.Spell;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-DEPLOY-308 section 10 (D308-10 / D308-10b): one residue system for spell residue, airborne drops and the
    // player's footprints. Fixed-capacity ring buffers drawn with Graphics.RenderMeshInstanced (ground 1 call, air 1 call,
    // nothing when nothing is alive). Drying is computed by the shader from (born, life); the CPU only uploads the arrays
    // when a mark is added, released or lands. Per frame: at most RaysPerFrame ground rays and StampsPerFrame new marks,
    // the rest waits. Marks never grow, never glow and have no game effect (no enemy reads them, no ink is spent).
    // forms3 (D10): an airborne drop of the tailed drop cell (SpellDeploy308ProfileSO.AirTail) is drawn along the way it
    // travels - its instance matrix carries the direction and the drawn length, the shader lays the quad along it on the
    // screen. Where a drop flies and when it lands is unchanged; every other air sprite is the round quad it was.
    [DefaultExecutionOrder(960)]
    public sealed class InkResidueField308 : MonoBehaviour
    {
        public const int QueueCapacity = 192;
        private static readonly int NowId = Shader.PropertyToID("_OhResidueNow");
        private static readonly int StampAId = Shader.PropertyToID("_StampA");
        private static readonly int StampBId = Shader.PropertyToID("_StampB");
        private static readonly int StampCId = Shader.PropertyToID("_StampC");
        private static readonly int AtlasId = Shader.PropertyToID("_Atlas");
        private const float HeldForever = 1e9f;

        private SpellDeploy308ProfileSO _profile;
        private int _spellCap, _footCap, _airCap, _groundCap;
        private Mesh _quad;
        private Material _material;
        private MaterialPropertyBlock _groundBlock, _airBlock;
        private Matrix4x4[] _groundMatrices, _airMatrices;
        private Vector4[] _groundA, _groundB, _groundC, _airA, _airB, _airC;
        private int[] _groundOwner;
        private Vector3[] _airVelocity;
        private Vector2[] _airFall;                 // x gravity scale, y landing height
        private float[] _airSize;                   // the drop's own size (a tailed drop's matrix carries its drawn length instead)
        private float[] _airTailMax;                // pass 4b (Q6): a tailed drop's own ceiling on its drawn length, in sizes (0 = the profile's)
        private int _spellNext, _footNext, _airNext, _held;
        private float _groundUntil, _airUntil;
        private bool _groundDirty, _airDirty;
        private Bounds _bounds = new Bounds(Vector3.zero, Vector3.one * 400f);
        private readonly ResidueStamp308[] _queue = new ResidueStamp308[QueueCapacity];
        private int _queueHead, _queueCount;
        private int _pieceCursor;                   // pieces of the head stamp already laid (a big mark may take several frames)
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private bool _manualClock;
        private float _manualNow;
        private float Now => _manualClock ? _manualNow : Time.time;

        public bool Ready => _profile != null && _quad != null && (_material != null || _manualClock);
        /// <summary>Slot data for checks and the edit-mode preview (ground slots: spell ring first, then the foot ring).</summary>
        public int GroundSlots => _groundCap;
        public bool TryGetGround(int slot, out Matrix4x4 pose, out Vector4 a, out Vector4 b, out Vector4 c)
        {
            pose = Matrix4x4.identity; a = b = c = Vector4.zero;
            if (_groundA == null || slot < 0 || slot >= _groundCap || _groundA[slot].y <= 0f) return false;
            pose = _groundMatrices[slot]; a = _groundA[slot]; b = _groundB[slot]; c = _groundC[slot];
            return true;
        }
        public void UseManualClock(float now) { _manualClock = true; _manualNow = now; }
        public int Queued => _queueCount;
        public int RaysThisFrame { get; private set; }
        public int StampsThisFrame { get; private set; }
        public int DrawCallsThisFrame { get; private set; }
        public int DroppedNoGround { get; private set; }
        public int SpellCapacity => _spellCap;
        public int FootCapacity => _footCap;

        public void Configure(SpellDeploy308ProfileSO profile, DeployTier308 tierId)
        {
            _profile = profile;
            if (profile == null) return;
            var tier = profile.Tier(tierId);
            _spellCap = Mathf.Clamp(tier.SpellResidue, 8, 256); _footCap = Mathf.Clamp(tier.Footprints, 4, 64); _airCap = Mathf.Clamp(tier.AirDrops, 8, 256);
            _groundCap = _spellCap + _footCap;
            _material = profile.ResidueMaterial;
            if (_groundMatrices == null || _groundMatrices.Length != _groundCap)
            {
                _groundMatrices = new Matrix4x4[_groundCap]; _groundA = new Vector4[_groundCap]; _groundB = new Vector4[_groundCap]; _groundC = new Vector4[_groundCap];
                _groundOwner = new int[_groundCap];
            }
            if (_airMatrices == null || _airMatrices.Length != _airCap)
            {
                _airMatrices = new Matrix4x4[_airCap]; _airA = new Vector4[_airCap]; _airB = new Vector4[_airCap]; _airC = new Vector4[_airCap];
                _airVelocity = new Vector3[_airCap]; _airFall = new Vector2[_airCap]; _airSize = new float[_airCap]; _airTailMax = new float[_airCap];
            }
            for (int i = 0; i < _groundCap; i++) { _groundMatrices[i] = Matrix4x4.identity; _groundA[i] = _groundB[i] = Vector4.zero; _groundC[i] = new Vector4(0f, 0f, 1f, 1f); _groundOwner[i] = 0; }
            for (int i = 0; i < _airCap; i++) { _airMatrices[i] = Matrix4x4.identity; _airA[i] = Vector4.zero; _airB[i] = new Vector4(0f, 0f, 1f, 0f); _airC[i] = new Vector4(0f, 0f, 1f, 1f); }
            _spellNext = _footNext = _airNext = _held = 0; _groundUntil = _airUntil = float.NegativeInfinity; _queueHead = _queueCount = 0; _pieceCursor = 0;
            if (_quad == null) _quad = BuildQuad();
            if (_groundBlock == null) { _groundBlock = new MaterialPropertyBlock(); _airBlock = new MaterialPropertyBlock(); }
            var atlas = tier.UseMobileAtlas && profile.AtlasMobile != null ? profile.AtlasMobile : profile.Atlas;
            if (atlas != null) { _groundBlock.SetTexture(AtlasId, atlas); _airBlock.SetTexture(AtlasId, atlas); }
            _groundDirty = _airDirty = true;
        }

        /// <summary>A unit quad in the XZ plane (+Y up), uv 0..1: U across, V along +Z.</summary>
        public static Mesh BuildQuad()
        {
            var mesh = new Mesh { name = "InkResidue308_Quad", hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new[] { new Vector3(-.5f, 0f, -.5f), new Vector3(.5f, 0f, -.5f), new Vector3(-.5f, 0f, .5f), new Vector3(.5f, 0f, .5f) });
            mesh.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            mesh.SetIndices(new[] { 0, 2, 1, 1, 2, 3 }, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, .2f, 1f));
            return mesh;
        }

        /// <summary>Pose of a ground mark: lying in the tangent plane of `normal`, its length along `forward`, lifted off the surface.</summary>
        public static Matrix4x4 Place(Vector3 point, Vector3 normal, Vector3 forward, float width, float length, float lift)
        {
            normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 flat = Vector3.ProjectOnPlane(forward, normal);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.ProjectOnPlane(Vector3.right, normal);
            return Matrix4x4.TRS(point + normal * lift, Quaternion.LookRotation(flat.normalized, normal), new Vector3(width, 1f, length));
        }

        /// <summary>forms3 (D10): the instance matrix of an air sprite. A plain one: its place and its size. A tailed drop
        /// (`tail` says which cell): X = the direction it travels x the drawn length (the cell's head is at U 1), Z = across x
        /// the drawn width - InkResidue308.shader reads the two lengths and lays the quad along X as the camera sees it.
        /// Also used by the edit-mode previews, so that they draw what the field draws.</summary>
        public static Matrix4x4 AirPose(SpellDeploy308ProfileSO.AirTailSet tail, Vector3 point, Vector3 velocity, float size, int cell, out bool tailed, float tailMax = 0f)
        {
            size = Mathf.Max(.01f, size);
            tailed = tail != null && tail.Tailed(cell);
            if (!tailed) return Matrix4x4.TRS(point, Quaternion.identity, Vector3.one * size);
            float length = tail.Along(velocity, size, out Vector3 along, tailMax);   // pass 4b (Q6): tailMax > 0 = this drop's own ceiling
            Vector3 across = Vector3.Cross(along, Vector3.up);
            if (across.sqrMagnitude < 1e-6f) across = Vector3.Cross(along, Vector3.right);
            across.Normalize();
            var pose = Matrix4x4.identity;
            pose.SetColumn(0, along * length);
            pose.SetColumn(1, Vector3.Cross(across, along));
            pose.SetColumn(2, across * (size * Mathf.Clamp(tail.Width, .4f, 1f)));
            pose.SetColumn(3, new Vector4(point.x, point.y, point.z, 1f));
            return pose;
        }

        public void Stamp(in ResidueStamp308 stamp)
        {
            if (!Ready) return;
            if (_queueCount == QueueCapacity) { _queueHead = (_queueHead + 1) % QueueCapacity; _queueCount--; _pieceCursor = 0; }   // the oldest waiting mark gives way
            _queue[(_queueHead + _queueCount) % QueueCapacity] = stamp;
            _queueCount++;
        }

        public void ReleaseHeld(int owner)
        {
            if (owner == 0 || _groundOwner == null) return;
            float now = Now;
            for (int i = 0; i < _groundCap; i++)
            {
                if (_groundOwner[i] != owner) continue;
                _groundOwner[i] = 0;
                if (_groundB[i].x < HeldForever * .5f) continue;
                _groundB[i].x = now; _held = Mathf.Max(0, _held - 1);
                _groundUntil = Mathf.Max(_groundUntil, now + _groundA[i].y);
                _groundDirty = true;
            }
            // marks still waiting in the queue lose their hold too
            for (int k = 0; k < _queueCount; k++)
            {
                int q = (_queueHead + k) % QueueCapacity;
                if (_queue[q].Owner == owner) { _queue[q].Held = false; _queue[q].Owner = 0; }
            }
        }

        public void SpawnAir(Vector3 point, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY)
        {
            if (!Ready) return;
            int i = _airNext; _airNext = (_airNext + 1) % _airCap;
            float now = Now;
            _airMatrices[i] = AirPose(_profile.AirTail, point, velocity, size, cell, out bool tailed);
            _airA[i] = new Vector4(now, Mathf.Max(.05f, life), cell, 1f);
            _airB[i] = new Vector4(0f, 0f, 1f, tailed ? 1f : 0f);
            _airVelocity[i] = velocity; _airFall[i] = new Vector2(gravityScale, landY); _airSize[i] = Mathf.Max(.01f, size);
            _airTailMax[i] = 0f;
            _airUntil = Mathf.Max(_airUntil, now + life);
            _bounds.center = point;
            _airDirty = true;
        }

        /// <summary>pass 4b (Q6): an airborne tailed drop whose drawn length has a ceiling of its own (`tailMax` sizes; 0 = the
        /// profile's) - the presenter's thrown head. It is the same sprite in every other way.</summary>
        public void SpawnAir(Vector3 point, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY, float tailMax)
        {
            if (!Ready) return;
            int i = _airNext;
            SpawnAir(point, velocity, size, cell, life, gravityScale, landY);
            _airTailMax[i] = Mathf.Max(0f, tailMax);
            if (tailMax > 0f) _airMatrices[i] = AirPose(_profile.AirTail, point, velocity, size, cell, out _, tailMax);
        }

        public void Clear()
        {
            if (_groundA == null) return;
            for (int i = 0; i < _groundCap; i++) { _groundA[i] = Vector4.zero; _groundOwner[i] = 0; }
            for (int i = 0; i < _airCap; i++) _airA[i] = Vector4.zero;
            _held = 0; _queueCount = 0; _pieceCursor = 0; _groundUntil = _airUntil = float.NegativeInfinity; _groundDirty = _airDirty = true;
        }

        public void CountAlive(out int spell, out int foot, out int air)
        {
            spell = foot = air = 0;
            if (_groundA == null) return;
            float now = Now;
            for (int i = 0; i < _groundCap; i++)
            {
                var a = _groundA[i];
                if (a.y <= 0f || now < a.x || now > Mathf.Max(a.x, _groundB[i].x) + a.y) continue;
                if (i < _spellCap) spell++; else foot++;
            }
            for (int i = 0; i < _airCap; i++) if (_airA[i].y > 0f && now <= _airA[i].x + _airA[i].y) air++;
        }

        /// <summary>World pose of the newest footprint slot (checks).</summary>
        public bool TryGetNewestFoot(out Matrix4x4 pose, out float born)
        {
            pose = Matrix4x4.identity; born = 0f;
            if (_groundA == null || _footCap == 0) return false;
            int i = _spellCap + (_footNext + _footCap - 1) % _footCap;
            if (_groundA[i].y <= 0f) return false;
            pose = _groundMatrices[i]; born = _groundA[i].x; return true;
        }

        private void LateUpdate() { Step(Time.time, Time.deltaTime, true); }

        /// <summary>One frame of the field. The unit command steps it with its own clock (draw = false) in edit mode.</summary>
        public void Step(float now, float deltaTime, bool draw)
        {
            _manualClock = !draw; _manualNow = now;
            RaysThisFrame = StampsThisFrame = DrawCallsThisFrame = 0;
            if (!Ready) return;
            Shader.SetGlobalFloat(NowId, now);
            Drain(now);
            Fall(now, deltaTime);
            if (!draw) return;
            if (now <= _groundUntil || _held > 0)
            {
                if (_groundDirty) { _groundBlock.SetVectorArray(StampAId, _groundA); _groundBlock.SetVectorArray(StampBId, _groundB); _groundBlock.SetVectorArray(StampCId, _groundC); _groundDirty = false; }
                var rp = new RenderParams(_material) { matProps = _groundBlock, worldBounds = _bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
                Graphics.RenderMeshInstanced(rp, _quad, 0, _groundMatrices, _groundCap);
                DrawCallsThisFrame++;
            }
            if (now <= _airUntil)
            {
                if (_airDirty) { _airBlock.SetVectorArray(StampAId, _airA); _airBlock.SetVectorArray(StampBId, _airB); _airBlock.SetVectorArray(StampCId, _airC); _airDirty = false; }
                var rp = new RenderParams(_material) { matProps = _airBlock, worldBounds = _bounds, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
                Graphics.RenderMeshInstanced(rp, _quad, 0, _airMatrices, _airCap);
                DrawCallsThisFrame++;
            }
        }

        // lay queued marks inside the frame budget; a big mark is split into pieces of at most MaxPiece, each on its own ray
        private void Drain(float now)
        {
            var res = _profile.Residue;
            while (_queueCount > 0 && StampsThisFrame < res.StampsPerFrame)
            {
                ref ResidueStamp308 stamp = ref _queue[_queueHead];
                float size = Mathf.Max(stamp.Width, stamp.Length);
                int n = stamp.Foot || stamp.HasGround ? 1 : Mathf.Clamp(Mathf.CeilToInt(size / Mathf.Max(.1f, res.MaxPiece)), 1, 3);
                Vector3 forward = stamp.Forward.sqrMagnitude > 1e-6f ? stamp.Forward : Vector3.forward;
                Vector3 flat = new Vector3(forward.x, 0f, forward.z); flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, flat);
                // the ray budget is hard: a mark of several pieces goes on over as many frames as it needs (the cursor remembers)
                bool finished = true;
                for (int k = _pieceCursor; k < n * n; k++)
                {
                    if (!stamp.HasGround && RaysThisFrame >= res.RaysPerFrame) { _pieceCursor = k; finished = false; break; }
                    int ix = k % n, iy = k / n;
                    Vector3 point = stamp.Point + side * ((ix + .5f) / n - .5f) * stamp.Width + flat * ((iy + .5f) / n - .5f) * stamp.Length;
                    Vector3 normal = stamp.Normal;
                    if (!stamp.HasGround)
                    {
                        RaysThisFrame++;
                        if (!Ground(point, out point, out normal)) { DroppedNoGround++; continue; }
                    }
                    Write(stamp, point, normal, forward, stamp.Width / n, stamp.Length / n, new Vector4(ix / (float)n, iy / (float)n, 1f / n, 1f / n), now);
                }
                if (!finished) break;   // waits for the next frame
                _pieceCursor = 0;
                _queueHead = (_queueHead + 1) % QueueCapacity; _queueCount--;
                StampsThisFrame++;
            }
        }

        private bool Ground(Vector3 from, out Vector3 point, out Vector3 normal)
        {
            var res = _profile.Residue;
            point = from; normal = Vector3.up;
            // blocked layers (water) stop footprints and spell residue alike: ink leaves no mark on water
            int block = _profile.Foot.BlockLayers;
            int mask = res.GroundLayers | block;
            int count = Physics.RaycastNonAlloc(from + Vector3.up * .6f, Vector3.down, _hits, 4f, mask, QueryTriggerInteraction.Collide);
            float best = float.PositiveInfinity; int pick = -1;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                // bodies that move (the player, enemies, props with a rigidbody) are not ground: a mark on them would float when they leave
                if (hit.collider is CharacterController || hit.rigidbody != null) continue;
                bool blocks = ((1 << hit.collider.gameObject.layer) & block) != 0;
                if (hit.collider.isTrigger && !blocks) continue;
                if (hit.distance >= best) continue;
                best = hit.distance; pick = i;
            }
            if (pick < 0) return false;
            var chosen = _hits[pick];
            // water (or any blocked layer) above the ground: no mark. Walls: no residue.
            if (((1 << chosen.collider.gameObject.layer) & block) != 0) return false;
            if (chosen.normal.y < res.MinNormalY) return false;
            point = chosen.point; normal = chosen.normal;
            return true;
        }

        private void Write(in ResidueStamp308 stamp, Vector3 point, Vector3 normal, Vector3 forward, float width, float length, Vector4 part, float now)
        {
            int slot;
            if (stamp.Foot) { slot = _spellCap + _footNext; _footNext = (_footNext + 1) % _footCap; }
            else
            {
                // held marks are not pushed out: look for the oldest slot that is not held
                slot = -1;
                for (int tries = 0; tries < _spellCap; tries++)
                {
                    int candidate = _spellNext; _spellNext = (_spellNext + 1) % _spellCap;
                    if (_groundB[candidate].x >= HeldForever * .5f && _groundA[candidate].y > 0f) continue;
                    slot = candidate; break;
                }
                if (slot < 0) return;
            }
            float life = Mathf.Clamp(stamp.Life, SpellDeploy308ProfileSO.MinResidueLife, SpellDeploy308ProfileSO.MaxResidueLife);
            _groundMatrices[slot] = Place(point, normal, forward, width, length, _profile.Residue.Lift);
            _groundA[slot] = new Vector4(now, life, stamp.Cell, Mathf.Clamp01(stamp.Opacity));
            bool held = stamp.Held && !stamp.Foot && stamp.Owner != 0;
            _groundB[slot] = new Vector4(held ? HeldForever : 0f, stamp.FlipU ? 1f : 0f, 0f, 0f);
            _groundC[slot] = part;
            _groundOwner[slot] = held ? stamp.Owner : 0;
            if (held) _held++;
            _groundUntil = Mathf.Max(_groundUntil, now + life);
            _bounds.center = point;
            _groundDirty = true;
        }

        // airborne drops fall; the ones that reach their landing height become ground marks
        private void Fall(float now, float dt)
        {
            if (now > _airUntil) return;
            float g = _profile.Residue.Gravity;
            for (int i = 0; i < _airCap; i++)
            {
                var a = _airA[i];
                if (a.y <= 0f || now > a.x + a.y) continue;
                if (_airFall[i].x <= 0f) continue;
                var m = _airMatrices[i];
                Vector3 p = new Vector3(m.m03, m.m13, m.m23);
                _airVelocity[i].y -= g * _airFall[i].x * dt;
                p += _airVelocity[i] * dt;
                if (p.y <= _airFall[i].y)
                {
                    float size = _airSize[i] * 1.6f;
                    Stamp(new ResidueStamp308 { Point = new Vector3(p.x, _airFall[i].y + .3f, p.z), Forward = _airVelocity[i], Width = size, Length = size,
                        Cell = InkBurstMeshBuilder308.CellDrop, Opacity = _profile.Residue.Opacity, Life = _profile.SpellLife });
                    _airA[i] = Vector4.zero; _airDirty = true;
                    continue;
                }
                // a tailed drop turns with its velocity (the arc of its fall); a plain one only moves
                if (_airB[i].w > .5f) m = AirPose(_profile.AirTail, p, _airVelocity[i], _airSize[i], Mathf.RoundToInt(a.z), out _, _airTailMax[i]);
                else { m.m03 = p.x; m.m13 = p.y; m.m23 = p.z; }
                _airMatrices[i] = m;
            }
        }

        private void OnDisable() { Shader.SetGlobalFloat(NowId, 0f); }

        private void OnDestroy() { ReleaseResources(); }

        /// <summary>Destroys the quad mesh (also called by edit-mode tools, where OnDestroy does not run).</summary>
        public void ReleaseResources()
        {
            if (_quad == null) return;
            if (Application.isPlaying) Destroy(_quad); else DestroyImmediate(_quad);
            _quad = null;
        }

        // domain reload is off: the global clock must not carry over into the next Play session
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobals() { Shader.SetGlobalFloat(NowId, 0f); }
    }
}
