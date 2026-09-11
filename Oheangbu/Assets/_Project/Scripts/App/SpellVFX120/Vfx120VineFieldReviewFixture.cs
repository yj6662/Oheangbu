using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public static class Vfx120VineFieldReviewFixture
    {
        public static Vector3 Foot(int index,float ground)=>new Vector3(index==0?-.48f:.62f,ground,index==0?3.8f:4.42f);
        // Review cues only. No actor is found, moved or slowed by these callbacks.
        public static void Sample(Vfx120Effect effect,float age)
        {
            if(effect==null||!effect.PreviewControlled||!effect.DemonstrationCues||!effect.VineFieldConfigured)return;
            if(age>=1.6f&&effect.VineFieldContactCount==0)effect.SignalVineFootContact(Foot(0,effect.TargetGroundWorldY),1.6f);
            if(age>=2.45f&&effect.VineFieldContactCount==1)effect.SignalVineFootContact(Foot(1,effect.TargetGroundWorldY),2.45f);
            if(age>=3.5f&&effect.VineFieldReleaseAt<0)effect.ReleaseVineField(3.5f);
        }
    }
}
