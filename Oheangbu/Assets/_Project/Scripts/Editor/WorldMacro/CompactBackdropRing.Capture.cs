using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Oheangbu.Data.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>
    /// Fixed review viewpoints for the backdrop. Screenshots are EVIDENCE, never a PASS:
    /// art approval and movement shimmer stay with the user. [SPEC-COMPACT-BACKDROP-RING]
    /// </summary>
    public static partial class CompactBackdropRing
    {
        public const string ReviewCameraName="Backdrop_Review_Camera";

        [Serializable] public sealed class ViewPose{public string id;public Vector3 eye,target;public float fieldOfView;}
        [Serializable] public sealed class ViewList{public ViewPose[] views;}

        /// <summary>
        /// Poses anchored to where the player actually stands. An arbitrary elevated camera looks out
        /// past the edge of the authored terrain, where this scene has always shown empty paper — that
        /// is a property of the existing world, not of the backdrop, and it makes such a view useless
        /// for judging the ring.
        /// </summary>
        static ViewPose[] Views()
        {
            var player=SceneManager.GetActiveScene().GetRootGameObjects()
                .FirstOrDefault(g=>g.name=="Macro_PlayerCapsule");
            Vector3 stand=player!=null?player.transform.position:new Vector3(894,134,247);
            Vector3 eye=stand+new Vector3(0,1.62f,0); // eye height from the capsule contract
            return new[]
            {
                new ViewPose{id="player_north",eye=eye,target=eye+new Vector3(0,40,1200),fieldOfView=60},
                new ViewPose{id="player_east",eye=eye,target=eye+new Vector3(1200,40,0),fieldOfView=60},
                new ViewPose{id="player_south",eye=eye,target=eye+new Vector3(0,40,-1200),fieldOfView=60},
                new ViewPose{id="player_west",eye=eye,target=eye+new Vector3(-1200,40,0),fieldOfView=60},
                new ViewPose{id="player_northeast",eye=eye,target=eye+new Vector3(900,60,900),fieldOfView=65},

                // Eye-level stances at other authored places, for judging the map's art as a whole
                // rather than only the backdrop. Ground height is sampled so the eye sits on the
                // surface instead of floating at an assumed altitude.
                new ViewPose{id="capital_north",eye=Ground(-346,-991)+new Vector3(0,1.62f,0),target=Ground(-346,-991)+new Vector3(0,80,1400),fieldOfView=60},
                new ViewPose{id="capital_west",eye=Ground(-346,-991)+new Vector3(0,1.62f,0),target=Ground(-346,-991)+new Vector3(-1400,80,0),fieldOfView=60},
                new ViewPose{id="field_centre_north",eye=Ground(0,0)+new Vector3(0,1.62f,0),target=Ground(0,0)+new Vector3(0,90,1600),fieldOfView=60},
                new ViewPose{id="field_south_north",eye=Ground(200,-2200)+new Vector3(0,1.62f,0),target=Ground(200,-2200)+new Vector3(0,90,1600),fieldOfView=60},
                new ViewPose{id="field_west_east",eye=Ground(-1500,600)+new Vector3(0,1.62f,0),target=Ground(-1500,600)+new Vector3(1600,90,0),fieldOfView=60}
            };
        }

        /// <summary>Drops a point onto the first surface below a high start, so a stance sits on real ground.</summary>
        static Vector3 Ground(float x,float z)
        {
            var origin=new Vector3(x,3000f,z);
            if(Physics.Raycast(origin,Vector3.down,out var hit,6000f))return hit.point;
            return new Vector3(x,140f,z);
        }

        /// <summary>Creates (or moves) a disabled review camera at one named pose and returns its state.</summary>
        public static string Pose(string id)
        {
            Guard();
            var pose=Views().FirstOrDefault(v=>v.id==id);
            if(pose==null)
                throw new ArgumentException("Unknown view id '"+id+"'. Known: "+string.Join(", ",Views().Select(v=>v.id)));

            var scene=SceneManager.GetActiveScene();
            var existing=scene.GetRootGameObjects().FirstOrDefault(g=>g.name==ReviewCameraName);
            if(existing==null)
            {
                existing=new GameObject(ReviewCameraName);
                // Transient: review only, never saved into the scene as content.
                existing.hideFlags=HideFlags.DontSave;
                var created=existing.AddComponent<Camera>();
                created.clearFlags=CameraClearFlags.Skybox;
                created.nearClipPlane=.08f;
                created.farClipPlane=22000f;
            }
            var camera=existing.GetComponent<Camera>();
            if(camera==null)camera=existing.AddComponent<Camera>();
            camera.fieldOfView=pose.fieldOfView;
            camera.farClipPlane=22000f;
            camera.nearClipPlane=.08f;
            existing.transform.position=pose.eye;
            existing.transform.rotation=Quaternion.LookRotation((pose.target-pose.eye).normalized,Vector3.up);

            var backdrop=BackdropTransform();
            int visible=0;
            if(backdrop!=null)
            {
                var planes=GeometryUtility.CalculateFrustumPlanes(camera);
                foreach(var renderer in backdrop.GetComponentsInChildren<Renderer>(true))
                    if(GeometryUtility.TestPlanesAABB(planes,renderer.bounds))visible++;
            }
            return "{\"status\":\"posed\",\"id\":\""+id+"\",\"eye\":\""+pose.eye.ToString("F0")+
                "\",\"target\":\""+pose.target.ToString("F0")+"\",\"backdropRenderersInFrustum\":"+visible+
                ",\"camera\":\""+ReviewCameraName+"\"}";
        }

        /// <summary>Removes the transient review camera.</summary>
        public static string ClearPose()
        {
            var existing=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==ReviewCameraName);
            if(existing==null)return "{\"status\":\"absent\"}";
            UnityEngine.Object.DestroyImmediate(existing);
            return "{\"status\":\"cleared\"}";
        }

        static Transform BackdropTransform()
        {
            var geography=SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==GeographyRoot);
            return geography==null?null:geography.transform.Find(BackdropRoot);
        }

        /// <summary>Toggles the backdrop subtree so before/after can be captured from identical poses.</summary>
        public static string Toggle(string state)
        {
            Guard();
            var backdrop=BackdropTransform();
            if(backdrop==null)return "{\"status\":\"absent\"}";
            bool active=state=="on";
            backdrop.gameObject.SetActive(active);
            return "{\"status\":\""+(active?"on":"off")+"\"}";
        }
    }
}
