using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-VFX-OVERHAUL-308 11.41: the companion shot and the sword-form swing. Their wrap is a 3 s cast cue
    // (SpellCueLife308), but shots and swings keep coming for the whole form, so what draws them is kept here per handle,
    // apart from the wrap's own life. A profile without CompanionShot308Prefab / SwordStrike308Prefab never enters the table
    // (old behaviour). Presentation only: the rule's clocks, targets and damage are read, never changed.
    public sealed partial class Vfx120SpellPresenter308
    {
        sealed class CueBody
        {
            public SpellFxRequest Request;
            public GameObject Shot, Strike;
        }

        // nothing nearer the eye than this is drawn; a swing's near end is pushed out to StrikeNearMeters
        const float EyeClearMeters = .45f, StrikeNearMeters = .9f, StrikeLife = 1f;

        readonly Dictionary<int, CueBody> _cueBodies = new Dictionary<int, CueBody>();
        readonly Dictionary<long, int> _shots = new Dictionary<long, int>();
        readonly List<long> _staleShots = new List<long>();

        static long ShotKey(int handle, int index) => ((long)handle << 32) | (uint)index;

        void NoteCueBody308(int id, Live live)
        {
            var profile = live.Fx != null ? live.Fx.Profile : null;
            if (profile == null) return;
            GameObject shot = live.Request.Role == SpellFxRole.Companion ? profile.CompanionShot308Prefab : null;
            GameObject strike = live.Request.Role == SpellFxRole.Attached ? profile.SwordStrike308Prefab : null;
            if (shot == null && strike == null) return;
            _cueBodies[id] = new CueBody { Request = live.Request, Shot = shot, Strike = strike };
        }

        // true = the cue was drawn here and goes no further
        bool CueBody308(int id, SpellFxCue cue, in SpellFxCueArgs args)
        {
            if (!_cueBodies.TryGetValue(id, out var body)) return false;
            if (body.Shot != null)
            {
                // Secondary: one shot, At = its flight seconds from now. Hit: At is the absolute clock, so only the pair is told.
                if (cue == SpellFxCue.Secondary) { LaunchShot308(id, body, args); return true; }
                if (cue == SpellFxCue.Hit)
                {
                    if (_shots.TryGetValue(ShotKey(id, args.Index), out int shotId) && _live.TryGetValue(shotId, out var shot) && shot.Instance != null && shot.Fx != null)
                        shot.Fx.Signal(Vfx120Effect.Cue.Hit, args.Target);
                    return true;
                }
                return false;
            }
            // a swing: Hit / Miss carry the struck point; Expire / Consume / Return come with empty arguments and draw nothing
            if (cue != SpellFxCue.Hit && cue != SpellFxCue.Miss) return false;
            if (args.Point.sqrMagnitude > 1e-6f) Strike308(body, args.Point);
            return true;
        }

        void LaunchShot308(int handle, CueBody body, in SpellFxCueArgs args)
        {
            Sweep();
            var request = body.Request;
            Vector3 origin = (request.Follow != null ? request.Follow.position : request.Origin) + Vector3.up * .4f;
            var live = new Live { Request = request };
            try
            {
                var instance = Object.Instantiate(body.Shot);
                live.Instance = instance;
                instance.name = "Spell308_" + request.Letter + "_Shot" + args.Index;
                if (_owner != null && instance.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(instance, _owner.gameObject.scene);
                live.Sequence = instance.GetComponent<SpellSequenceEffect>();
                live.Fx = live.Sequence as Vfx120Effect;
                if (live.Fx == null || live.Fx.Profile == null) { Release(live); return; }
                live.Fx.PreviewControlled = false; live.Fx.DemonstrationCues = false;
                if (args.At > 0f) live.Fx.SetImpactClock(args.At);
                live.Fx.Begin(origin, args.Target, args.Point, live.Fx.Profile.Pigment);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                Release(live);
                return;
            }
            int id = ++_next; if (id == 0) id = ++_next;
            _live.Add(id, live);
            _shots[ShotKey(handle, args.Index)] = id;
        }

        void Strike308(CueBody body, Vector3 point)
        {
            var eye = Camera.main;
            Vector3 from = body.Request.Follow != null ? body.Request.Follow.position : body.Request.Origin;
            if (eye != null)
            {
                Vector3 view = point - eye.transform.position;
                float d = view.magnitude;
                if (d < EyeClearMeters) return;
                if (d < StrikeNearMeters) point = eye.transform.position + view / d * StrikeNearMeters;
            }
            Vector3 flat = point - from; flat.y = 0f;
            var rotation = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward, Vector3.up);
            var strike = Object.Instantiate(body.Strike, point, rotation);
            strike.name = "Spell308_" + body.Request.Letter + "_Strike";
            if (_owner != null && strike.scene != _owner.gameObject.scene) SceneManager.MoveGameObjectToScene(strike, _owner.gameObject.scene);
            if (!Application.isPlaying) return;   // an edit-mode caller owns what it made
            var residue = strike.GetComponent<Vfx308Residue>() ?? strike.AddComponent<Vfx308Residue>();
            residue.Life = StrikeLife;
        }

        void ForgetCueBody308(int id)
        {
            if (!_cueBodies.Remove(id)) return;
            _staleShots.Clear();
            foreach (var key in _shots.Keys) if ((int)(key >> 32) == id) _staleShots.Add(key);
            foreach (var key in _staleShots) _shots.Remove(key);
        }

        void ClearCueBodies308()
        {
            _cueBodies.Clear(); _shots.Clear();
        }
    }
}
