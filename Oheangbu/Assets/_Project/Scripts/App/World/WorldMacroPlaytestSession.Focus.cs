using System;
using System.Linq;
using UnityEngine;
namespace Oheangbu.App.World {
 public sealed partial class WorldMacroPlaytestSession {
 [Serializable] public sealed class InteractionVisual { public string Id; public Renderer[] Renderers; }
 public InteractionVisual[] InteractionVisuals=Array.Empty<InteractionVisual>();
 GameObject cachedDynamicTarget;Renderer[] cachedDynamicRenderers;
 Renderer[] DynamicRenderers(GameObject target){if(cachedDynamicTarget!=target){cachedDynamicTarget=target;cachedDynamicRenderers=target.GetComponentsInChildren<Renderer>().Where(r=>r.name!="FocusedOutline").ToArray();}return cachedDynamicRenderers;}
 public Renderer[] FocusedRenderers {
 get {
 if(FocusedId=="CurrencyDrop"&&drop!=null)return DynamicRenderers(drop);
 if(!string.IsNullOrEmpty(FocusedCollectionBundleId)){
 var pickup=FindFragmentPickup(FocusedCollectionBundleId);   // #307 item 12: no capture per HUD frame
 return pickup!=null?DynamicRenderers(pickup.gameObject):Array.Empty<Renderer>();
 }
 foreach(var binding in InteractionVisuals)if(binding.Id==FocusedId)return binding.Renderers;
 return Array.Empty<Renderer>();
 }}
 public bool TryGetFocusedInteractionBounds(out Bounds bounds){
 bounds=default;if(!TryGetFocusedInteractionPosition(out _))return false;
 bool found=false;
 foreach(var r in FocusedRenderers){if(r==null||!r.enabled||!r.gameObject.activeInHierarchy)continue;
 if(!found){bounds=r.bounds;found=true;}else bounds.Encapsulate(r.bounds);}
 return found;
 }
 }
}
