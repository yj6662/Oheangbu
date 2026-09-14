using UnityEngine;
namespace Oheangbu.App.Prologue
{
    [RequireComponent(typeof(Renderer))]
    public sealed class PrologueStreamTime:MonoBehaviour
    {
        Renderer surface;MaterialPropertyBlock properties;
        void Awake(){surface=GetComponent<Renderer>();properties=new MaterialPropertyBlock();}
        void LateUpdate(){surface.GetPropertyBlock(properties);properties.SetFloat("_EffectTime",Time.time);surface.SetPropertyBlock(properties);}
    }
}
