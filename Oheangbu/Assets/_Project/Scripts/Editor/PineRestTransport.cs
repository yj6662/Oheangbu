using System;
using System.Linq;
using Oheangbu.App;
using Oheangbu.App.Prologue;
using Oheangbu.App.World;
using Oheangbu.App.World.Vehicle;
using Oheangbu.Combat;
using Oheangbu.Drawing;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools
{
    public static partial class PineRestGameBuilder
    {
        static string TransportLoadout()
        {
            var scene=SceneManager.GetActiveScene();if(EditorApplication.isPlaying||scene.path!=Scene||scene.isDirty)throw new Exception("Saved Journey required");
            var s=Object.FindFirstObjectByType<PrologueSession>();var v=s.JourneySeat.Vehicle;
            var ground=Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c=>c.name=="Valley");
            if(!ground.Raycast(new Ray(new Vector3(8,200,43),Vector3.down),out var hit,400))throw new Exception("Departure support absent");
            v.transform.position=hit.point+Vector3.up*.15f;
            Transform Socket(string name,Vector3 pos){var t=v.transform.Find(name);if(t==null){t=new GameObject(name).transform;t.SetParent(v.transform,false);}t.localPosition=pos;t.localRotation=Quaternion.identity;return t;}
            s.EscortPassengerSocket=Socket("Journey passenger",new Vector3(-.3f,1.12f,-.5f));
            s.EscortCargoSocket=Socket("Journey cargo",new Vector3(.2f,1.12f,.55f));
            var root=GameObject.Find("Journey Merchant Branch").transform;var anchor=root.Find("Departure anchor");if(anchor==null){anchor=new GameObject("Departure anchor").transform;anchor.SetParent(root,false);}anchor.position=hit.point;s.EscortDepartureAnchor=anchor;
            EditorUtility.SetDirty(s);EditorSceneManager.SaveScene(scene);return "Owned passenger/cargo sockets and fixed departure point connected";
        }

        static string TransportSite()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlaying || scene.path != Scene || scene.isDirty) throw new Exception("Saved Journey required");
            var session = Object.FindFirstObjectByType<PrologueSession>();
            if (session.JourneySeat != null) return "Existing Journey vehicle retained";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/World/WorldMacro/Palanquin/Prefabs/MagicPalanquin_TEST.prefab");
            if (prefab == null) throw new Exception("Existing palanquin prefab absent");
            var ground = Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None).Single(c => c.name == "Valley");
            if (!ground.Raycast(new Ray(new Vector3(6, 200, 43), Vector3.down), out var hit, 400)) throw new Exception("Vehicle support absent");
            var vehicle = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            vehicle.name = "Journey Palanquin";
            PrefabUtility.UnpackPrefabInstance(vehicle, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            vehicle.transform.SetPositionAndRotation(hit.point + Vector3.up * .15f, Quaternion.identity);
            var driver = vehicle.GetComponent<WorldMacroPalanquinController>();
            string profilePath = Folder + "/Palanquin.asset";
            var profile = AssetDatabase.LoadAssetAtPath<WorldMacroPalanquinProfileSO>(profilePath);
            if (profile == null) { profile = Object.Instantiate(driver.Profile); AssetDatabase.CreateAsset(profile, profilePath); }
            driver.Profile = profile;
            var seat = vehicle.GetComponent<WorldMacroPalanquinSeat>();
            var walker = session.Player.GetComponent<WorldMacroCombatWalker>() ?? session.Player.gameObject.AddComponent<WorldMacroCombatWalker>();
            walker.Motor = session.Player.GetComponent<PlayerMotor>(); walker.Body = session.Player.GetComponent<CharacterController>();
            walker.CameraRig = Object.FindFirstObjectByType<CameraRigController>(); walker.ViewCamera = Camera.main;
            walker.Drawing = session.Player.GetComponentInChildren<DrawingInputController>(true); walker.Wiring = session.Wiring;
            walker.Visuals = GameObject.Find("PlayerRig").GetComponentsInChildren<Renderer>(true);
            walker.EyeHeight = walker.Body.center.y + walker.Body.height * .5f - .15f;
            if (walker.Drawing == null || walker.CameraRig == null || walker.ViewCamera == null) throw new Exception("Journey player references missing");
            seat.ReviewController = null; seat.CombatWalker = walker; seat.ViewCamera = walker.ViewCamera; seat.StartInSeatedView = false;
            session.JourneySeat = seat;
            if (!driver.ApplyConfiguration()) throw new Exception(driver.ConfigurationIssue);
            EditorUtility.SetDirty(session); EditorUtility.SetDirty(walker); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
            return "Journey-owned vehicle/profile and player camera adapter connected; escort departure remains separate";
        }
    }
}
