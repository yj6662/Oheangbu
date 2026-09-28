using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.App.World
{
    // Only scenes carrying this component opt into mountain restrictions.
    [DisallowMultipleComponent]
    public sealed class CompactMountainAccess : MonoBehaviour
    {
        public CompactWorldLayoutSO Layout;
        static readonly HashSet<CompactMountainAccess> instances=new HashSet<CompactMountainAccess>();
        void OnEnable()=>instances.Add(this);
        void OnDisable()=>instances.Remove(this);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstances()=>instances.Clear();
        public static bool VehicleAllowed(Scene scene,Vector3 position,float margin=0)
        {
            if(!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z))return false;
            foreach(var instance in instances)
            {
                if(instance==null||!instance.isActiveAndEnabled||instance.gameObject.scene!=scene||instance.Layout==null)continue;
                foreach(var mountain in instance.Layout.Mountains)
                    foreach(var volume in mountain.VehicleExclusions)
                        if(volume.Contains(position,margin))return false;
            }
            return true;
        }
        public static bool VehicleSegmentAllowed(Scene scene,Vector3 from,Vector3 to,float margin)
        {
            // Analytic segment/slab intersection cannot tunnel through narrow entrances at speed.
            foreach(var instance in instances)
            {
                if(instance==null||!instance.isActiveAndEnabled||instance.gameObject.scene!=scene||instance.Layout==null)continue;
                foreach(var mountain in instance.Layout.Mountains)
                    foreach(var volume in mountain.VehicleExclusions)
                        if(Intersects(volume,from,to,margin))return false;
            }
            return true;
        }
        public static bool Intersects(CompactWorldLayoutSO.VehicleExclusion volume,Vector3 from,Vector3 to,float margin)
        {
            var half=volume.Size*.5f+Vector3.one*margin;var a=from-volume.Centre;var d=to-from;
            float near=0,far=1;
            for(int axis=0;axis<3;axis++)
            {
                if(Mathf.Abs(d[axis])<.00001f){if(Mathf.Abs(a[axis])>half[axis])return false;continue;}
                float x=(-half[axis]-a[axis])/d[axis],y=(half[axis]-a[axis])/d[axis];
                near=Mathf.Max(near,Mathf.Min(x,y));far=Mathf.Min(far,Mathf.Max(x,y));if(near>far)return false;
            }
            return true;
        }
    }
}
