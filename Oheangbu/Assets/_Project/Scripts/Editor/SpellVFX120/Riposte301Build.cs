namespace Oheangbu.EditorTools.SpellVFX120
{
    // D301 앞잡: the glyph atlas / ring / orb / splash builder of the 48-glyph volley. LEGACY since D308-148 (앞잡 = 일필휘지 한 획):
    // it would wire the old glyph, circle and orb materials back into Resources/Riposte301Profile.asset, so it refuses and points at the
    // stroke's builder. Nothing else called it. The previous body is in Art/Backup308/20261024_riposte_stroke/ (and in git).
    public static class Riposte301Build
    {
        public static string Run(string command)
        {
            throw new System.InvalidOperationException("Riposte301Build is LEGACY since D308-148 (the riposte is one stroke now): use "
                + "Oheangbu.EditorTools.SpellVFX120.RiposteStroke308Build Run \"build\". It does not touch the old look.");
        }
    }
}
