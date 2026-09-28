using System.Collections.Generic;
using UnityEngine;
namespace Oheangbu.App.Demo
{
    // Only editor-verified, continuously supported ground seams may carry this marker.
    public sealed class DemoEscortNavigationSeam : MonoBehaviour
    {
        public Vector3 StartWorld,EndWorld;
        public static readonly List<DemoEscortNavigationSeam> Active=new List<DemoEscortNavigationSeam>();
        void OnEnable(){if(!Active.Contains(this))Active.Add(this);}
        void OnDisable(){Active.Remove(this);}
        public bool Matches(Vector3 a,Vector3 b)=>Vector3.Distance(a,StartWorld)<.5f&&Vector3.Distance(b,EndWorld)<.5f||Vector3.Distance(b,StartWorld)<.5f&&Vector3.Distance(a,EndWorld)<.5f;
    }
}
