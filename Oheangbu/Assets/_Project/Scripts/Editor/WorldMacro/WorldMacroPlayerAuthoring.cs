using System;
using System.IO;
using Oheangbu.App.World;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroPlayerAuthoring
    {
        public static string Install()
        {
            if(Application.isPlaying||UnityEngine.SceneManagement.SceneManager.GetActiveScene().path!=WorldMacroBuilder.ScenePath)throw new Exception("Macro scene in Edit mode required");
            var view=Object.FindFirstObjectByType<WorldMacroReviewController>();
            if(view==null)throw new Exception("Macro review camera missing");
            var root=GameObject.Find("Macro_PlayerCapsule");
            if(root==null)root=new GameObject("Macro_PlayerCapsule");
            var body=root.GetComponent<CharacterController>();if(body==null)body=root.AddComponent<CharacterController>();
            body.enabled=false;root.transform.localScale=Vector3.one;
            body.height=1.75f;body.radius=.28f;body.center=Vector3.up*.875f;
            body.slopeLimit=45;body.stepOffset=.3f;body.skinWidth=.03f;body.minMoveDistance=0;
            var feet=view.transform.position;float ground=-10000;
            foreach(var hit in Physics.RaycastAll(new Vector3(feet.x,2000,feet.z),Vector3.down,4000,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider.name.StartsWith("Terrain_")||hit.collider.name.StartsWith("Bridge_"))ground=Mathf.Max(ground,hit.point.y);
            if(ground<-9000)throw new Exception("No supported capsule spawn");
            feet.y=ground+.04f;root.transform.position=feet;root.transform.rotation=Quaternion.Euler(0,view.transform.eulerAngles.y,0);
            var visual=root.transform.Find("Player_Size_Reference");
            if(visual==null){visual=GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;visual.name="Player_Size_Reference";visual.SetParent(root.transform,false);Object.DestroyImmediate(visual.GetComponent<Collider>());}
            visual.localPosition=Vector3.up*.875f;visual.localRotation=Quaternion.identity;visual.localScale=new Vector3(.56f,.875f,.56f);
            var renderer=visual.GetComponent<Renderer>();renderer.sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>(WorldMacroBuilder.Folder+"/MacroStone.mat");
            view.WalkBody=body;view.WalkVisual=renderer;view.EyeHeight=1.62f;view.Mode=1;
            view.transform.position=feet+Vector3.up*view.EyeHeight;view.GetComponent<Camera>().nearClipPlane=.08f;
            body.enabled=true;EditorUtility.SetDirty(view);EditorUtility.SetDirty(body);
            WorldMacroWaterAuthoring.SaveScene();
            Directory.CreateDirectory(WorldMacroBuilder.Output+"/PlayerCapsule");
            var report=new Report{height=body.height,diameter=body.radius*2,eyeHeight=view.EyeHeight,skin=body.skinWidth,step=body.stepOffset,
                renderedHeight=renderer.bounds.size.y,renderedWidth=renderer.bounds.size.x,position=root.transform.position,walkSpeed=view.Sheet.WalkSpeed};
            File.WriteAllText(WorldMacroBuilder.Output+"/PlayerCapsule/setup.json",JsonUtility.ToJson(report,true));
            return "Capsule installed: 1.75m height / 0.56m diameter / eye 1.62m. WASD + hold RMB look. 1 free camera, 2 capsule, 3 route inspection. User walking validation pending.";
        }
        [Serializable]class Report
        {
            public string status="TEST",scope="Authored dimensions and floor-supported spawn only. Walking validation belongs to the user.";
            public float height,diameter,eyeHeight,skin,step,renderedHeight,renderedWidth,walkSpeed;
            public Vector3 position;
        }
    }
}
