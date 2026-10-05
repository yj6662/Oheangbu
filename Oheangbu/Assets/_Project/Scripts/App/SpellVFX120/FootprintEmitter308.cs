using Oheangbu.App.World;
using Oheangbu.Combat;
using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // SPEC-SPELL-DEPLOY-308 section 10 (D308-10 "플레이어의 발자국에도 먹물 잉크 잔먹을 남긴다"): step events -> foot-shaped marks
    // in the shared residue field. The game is first person: the marks are seen looking down or back.
    //   primary : the rising edge of each leg's support phase in WorldMacroPlayerFootPlacement (read-only output)
    //   fallback: when the body is not drawn (the solve is skipped) - stride accumulation like the footstep sound
    // Neutral pale ink, no element tint, no pulse, never grows: it must not read as glow or as contamination.
    // No print while airborne, dodging / rolling, seated or riding, in water, or on a slope steeper than Foot.MaxSlopeDeg.
    [DefaultExecutionOrder(950)]   // after the foot solve (900), before the residue field (960)
    public sealed class FootprintEmitter308 : MonoBehaviour
    {
        private SpellDeploy308ProfileSO _profile;
        private InkResidueField308 _field;
        private WorldMacroPlayerFootPlacement _placement;
        private PlayerMotor _motor;
        private WorldMacroCombatWalker _walker;
        private Transform _body;
        private int _leftSerial, _rightSerial, _landSerial;
        private bool _primed, _fallbackLeft;
        private float _stride;
        private Vector3 _lastPosition;

        /// <summary>#308 forms2 S6: asked once per printed step (index 0, 1, ... until false) whether a buff mark goes beside the
        /// print. Set by the layer's director; null = no marks. Presentation only.</summary>
        public delegate bool MarkSource308(int index, out int cell, out float size);
        public MarkSource308 MarkSource;
        public int Marked { get; private set; }

        public int Emitted { get; private set; }
        public int EmittedFallback { get; private set; }
        public int Suppressed { get; private set; }
        public bool UsingFallback { get; private set; }

        public void Configure(SpellDeploy308ProfileSO profile, InkResidueField308 field, Transform body, PlayerMotor motor,
            WorldMacroPlayerFootPlacement placement, WorldMacroCombatWalker walker)
        {
            _profile = profile; _field = field; _body = body; _motor = motor; _placement = placement; _walker = walker;
            _primed = false; _stride = 0f;
        }

        /// <summary>Why no print would be laid right now (null = prints are laid).</summary>
        public string Blocked()
        {
            if (_profile == null || !_profile.LayerEnabled || !_profile.Foot.Enabled) return "off";
            if (_field == null || !_field.Ready || _body == null) return "not ready";
            if (_walker != null && _walker.Seated) return "seated / riding";
            if (_motor == null) return null;
            if (!_motor.isActiveAndEnabled) return "motor off";   // riding / seated: the walker switches the motor off
            if (!_motor.IsLocomotionGrounded) return "airborne";
            if (_motor.IsSitting) return "sitting";
            if (_motor.IsDodging) return "dodging";
            if (Vector3.Angle(_motor.GroundNormal, Vector3.up) > _profile.Foot.MaxSlopeDeg) return "slope";
            return null;
        }

        private void LateUpdate()
        {
            if (_profile == null || _body == null) return;
            Vector3 position = _body.position;
            if (!_primed) { Prime(position); return; }
            string blocked = Blocked();
            if (blocked != null)
            {
                // steps taken while blocked are forgotten, not replayed on landing
                if (blocked != "off" && blocked != "not ready") Suppressed++;
                Prime(position);
                return;
            }

            var foot = _profile.Foot;
            Vector3 travel = position - _lastPosition; travel.y = 0f;
            // toes follow the body facing (the motor root turns with the look yaw), not the travel direction:
            // a back-step or a strafe must not turn the prints round
            Vector3 heading = _body.forward; heading.y = 0f;
            if (heading.sqrMagnitude < 1e-4f) heading = travel;
            heading = heading.sqrMagnitude > 1e-6f ? heading.normalized : Vector3.forward;

            // a landing: both feet, darker, with a few drops
            if (_motor != null && _motor.LandingSerial != _landSerial)
            {
                _landSerial = _motor.LandingSerial;
                Vector3 side = Vector3.Cross(Vector3.up, heading);
                Print(position - side * foot.SideOffset, heading, true, Mathf.Min(1f, foot.Opacity * 1.5f));
                Print(position + side * foot.SideOffset, heading, false, Mathf.Min(1f, foot.Opacity * 1.5f));
                for (int i = 0; i < foot.LandingDrops; i++)
                {
                    float a = (i + .35f) / Mathf.Max(1, foot.LandingDrops) * Mathf.PI * 2f;
                    float size = _profile.Residue.DropSmall * 1.6f;
                    _field.Stamp(new ResidueStamp308 { Point = position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (foot.SideOffset * 2.6f) + Vector3.up * .3f,
                        Forward = heading, Width = size, Length = size, Cell = InkBurstMeshBuilder308.CellDrop, Opacity = foot.Opacity, Life = _profile.FootLife, Foot = true });
                }
                // the landing is its own mark: the support phases that begin on this frame are not printed a second time
                Prime(position);
                return;
            }

            bool primary = false;
            if (_placement != null)
            {
                if (_placement.TryGetSupport(true, out int ls, out Vector3 lp, out _))
                { primary = true; if (ls != _leftSerial) { _leftSerial = ls; Print(lp, heading, true, foot.Opacity); } }
                if (_placement.TryGetSupport(false, out int rs, out Vector3 rp, out _))
                { primary = true; if (rs != _rightSerial) { _rightSerial = rs; Print(rp, heading, false, foot.Opacity); } }
            }
            UsingFallback = !primary;
            if (!primary)
            {
                // the body is not drawn (or there is no solve): one print per stride, alternating sides
                _stride += travel.magnitude;
                float length = _motor != null && _motor.IsSprinting ? foot.StrideRun : foot.StrideWalk;
                if (_stride >= length)
                {
                    _stride -= length;
                    Vector3 side = Vector3.Cross(Vector3.up, heading);
                    Print(position + side * (_fallbackLeft ? -foot.SideOffset : foot.SideOffset), heading, _fallbackLeft, foot.Opacity);
                    _fallbackLeft = !_fallbackLeft; EmittedFallback++;
                }
            }
            _lastPosition = position;
        }

        private void Prime(Vector3 position)
        {
            _primed = true; _lastPosition = position; _stride = 0f;
            if (_motor != null) _landSerial = _motor.LandingSerial;
            if (_placement == null) return;
            if (_placement.TryGetSupport(true, out int ls, out _, out _)) _leftSerial = ls;
            if (_placement.TryGetSupport(false, out int rs, out _, out _)) _rightSerial = rs;
        }

        private void Print(Vector3 point, Vector3 heading, bool left, float opacity)
        {
            var foot = _profile.Foot;
            // toes point along the travel direction, turned out a little
            Vector3 forward = Quaternion.AngleAxis(left ? -foot.ToeOutDeg : foot.ToeOutDeg, Vector3.up) * heading;
            _field.Stamp(new ResidueStamp308
            {
                Point = point + Vector3.up * .25f, Forward = forward, Width = foot.Width, Length = foot.Length,
                Cell = (Emitted & 1) == 0 ? InkBurstMeshBuilder308.CellFoot : InkBurstMeshBuilder308.CellFootB,
                Opacity = opacity, Life = _profile.FootLife, Foot = true, FlipU = !left,
            });
            Emitted++;
            if (MarkSource == null) return;
            try
            {
                // a running buff leaves its mark beside the print: in the spell ring (never the footprint ring), same life as a mark
                Vector3 side = Vector3.Cross(Vector3.up, heading) * (left ? -1f : 1f);
                for (int i = 0; i < _profile.Buff.FootMarksAsked && MarkSource(i, out int cell, out float size); i++)
                {
                    _field.Stamp(new ResidueStamp308
                    {
                        Point = InkForms2Rules308.FootMarkPoint(_profile, point, side, foot.Width, i), Forward = forward, Width = size, Length = size, Cell = cell,
                        Opacity = opacity, Life = _profile.BuffMarkLife,
                    });
                    Marked++;
                }
            }
            catch (System.Exception e) { Debug.LogException(e, this); }
        }
    }
}
