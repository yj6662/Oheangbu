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
        [Header("Gradient environment light (off keeps the flat ink ambient above)")]
        [Tooltip("#297: sky/equator/ground ambient for lit (URP) materials; the realm sky only tints its hue")]
        public bool GradientAmbient;
        public Color AmbientSky=new Color(.58f,.62f,.66f);
        public Color AmbientEquator=new Color(.46f,.47f,.45f);
        public Color AmbientGround=new Color(.23f,.22f,.20f);
        [Range(0,2)] public float GradientIntensity=1;
        [Tooltip("How far the regional zenith/horizon hue pulls the sky/equator ambient (brightness stays from the base colours)")]
        [Range(0,1)] public float RegionalTint=.35f;
        [Range(0,1)] public float GradientReflection=.5f;
        public void ApplyEnvironment()
        {
            if(GradientAmbient){ApplyGradient(AmbientSky,AmbientEquator,AmbientGround);return;}
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight=AmbientColor;RenderSettings.ambientIntensity=AmbientIntensity;
            RenderSettings.reflectionIntensity=ReflectionIntensity;
        }
        /// <summary>Gradient ambient following the blended regional sky. Without GradientAmbient this is the flat ink ambient.</summary>
        public void ApplyEnvironment(Color zenith,Color horizon)
        {
            if(!GradientAmbient){ApplyEnvironment();return;}
            ApplyGradient(Tinted(AmbientSky,zenith),Tinted(AmbientEquator,horizon),AmbientGround);
        }
        Color Tinted(Color baseColor,Color sky)
        {
            float l=.2126f*sky.r+.7152f*sky.g+.0722f*sky.b;if(l<1e-4f)return baseColor;
            return new Color(baseColor.r*Mathf.Lerp(1,sky.r/l,RegionalTint),baseColor.g*Mathf.Lerp(1,sky.g/l,RegionalTint),baseColor.b*Mathf.Lerp(1,sky.b/l,RegionalTint),1);
        }
        void ApplyGradient(Color sky,Color equator,Color ground)
        {
            // Measured (#297): with this scene's lighting data neither the trilight colours nor DynamicGI.UpdateEnvironment
            // reach the ambient probe shaders sample, so the gradient is written straight into the probe (custom mode).
            RenderSettings.ambientSkyColor=sky*GradientIntensity;RenderSettings.ambientEquatorColor=equator*GradientIntensity;
            RenderSettings.ambientGroundColor=ground*GradientIntensity;RenderSettings.ambientIntensity=1;
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Custom;
            RenderSettings.ambientProbe=GradientProbe((sky*GradientIntensity).linear,(equator*GradientIntensity).linear,(ground*GradientIntensity).linear);
            RenderSettings.reflectionIntensity=GradientReflection;
        }

        static Vector3[] _shDirections;static float[,] _shSolve;
        /// <summary>L2 probe whose evaluation is sky facing up, equator facing sideways, ground facing down (least squares over 64 directions).</summary>
        public static UnityEngine.Rendering.SphericalHarmonicsL2 GradientProbe(Color sky,Color equator,Color ground)
        {
            if(_shDirections==null)
            {
                const int n=64;var dirs=new Vector3[n];
                for(int k=0;k<n;k++){float y=1-2*(k+.5f)/n,r=Mathf.Sqrt(Mathf.Max(0,1-y*y)),a=k*2.39996323f;dirs[k]=new Vector3(Mathf.Cos(a)*r,y,Mathf.Sin(a)*r);}
                var basis=new float[n,9];var values=new Color[n];
                for(int i=0;i<9;i++){var unit=new UnityEngine.Rendering.SphericalHarmonicsL2();unit[0,i]=1;unit.Evaluate(dirs,values);for(int k=0;k<n;k++)basis[k,i]=values[k].r;}
                // normal equations (B^T B) c = B^T f solved once: _shSolve = (B^T B)^-1 B^T (9 x n)
                var m=new double[9,18];
                for(int i=0;i<9;i++){for(int j=0;j<9;j++){double s=0;for(int k=0;k<n;k++)s+=basis[k,i]*basis[k,j];m[i,j]=s;}m[i,9+i]=1;}
                for(int c=0;c<9;c++)
                {
                    int p=c;for(int r=c+1;r<9;r++)if(System.Math.Abs(m[r,c])>System.Math.Abs(m[p,c]))p=r;
                    for(int j=0;j<18;j++){double t=m[c,j];m[c,j]=m[p,j];m[p,j]=t;}
                    double d=m[c,c];if(System.Math.Abs(d)<1e-12)d=1e-12;for(int j=0;j<18;j++)m[c,j]/=d;
                    for(int r=0;r<9;r++){if(r==c)continue;double f=m[r,c];if(f==0)continue;for(int j=0;j<18;j++)m[r,j]-=f*m[c,j];}
                }
                _shSolve=new float[9,n];
                for(int i=0;i<9;i++)for(int k=0;k<n;k++){double s=0;for(int j=0;j<9;j++)s+=m[i,9+j]*basis[k,j];_shSolve[i,k]=(float)s;}
                _shDirections=dirs;
            }
            var sh=new UnityEngine.Rendering.SphericalHarmonicsL2();int count=_shDirections.Length;
            for(int k=0;k<count;k++)
            {
                float y=_shDirections[k].y;Color target=y>=0?Color.Lerp(equator,sky,y):Color.Lerp(equator,ground,-y);
                for(int i=0;i<9;i++){float w=_shSolve[i,k];sh[0,i]+=w*target.r;sh[1,i]+=w*target.g;sh[2,i]+=w*target.b;}
            }
            return sh;
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
