using System;
using System.IO;
using System.Linq;
using Oheangbu.App.World;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    /// <summary>Prepares only the saved playtest encounter instances for ordered runtime NavMesh binding.</summary>
    public static class WorldMacroPlaytestNavStartupAuthoring
    {
        [Serializable] sealed class ActorRecord
        {
            public string id;
            public Vector3 position,navMeshPosition,finalPatrolPosition;
            public bool startsDisabled,positionSampled,finalPatrolSampled,completePath;
        }

        [Serializable] sealed class PreparationReport
        {
            public string utc,scene,navMeshAsset,scope;
            public bool passed;
            public ActorRecord[] actors;
        }

        public static string PrepareSavedAgents()
        {
            var scene=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(EditorApplication.isPlaying || scene.path!=WorldMacroPlaytestAuthoring.ScenePath)
                throw new InvalidOperationException("Open the saved world macro playtest scene in Edit mode.");
            var session=Object.FindFirstObjectByType<WorldMacroPlaytestSession>();
            if(session==null || session.Actors==null || session.Actors.Length==0)
                throw new InvalidOperationException("World macro playtest session actors are missing.");
            var surface=Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include,FindObjectsSortMode.None)
                .FirstOrDefault(s => s.name=="Playtest_Local_Navigation");
            if(surface==null || surface.navMeshData==null)
                throw new InvalidOperationException("Saved Playtest_Local_Navigation data is missing.");
            string navMeshAsset=AssetDatabase.GetAssetPath(surface.navMeshData);
            if(string.IsNullOrEmpty(navMeshAsset))
                throw new InvalidOperationException("Playtest navigation must reference a saved NavMeshData asset.");

            var records=session.Actors.Select(actor => InspectAndDisable(actor)).ToArray();
            var report=new PreparationReport
            {
                utc=DateTime.UtcNow.ToString("o"),scene=scene.path,navMeshAsset=navMeshAsset,
                scope="Saved playtest actor startup state only; navigation data, geometry, sources, and encounter behavior are unchanged.",
                actors=records,passed=records.Length==3 && records.All(r => r.startsDisabled && r.positionSampled && r.finalPatrolSampled && r.completePath)
            };
            if(!report.passed)throw new InvalidOperationException("Playtest agent startup preparation did not validate all three actors.");
            Directory.CreateDirectory(WorldMacroPlaytestAuthoring.Output);
            File.WriteAllText(WorldMacroPlaytestAuthoring.Output+"/navmesh_startup_preparation.json",JsonUtility.ToJson(report,true));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            return "Saved 3 playtest NavMeshAgents disabled for ordered Start binding; all positions and final patrol paths validate against "+navMeshAsset+".";
        }

        static ActorRecord InspectAndDisable(Oheangbu.App.Prologue.PrologueEncounter actor)
        {
            if(actor==null)throw new InvalidOperationException("Playtest actor reference is null.");
            var agent=actor.GetComponent<NavMeshAgent>();
            if(agent==null)throw new InvalidOperationException("Playtest actor has no NavMeshAgent: "+actor.Id);
            if(actor.PatrolPoints==null || actor.PatrolPoints.Length==0)
                throw new InvalidOperationException("Playtest actor has no patrol points: "+actor.Id);
            Vector3 positionProbe=actor.transform.position-Vector3.up*agent.baseOffset;
            Vector3 patrolProbe=actor.PatrolPoints[actor.PatrolPoints.Length-1]-Vector3.up*agent.baseOffset;
            bool positionSampled=NavMesh.SamplePosition(positionProbe,out var start,2f,agent.areaMask);
            bool patrolSampled=NavMesh.SamplePosition(patrolProbe,out var finish,2f,agent.areaMask);
            var path=new NavMeshPath();
            bool complete=positionSampled && patrolSampled && NavMesh.CalculatePath(start.position,finish.position,agent.areaMask,path) &&
                path.status==NavMeshPathStatus.PathComplete;
            agent.enabled=false;
            EditorUtility.SetDirty(agent);
            return new ActorRecord
            {
                id=actor.Id,position=actor.transform.position,navMeshPosition=positionSampled?start.position:default,
                finalPatrolPosition=actor.PatrolPoints[actor.PatrolPoints.Length-1],startsDisabled=!agent.enabled,
                positionSampled=positionSampled,finalPatrolSampled=patrolSampled,completePath=complete
            };
        }
    }
}
