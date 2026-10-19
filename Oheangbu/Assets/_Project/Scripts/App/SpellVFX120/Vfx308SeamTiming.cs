using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Spec 11.34 fire18: per-graph timing for the area seam (Vfx120Effect.AreaRemake.SampleAreaGraphs). Display only - no rule,
    // clock, hit or power reads it. A graph host without this component keeps the shared window, the caster's ground point
    // and the brush-tip lift (Lead 0, Forward 0, Height -1).
    //   Lead     seconds added to the area clock before the emit-window test (+ = on earlier, - = later), relative to the plan's Delay
    //   Forward  metres along the cast written into Initial Position z
    //   Height   metres above the ground point written into Initial Position y; below 0 = the brush-tip lift
    public sealed class Vfx308SeamTiming : MonoBehaviour
    {
        public float Lead;
        public float Forward;
        public float Height = -1f;
    }
}
