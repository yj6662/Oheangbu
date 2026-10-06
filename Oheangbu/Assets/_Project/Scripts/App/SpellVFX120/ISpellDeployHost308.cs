using Oheangbu.Data.Spell;
using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    /// <summary>A ground mark (spell residue or footprint). The field finds the ground under Point within its per-frame budget.</summary>
    public struct ResidueStamp308
    {
        public Vector3 Point;        // world, anywhere above the ground it should land on
        public Vector3 Forward;      // world, the mark's length axis (flattened onto the ground)
        public float Width, Length;  // m
        public int Cell;             // atlas cell
        public float Opacity;
        public float Life;           // s, clamped to 3-5 by the field
        public bool Foot;            // footprint ring (its own cap) instead of the spell ring
        public bool FlipU;           // right foot
        public bool Held;            // stays wet until ReleaseHeld (field / install mark)
        public int Owner;            // id for ReleaseHeld (0 = none)
        public bool HasGround;       // Point / Normal are already on the ground: no ray
        public Vector3 Normal;
    }

    public struct ImpactRequest308
    {
        public Vector3 WorldPoint;
        public int Frames;           // 1..3
        public float Strength;       // 0..1
        public bool LocalOnly;       // ask for the local form (never a full-screen flip)
    }

    /// <summary>
    /// SPEC-SPELL-DEPLOY-308 section 1: what a deploy effect may ask of the scene. A runtime without a host (EA runtimes, the
    /// editor preview) draws its own burst and drops every request. Presentation only: no call here reaches judgement
    /// (TargetGroggy reads the judged target's state, it never changes it).
    /// </summary>
    public interface ISpellDeployHost308
    {
        DeployTier308 Tier { get; }
        /// <summary>Presentation-clock pause allowed on the burst's first cel (0 when reduced motion / flash off).</summary>
        float CutPause { get; }
        Camera ViewCamera { get; }
        /// <summary>Pooled burst buffer + mesh. False = pool exhausted (the caller builds nothing).</summary>
        bool RentBurst(InkDeployRuntime308 owner, out InkBurstBuffer308 buffer, out Mesh mesh);
        void ReturnBurst(InkDeployRuntime308 owner, InkBurstBuffer308 buffer, Mesh mesh);
        void StampResidue(in ResidueStamp308 stamp);
        void ReleaseHeld(int owner);
        /// <summary>A camera-facing atlas sprite in the air (falling drop, contact star). gravity 0 = it stays.</summary>
        void SpawnAir(Vector3 worldPoint, Vector3 velocity, float size, int cell, float life, float gravityScale, float landY);
        void RequestImpact(in ImpactRequest308 request);
        void NotifyDeployBegan(char letter);
        /// <summary>D308-10c, read only: is the judged target of a cast groggy right now? Single cast (plan null): `target` is
        /// groggy - its weak-point window is open, or the cast's own hit has just killed it while it was. Area cast: at least one
        /// target the plan scheduled a hit on is groggy. A cast asks once, on the burst's first cel; nothing is fed back.</summary>
        bool TargetGroggy(Transform target, AreaImpactPlan plan);
    }
}
