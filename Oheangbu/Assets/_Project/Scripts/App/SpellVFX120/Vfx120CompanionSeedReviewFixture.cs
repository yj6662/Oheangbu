using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed class Vfx120CompanionSeedReviewFixture:MonoBehaviour
    {
        private Transform _primary;
        private Vfx120Effect _effect;
        private Material _material;
        public static Vector3 PrimaryPosition(float age)=>Vector3.Lerp(new Vector3(.3f,1.1f,.5f),new Vector3(.3f,1.1f,3.35f),Mathf.Clamp01((age-.4f)/1.05f));
        public static Vector3 ContactPosition(int index)=>new Vector3(.3f+(index==0?-.24f:.24f),1.075f,3.375f);
        public static GameObject Create(Vfx120Effect effect)
        {
            if(effect==null||!effect.PreviewControlled||!effect.DemonstrationCues)return null;
            var root=new GameObject("CompanionSeeds_ReviewPrimary"){hideFlags=HideFlags.DontSave};var f=root.AddComponent<Vfx120CompanionSeedReviewFixture>();f._effect=effect;
            var primary=GameObject.CreatePrimitive(PrimitiveType.Sphere);primary.name="ReviewPrimaryProjectile";primary.transform.SetParent(root.transform,false);primary.transform.localScale=Vector3.one*.09f;
            var col=primary.GetComponent<Collider>();col.enabled=false;DestroyImmediate(col);f._material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));f._material.SetColor("_BaseColor",new Color(.17f,.2f,.22f));primary.GetComponent<Renderer>().sharedMaterial=f._material;
            f._primary=primary.transform;effect.SetCompanionProjectile(f._primary,.4f,1.45f);f.Sample(0);return root;
        }
        public void Sample(float age)
        {
            _primary.SetPositionAndRotation(PrimaryPosition(age),Quaternion.identity);
            for(int i=0;i<2;i++){float at=1.45f+i*.16f;if(age>=at&&(_effect.CompanionHitMask&(1<<i))==0)_effect.ConfirmCompanionHit(i,ContactPosition(i),Vector3.back,at);}
        }
        private void OnDestroy(){if(_material!=null){if(Application.isPlaying)Destroy(_material);else DestroyImmediate(_material);}}
    }
}
