using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // D308-84 (SPEC-SPELL-VFX-OVERHAUL-308 11.21): the ink left on the ground after a hit. Detached from the spell at the hit so
    // the spell's own 0.9 s life does not take it; removes itself after Life seconds (3 - 5, from its longest particle life).
    public sealed class Vfx308Residue : MonoBehaviour
    {
        public float Life = 4f;
        float age;

        void Update()
        {
            age += Time.deltaTime;
            if (age >= Life) Destroy(gameObject);
        }
    }
}
