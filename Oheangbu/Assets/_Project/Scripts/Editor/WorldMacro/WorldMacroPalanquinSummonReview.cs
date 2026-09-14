using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Oheangbu.App.World.Vehicle;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static class WorldMacroPalanquinSummonReview
    {
        [Serializable] sealed class Sample {public string origin,route,reason;public bool found;public Vector3 feet,destination;public float slope;}
        [Serializable] sealed class Report
        {
            public string status,scope="Read-only destination probes against the actual scene. Does not call, move, instantiate, board, save or reconfigure the vehicle.";
            public int cars,callers,majorRoads,successfulDestinations;
            public bool carTransformPreserved,profilePreserved;
            public List<Sample> samples=new List<Sample>();public List<string> failures=new List<string>();
        }
        public static string Execute(string action)
        {
            if(action!="audit")throw new ArgumentException("summon review: audit");
            var r=new Report();
            var cars=Object.FindObjectsByType<WorldMacroPalanquinController>(FindObjectsSortMode.None);
            var callers=Object.FindObjectsByType<WorldMacroPalanquinSummon>(FindObjectsSortMode.None);
            r.cars=cars.Length;r.callers=callers.Length;
            if(cars.Length!=1||callers.Length!=1){r.failures.Add("Exactly one existing car and one caller required");return Save(r);}
            var c=callers[0];var v=cars[0];Vector3 oldPosition=v.transform.position;Quaternion oldRotation=v.transform.rotation;var profile=v.Profile;
            if(c.WorldSheet==null||c.Walker==null||c.Walker.Body==null){r.failures.Add("Caller references incomplete");return Save(r);}
            void Probe(string name,Vector3 feet,Vector3 forward)
            {
                bool found=c.TryFindPlacement(feet,forward,out var placement,out string reason);
                if(found)r.successfulDestinations++;
                r.samples.Add(new Sample{origin=name,feet=feet,found=found,reason=reason,route=placement.Route,destination=placement.Position,slope=placement.Slope});
            }
            Physics.SyncTransforms();
            Probe("current_player",c.Walker.Body.transform.position,c.Walker.Body.transform.forward);
            r.majorRoads=c.WorldSheet.Routes.Count(x=>x!=null&&x.Carriage);
            // Nearby road samples are virtual query origins. The actual player never moves.
            var feetNow=c.Walker.Body.transform.position;
            var near=c.WorldSheet.Routes.Where(x=>x!=null&&x.Carriage&&x.Points!=null)
                .SelectMany(x=>x.Points.Select(p=>new{route=x.Id,p}))
                .OrderBy(x=>Vector3.Distance(x.p,feetNow)).Take(12);
            foreach(var p in near)Probe(p.route+"_virtual_road_origin",p.p+Vector3.right*3,Vector3.forward);
            r.carTransformPreserved=v.transform.position==oldPosition&&v.transform.rotation==oldRotation;
            r.profilePreserved=v.Profile==profile;
            if(!r.carTransformPreserved||!r.profilePreserved)r.failures.Add("Read-only probe mutated car");
            if(r.successfulDestinations==0)r.failures.Add("No safe destination found near sampled roads; real call unverified");
            return Save(r);
        }
        static string Save(Report r)
        {
            r.status=r.failures.Count==0?"PASS_QUERY_ONLY":"FINDINGS";
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Art/PlaytestRecovery/OpeningSequence"));Directory.CreateDirectory(folder);
            string text=JsonUtility.ToJson(r,true);File.WriteAllText(Path.Combine(folder,"vehicle_call_query.json"),text);return text;
        }
    }
}
