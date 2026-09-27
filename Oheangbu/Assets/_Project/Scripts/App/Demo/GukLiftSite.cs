using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.Demo
{
    // An authored vertical opportunity, never a global increase to the spell's reach.
    public sealed class GukLiftSite : MonoBehaviour
    {
        public string Id;
        public Transform Lower,Upper;
        [Range(.2f,1)] public float CastRadius=.65f;
        [Range(4,6)] public float RiseSeconds=5;
        static readonly HashSet<GukLiftSite> sites=new HashSet<GukLiftSite>();
        void OnEnable()=>sites.Add(this);
        void OnDisable()=>sites.Remove(this);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSites()=>sites.Clear();
        public float Height=>Upper!=null&&Lower!=null?Upper.position.y-Lower.position.y:0;
        public bool Valid=>!string.IsNullOrWhiteSpace(Id)&&Lower!=null&&Upper!=null&&Height>=8&&Height<=24&&
            float.IsFinite(Height)&&CastRadius>=.2f&&CastRadius<=1&&RiseSeconds>=4&&RiseSeconds<=6&&
            Vector2.Distance(new Vector2(Lower.position.x,Lower.position.z),new Vector2(Upper.position.x,Upper.position.z))<=2;
        public bool Contains(Vector3 feet)=>Lower!=null&&Mathf.Abs(feet.y-Lower.position.y)<.2f&&
            Vector2.Distance(new Vector2(feet.x,feet.z),new Vector2(Lower.position.x,Lower.position.z))<=CastRadius;
        public static bool Resolve(Scene scene,Vector3 feet,out GukLiftSite site)
        {
            site=null;
            foreach(var candidate in sites)
            {
                if(candidate==null||!candidate.isActiveAndEnabled||candidate.gameObject.scene!=scene||!candidate.Contains(feet))continue;
                if(!candidate.Valid||site!=null){site=null;return false;} // Ambiguity/invalid authored nodes fail closed.
                site=candidate;
            }
            return true;
        }
    }
}
