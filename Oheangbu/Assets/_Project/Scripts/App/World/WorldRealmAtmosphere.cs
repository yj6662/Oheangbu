using UnityEngine;
namespace Oheangbu.App.World
{
 // Camera-local sky override: never recolours terrain, lights or shared sky assets.
 [DisallowMultipleComponent]
 public sealed class WorldRealmAtmosphere:MonoBehaviour
 {
  public WorldLocationCatalog Catalog;public Camera View;public Material SkySource;
  public float TintStrength=.055f,ResponseSeconds=6;
  Material instance,prior;Skybox sky;Color horizon,zenith,cloud;Color current;bool initialized;CameraClearFlags priorFlags;
  void OnEnable()
  {
   if(View==null||SkySource==null||Catalog==null)return;
   sky=View.GetComponent<Skybox>();if(sky==null)sky=View.gameObject.AddComponent<Skybox>();prior=sky.material;priorFlags=View.clearFlags;View.clearFlags=CameraClearFlags.Skybox;
   instance=new Material(SkySource){name="Realm sky transient",hideFlags=HideFlags.DontSave};
   horizon=SkySource.GetColor("_Horizon");zenith=SkySource.GetColor("_Zenith");cloud=SkySource.GetColor("_Cloud");sky.material=instance;
   ApplyAt(View.transform.position,0,true);
  }
  public static Color Tint(Color basis,Color ink,float strength)=>Color.Lerp(basis,ink,Mathf.Clamp(strength,0,.12f));
  public void ApplyAt(Vector3 position,float dt,bool snap=false)
  {
   if(instance==null)return;var realm=Catalog.RealmAt(position);var target=WorldLocationCatalog.RealmInk(realm?.Id);
   current=snap||!initialized?target:Color.Lerp(current,target,1-Mathf.Exp(-Mathf.Max(0,dt)/Mathf.Max(.1f,ResponseSeconds)));initialized=true;
   instance.SetColor("_Horizon",Tint(horizon,current,TintStrength*.6f));instance.SetColor("_Zenith",Tint(zenith,current,TintStrength));
   instance.SetColor("_Cloud",Tint(cloud,current,TintStrength*.4f));
  }
  void LateUpdate(){if(View!=null)ApplyAt(View.transform.position,Time.deltaTime);}
  void OnDisable(){if(sky!=null&&sky.material==instance){sky.material=prior;if(View!=null)View.clearFlags=priorFlags;}if(instance!=null){if(Application.isPlaying)Destroy(instance);else DestroyImmediate(instance);}instance=null;initialized=false;}
 }
}
