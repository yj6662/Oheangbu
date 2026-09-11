using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed class Vfx120BloomReviewFixture:MonoBehaviour
    {
        private Material _material;
        public static GameObject Create(Vfx120Effect effect)
        {
            if(effect==null||!effect.PreviewControlled||!effect.DemonstrationCues)return null;
            var root=new GameObject("Bloom_ReviewReceiver"){hideFlags=HideFlags.DontSave};
            var fixture=root.AddComponent<Vfx120BloomReviewFixture>();
            fixture._material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            fixture._material.SetColor("_BaseColor",new Color(.2f,.22f,.21f));
            var body=GameObject.CreatePrimitive(PrimitiveType.Capsule);body.name="ReviewReceiverBody";body.transform.SetParent(root.transform,false);
            body.transform.localPosition=new Vector3(0,.85f,0);body.transform.localScale=new Vector3(.42f,.85f,.32f);
            var collider=body.GetComponent<Collider>();collider.enabled=false;DestroyImmediate(collider);
            body.GetComponent<Renderer>().sharedMaterial=fixture._material;
            body.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            var torso=new GameObject("ReviewTorso");torso.transform.SetParent(root.transform,false);torso.transform.localPosition=new Vector3(0,1,0);
            effect.SetBloomReceiver(torso.transform);return root;
        }
        private void OnDestroy(){if(_material!=null){if(Application.isPlaying)Destroy(_material);else DestroyImmediate(_material);}}
    }
}
