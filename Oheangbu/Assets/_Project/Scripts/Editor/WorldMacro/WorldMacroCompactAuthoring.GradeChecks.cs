using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Oheangbu.Data.World;

namespace Oheangbu.EditorTools.WorldMacro
{
    public static partial class WorldMacroCompactAuthoring
    {
        [Serializable] sealed class GradeCheckRow { public string name; public float expected, actual; public bool pass; }
        [Serializable] sealed class GradeCheckReport { public string status, scope; public int version; public GradeCheckRow[] checks; }
        static string GradeChecks()
        {
            RequireCompact();
            var profile=ScriptableObject.CreateInstance<WorldMacroRoadGradeSO>();
            var rows=new List<GradeCheckRow>();
            void Check(string name,float expected,float actual) => rows.Add(new GradeCheckRow { name=name,expected=expected,actual=actual,pass=Mathf.Abs(expected-actual)<.0001f });
            try
            {
                var wide=new WorldMacroRoadGradeSO.Line {Id="car",Width=8,Points=new[]{new Vector3(0,10,0),new Vector3(0,20,100)},OriginalY=new[]{10f,20f}};
                var narrow=new WorldMacroRoadGradeSO.Line {Id="foot",Width=2.2f,Points=new[]{new Vector3(6,12,0),new Vector3(6,12,100)},OriginalY=new[]{12f,12f}};
                profile.Lines=new[]{narrow,wide};profile.InvalidateCache();
                Check("car-bed versus closer footpath shoulder",15,profile.Height(3.5f,50,30));
                Check("overlapping full beds choose wider car bed",15,profile.Height(4.5f,50,30));
                Check("outside car bed keeps footpath",12,profile.Height(6,50,30));
                Check("outside every shoulder retains source",30,profile.Height(80,50,30));
                profile.Lines=new[]{wide,narrow};profile.InvalidateCache();
                Check("bed selection independent of route order",15,profile.Height(3.5f,50,30));
                profile.Protected=new[]{Rect.MinMaxRect(3,49,4,51)};
                Check("protected source island height",77,profile.Height(3.5f,50,77));
                Check("hillside receives relative displacement",102.5f,WorldMacroRoadGradeSO.BlendHeight(100,50,55,25));
                Check("shoulder boundary retains source",100,WorldMacroRoadGradeSO.BlendHeight(100,50,55,50));
                var report=new GradeCheckReport {status=rows.TrueForAll(r=>r.pass)?"PASS":"FAIL",version=WorldMacroRoadGradeSO.CurrentVersion,checks=rows.ToArray(),scope="Actual grade evaluator on analytic fixtures in Unity Edit mode. No terrain, traversal or gameplay certification."};
                string json=JsonUtility.ToJson(report,true);File.WriteAllText(Path.Combine(Output,"grade_evaluator_checks.json"),json);return json;
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }
    }
}
