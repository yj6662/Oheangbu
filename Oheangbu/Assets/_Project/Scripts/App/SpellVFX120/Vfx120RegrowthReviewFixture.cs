using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    // Small ankle proxies establish scale; they are not the player mesh or a healing actor.
    public sealed class Vfx120RegrowthReviewFixture : MonoBehaviour
    {
        private Material _material;
        public static GameObject Create(Vfx120Effect effect)
        {
            if(effect==null || !effect.PreviewControlled || !effect.DemonstrationCues)return null;
            var root=new GameObject("Regrowth_ReviewFeet");root.hideFlags=HideFlags.DontSave;
            var fixture=root.AddComponent<Vfx120RegrowthReviewFixture>();
            fixture._material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fixture._material.SetColor("_BaseColor",new Color(.16f,.17f,.16f,1));
            for(int i=0;i<2;i++)
            {
                var foot=GameObject.CreatePrimitive(PrimitiveType.Sphere);foot.name="ReviewFoot_"+i;
                foot.transform.SetParent(root.transform,false);foot.transform.localPosition=new Vector3(i==0?-.09f:.09f,.035f,.03f);
                foot.transform.localScale=new Vector3(.10f,.07f,.23f);fixture.Prepare(foot);
                var ankle=GameObject.CreatePrimitive(PrimitiveType.Capsule);ankle.name="ReviewAnkle_"+i;
                ankle.transform.SetParent(root.transform,false);ankle.transform.localPosition=new Vector3(i==0?-.09f:.09f,.26f,0);
                ankle.transform.localScale=new Vector3(.085f,.24f,.085f);fixture.Prepare(ankle);
            }
            fixture.Sample(0);effect.SetRegrowthFootAnchor(root.transform);return root;
        }
        void Prepare(GameObject part)
        {
            var collider=part.GetComponent<Collider>();collider.enabled=false;DestroyImmediate(collider);
            var renderer=part.GetComponent<MeshRenderer>();renderer.sharedMaterial=_material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        public void Sample(float age)
        {
            float t=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.6f,2.4f,age));
            transform.position=new Vector3(t*.65f,0,0);transform.rotation=Quaternion.Euler(0,t*18,0);
        }
        private void OnDestroy()
        {if(_material!=null){if(Application.isPlaying)Destroy(_material);else DestroyImmediate(_material);}}
    }
}
