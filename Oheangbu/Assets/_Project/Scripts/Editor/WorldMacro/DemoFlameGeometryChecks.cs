using System;
using System.Collections.Generic;
using System.IO;
using Oheangbu.App.Demo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    // Geometry diagnostic, not a rendered-pixel or damage test. Uses the actual owned PS prefab.
    public static class DemoFlameGeometryChecks
    {
        [Serializable] sealed class Row
        {
            public string name;
            public int particleSamples, outsideCenters, quadVertices, outsideQuadVertices;
            public float maximumCenterRange, maximumQuadRange;
            public Vector3 firstParticleMean, firstBakedMean, systemPosition, systemScale;
        }
        [Serializable] sealed class Report
        {
            public string status;
            public string scope = "Actual particle simulation and camera-facing baked billboard vertices; transparent texels are included. Not a visible-pixel proof.";
            public List<Row> systems = new List<Row>();
            public List<string> failures = new List<string>();
            public int samples, outsideCenters, outsideQuadVertices;
            public float maximumCenterRange, maximumQuadRange;
        }
        public static string Run()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Edit mode only");
            var report = new Report(); var scene = EditorSceneManager.NewPreviewScene();
            Mesh baked = null;
            try
            {
                var profile = AssetDatabase.LoadAssetAtPath<SummonCombatProfile>(DemoHaetaeAuthoring.ProfilePath);
                if (profile == null || profile.FlamePrefab == null) throw new InvalidOperationException("Authored Nom required");
                var holder = new GameObject("Owned fire geometry diagnostic"); SceneManager.MoveGameObjectToScene(holder, scene);
                var visual = holder.AddComponent<DemoSummonFlamePresentation>();
                var plan = new SummonFlameAttackPlan(Vector3.zero, Vector3.forward, profile.FlameRange,
                    profile.FlameHalfAngleDegrees, profile.FlameVerticalTolerance, 0, profile.FlameWindupSeconds,
                    profile.FlameSpraySeconds, profile.FlameRecoverySeconds);
                var cameraObject = new GameObject("Owned billboard diagnostic camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
                var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
                baked = new Mesh(); var buffer = new ParticleSystem.Particle[1024];
                var views = new[] {new Vector3(8,2,3),new Vector3(0,1,-6),new Vector3(0,9,2)};
                visual.Sample(profile, plan, 0, true);
                var systems = holder.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var ps in systems) report.systems.Add(new Row {name=ps.name});
                for (float clock = plan.ReleaseAt; clock < plan.EndAt; clock += 1f / 60f)
                {
                    plan.AdvanceTo(clock); visual.Sample(profile, plan, clock, true); report.samples++;
                    for (int s = 0; s < systems.Length; s++)
                    {
                        var ps=systems[s]; var row=report.systems[s]; int count=ps.GetParticles(buffer);
                        for (int p = 0; p < count; p++)
                        {
                            var main=ps.main; Vector3 point=buffer[p].position;
                            if(main.simulationSpace==ParticleSystemSimulationSpace.Local) point=ps.transform.TransformPoint(point);
                            else if(main.simulationSpace==ParticleSystemSimulationSpace.Custom && main.customSimulationSpace!=null)
                                point=main.customSimulationSpace.TransformPoint(point);
                            float distance=new Vector2(point.x,point.z).magnitude;
                            row.particleSamples++; row.maximumCenterRange=Mathf.Max(row.maximumCenterRange,distance);
                            if(!plan.Contains(point))row.outsideCenters++;
                        }
                        var renderer=ps.GetComponent<ParticleSystemRenderer>();
                        foreach(var view in views)
                        {
                            camera.transform.position=view; camera.transform.LookAt(new Vector3(0,.1f,2));
                            baked.Clear(); renderer.BakeMesh(baked,camera,false);
                            if(row.quadVertices==0 && baked.vertexCount>0)
                            {
                                Vector3 sum=Vector3.zero;for(int p=0;p<count;p++)sum+=ps.transform.TransformPoint(buffer[p].position);
                                row.firstParticleMean=sum/Mathf.Max(1,count);sum=Vector3.zero;
                                foreach(var vertex in baked.vertices)sum+=vertex;
                                row.firstBakedMean=sum/baked.vertexCount;row.systemPosition=renderer.transform.position;row.systemScale=renderer.transform.lossyScale;
                            }
                            foreach(var vertex in baked.vertices)
                            {
                                var point=renderer.transform.TransformPoint(vertex);
                                row.quadVertices++; row.maximumQuadRange=Mathf.Max(row.maximumQuadRange,new Vector2(point.x,point.z).magnitude);
                                if(!plan.Contains(point))row.outsideQuadVertices++;
                            }
                        }
                    }
                }
                foreach(var row in report.systems)
                {
                    report.outsideCenters+=row.outsideCenters; report.outsideQuadVertices+=row.outsideQuadVertices;
                    report.maximumCenterRange=Mathf.Max(report.maximumCenterRange,row.maximumCenterRange);
                    report.maximumQuadRange=Mathf.Max(report.maximumQuadRange,row.maximumQuadRange);
                }
                if(report.samples==0||report.systems.Count!=10)report.failures.Add("Missing expected simulation samples or systems");
                report.status=report.failures.Count>0?"ERROR":report.outsideCenters>0||report.outsideQuadVertices>0?"GEOMETRY_EXCEEDS_DAMAGE_CONE":"GEOMETRY_CONTAINED";
            }
            catch(Exception e){report.failures.Add(e.ToString());report.status="ERROR";}
            finally {if(baked!=null)Object.DestroyImmediate(baked);EditorSceneManager.ClosePreviewScene(scene);}
            string json=JsonUtility.ToJson(report,true);
            File.WriteAllText(Path.Combine(DemoSummonAuthoring.Output,"flame_geometry.json"),json);return json;
        }
    }
}
