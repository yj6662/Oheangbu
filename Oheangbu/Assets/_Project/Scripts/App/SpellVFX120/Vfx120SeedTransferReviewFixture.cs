using UnityEngine;
namespace Oheangbu.App.SpellVFX120
{
    public static class Vfx120SeedTransferReviewFixture
    {
        public static readonly Vector3 Recipient=new Vector3(-1.1f,1.1f,4.9f);
        public static void Sample(Vfx120Effect e,float age)
        {
            if(e==null||!e.PreviewControlled||!e.DemonstrationCues||!e.SeedTransferConfigured)return;
            if(age>=1.3f&&e.SeedTransferStartedAt<0)e.RequestSeedTransfer(null,Recipient,1.3f);
            if(age>=1.85f&&e.SeedTransferHitAt<0)e.ConfirmSeedTransferHit(Recipient,1.85f);
        }
    }
}
