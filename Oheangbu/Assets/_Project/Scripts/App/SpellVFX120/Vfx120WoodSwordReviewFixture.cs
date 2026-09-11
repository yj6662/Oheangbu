using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed class Vfx120WoodSwordReviewFixture:MonoBehaviour
    {
        private Transform _grip;
        private Material _material;
        private Vfx120Effect _effect;
        public static GameObject Create(Vfx120Effect effect)
        {
            if(effect==null||!effect.PreviewControlled||!effect.DemonstrationCues)return null;
            var root=new GameObject("WoodSword_ReviewGrip"){hideFlags=HideFlags.DontSave};
            var fixture=root.AddComponent<Vfx120WoodSwordReviewFixture>();fixture._effect=effect;
            fixture._grip=new GameObject("GripPose").transform;fixture._grip.SetParent(root.transform,false);
            fixture._material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));fixture._material.SetColor("_BaseColor",new Color(.2f,.22f,.21f));
            var hand=GameObject.CreatePrimitive(PrimitiveType.Sphere);hand.name="ReviewGripProxy";hand.transform.SetParent(fixture._grip,false);hand.transform.localPosition=new Vector3(0,-.07f,0);hand.transform.localScale=new Vector3(.09f,.14f,.08f);
            var col=hand.GetComponent<Collider>();col.enabled=false;DestroyImmediate(col);hand.GetComponent<Renderer>().sharedMaterial=fixture._material;
            effect.SetWoodSwordGrip(fixture._grip);fixture.Sample(0);return root;
        }
        private void OnDestroy(){if(_material!=null){if(Application.isPlaying)Destroy(_material);else DestroyImmediate(_material);}}
        public static bool SwingAt(float age)=>age>=.8f&&age<=1.45f;
        public static void GripPose(float age,out Vector3 position,out Quaternion rotation)
        {
            float stroke=Mathf.SmoothStep(0,1,Mathf.InverseLerp(.8f,1.45f,age));
            float recover=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.55f,2.15f,age));
            position=new Vector3(.35f-Mathf.Sin(stroke*Mathf.PI)*.18f,1.02f,.65f+Mathf.Sin(stroke*Mathf.PI)*.18f);
            rotation=Quaternion.Euler(18+Mathf.Sin(stroke*Mathf.PI)*36,0,Mathf.Lerp(Mathf.Lerp(32,-72,stroke),32,recover));
        }
        public void Sample(float age)
        {
            GripPose(age,out var p,out var q);_grip.SetPositionAndRotation(p,q);_effect.SetWoodSwordSwing(SwingAt(age));
            _effect.ClearWoodSwordPreviewTrace();
            for(int i=0;i<24;i++)
            {
                float t=age-.15f+i*.15f/23;
                if(t>=0&&SwingAt(t)){GripPose(t,out var tp,out var tq);_effect.RecordWoodSwordPreviewPose(tp,tq,t);}
            }
            if(age>=1.15f&&_effect.WoodSwordContactCount==0)
            {
                GripPose(1.15f,out var hp,out var hq);_effect.SetWoodSwordSwing(true);
                _effect.SignalWoodSwordContact(hp+hq*new Vector3(.03f,.85f,0),1.15f);_effect.SetWoodSwordSwing(SwingAt(age));
            }
        }
    }
}
