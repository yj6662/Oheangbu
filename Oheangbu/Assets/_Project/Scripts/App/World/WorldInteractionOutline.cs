using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Oheangbu.App.World {
 // Private renderer shells preserve original materials, animation and scene depth.
 public sealed class WorldInteractionOutline : System.IDisposable {
 readonly List<(Renderer source,Renderer shell,Material material)> shells=new();
 Renderer[] sources; Shader shader;
 public int ActiveCount {get;private set;}
 public void Show(Renderer[] next,WorldMacroHudSkinProfileSO skin){
 if(!ReferenceEquals(sources,next)){Clear();sources=next;
 shader=Resources.Load<Shader>("Interaction/FocusOutline");
 if(shader==null)return;
 foreach(var source in next)for(int pass=0;pass<3;pass++){
 if(source==null)continue;
 var skinned=source as SkinnedMeshRenderer;var filter=source.GetComponent<MeshFilter>();
 Mesh mesh=skinned!=null?skinned.sharedMesh:filter!=null?filter.sharedMesh:null;if(mesh==null)continue;
 var go=new GameObject("FocusedOutline"){hideFlags=HideFlags.DontSave,layer=source.gameObject.layer};go.transform.SetParent(source.transform,false);
 Renderer shell;
 if(skinned!=null){var r=go.AddComponent<SkinnedMeshRenderer>();r.sharedMesh=mesh;r.bones=skinned.bones;r.rootBone=skinned.rootBone;r.localBounds=skinned.localBounds;r.updateWhenOffscreen=false;shell=r;}
 else{go.AddComponent<MeshFilter>().sharedMesh=mesh;shell=go.AddComponent<MeshRenderer>();}
 var material=new Material(pass==1?shader:Resources.Load<Shader>("Interaction/FocusMask")){hideFlags=HideFlags.DontSave};
 material.renderQueue=2977+pass;
 if(pass!=1)material.SetFloat("_StencilOp",pass==0?2:1);
 var materials=new Material[mesh.subMeshCount];for(int i=0;i<materials.Length;i++)materials[i]=material;shell.sharedMaterials=materials;
 shell.shadowCastingMode=ShadowCastingMode.Off;shell.receiveShadows=false;shell.lightProbeUsage=LightProbeUsage.Off;shell.reflectionProbeUsage=ReflectionProbeUsage.Off;
 shells.Add((source,shell,material));
 }}
 ActiveCount=0;
 foreach(var item in shells){bool visible=item.source!=null&&item.source.enabled&&item.source.gameObject.activeInHierarchy;item.shell.enabled=visible;
 if(item.material.shader==shader){if(visible)ActiveCount++;
 item.material.SetColor("_OutlineColor",skin.InteractionOutlineColor);item.material.SetFloat("_Width",skin.InteractionOutlinePixels);
 if(item.source!=null){item.material.SetFloat("_Radial",item.source is SkinnedMeshRenderer?0:1);item.material.SetVector("_CenterWS",item.source.bounds.center);}}}
 }
 public void Hide(){foreach(var item in shells)if(item.shell!=null)item.shell.enabled=false;ActiveCount=0;}
 public void Clear(){foreach(var item in shells){if(item.shell!=null)Object.Destroy(item.shell.gameObject);if(item.material!=null)Object.Destroy(item.material);}shells.Clear();sources=null;ActiveCount=0;}
 public void Dispose()=>Clear();
 }
}
