using UnityEngine;
namespace Oheangbu.Data.World
{
    [CreateAssetMenu(menuName="Oheangbu/World/Ink Sky Profile")]
    public sealed class InkSkyProfile : ScriptableObject
    {
        public Color Horizon = new Color(.90f,.88f,.83f);
        public Color Zenith = new Color(.73f,.75f,.72f);
        public Color Cloud = new Color(.55f,.58f,.55f);
        [Range(0,.5f)] public float CloudDensity=.14f;
        public float CloudScale=3.2f;
        public float CloudSpeed=.002f;
        public float CapitalAzimuth=15f;
        [Range(0,.08f)] public float CapitalAtmosphere=.018f;
        public Color AmbientColor=Color.black;
        [Range(0,1)] public float AmbientIntensity=0;
        [Range(0,1)] public float ReflectionIntensity=0;
        public void ApplyEnvironment()
        {
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=AmbientColor;RenderSettings.ambientIntensity=AmbientIntensity;
            RenderSettings.reflectionIntensity=ReflectionIntensity;
        }
        public void Apply(Material material)
        {
            if(material==null)return;
            material.SetColor("_Horizon",Horizon); material.SetColor("_Zenith",Zenith); material.SetColor("_Cloud",Cloud);
            material.SetFloat("_CloudDensity",CloudDensity);material.SetFloat("_CloudScale",CloudScale);
            material.SetFloat("_CloudSpeed",CloudSpeed);material.SetFloat("_CapitalAzimuth",CapitalAzimuth*Mathf.Deg2Rad);
            material.SetFloat("_CapitalAtmosphere",CapitalAtmosphere);
        }
    }
}
