using System;
using System.Linq;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Oheangbu.App;
using Oheangbu.App.SpellVFX120;
using Oheangbu.Combat;
using Oheangbu.Spellcraft;
using Oheangbu.Core.Domain;
using Object=UnityEngine.Object;
namespace Oheangbu.EditorTools.SpellVFX120
{
    [InitializeOnLoad] public static class KtpAreaWideRegression
    {
        const string Key="KtpAreaWideRegression";
        const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        [Serializable] class Report{public string status,mvid;public List<string> checks=new List<string>(),errors=new List<string>();}
        static KtpAreaWideRegression(){EditorApplication.update+=Tick;}
        public static string Start(){if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Stopped editor required");SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;return "PLAY_AUDIT_REQUESTED";}
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
            SessionState.SetBool(Key,false);
            var r=new Report{mvid=typeof(Vfx120Effect).Module.ModuleVersionId.ToString()};
            try{Run(r);r.status="PASS";}catch(Exception e){r.status="FAIL";r.errors.Add(e.ToString());}
            File.WriteAllText(Path.Combine(Vfx120Editor.Output,"AreaWide","contact_audit.json"),JsonUtility.ToJson(r,true));EditorApplication.isPlaying=false;
        }
        static object Get(object o,string name)=>o.GetType().GetField(name,Hidden).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetField(name,Hidden).SetValue(o,value);
        static void Invoke(object o,string name,params object[] args)=>o.GetType().GetMethod(name,Hidden).Invoke(o,args);
        static Element ElementFor(char g)=>g=='가'||g=='거'?Element.Wood:g=='나'||g=='너'?Element.Fire:g=='사'||g=='서'?Element.Metal:g=='마'||g=='머'?Element.Earth:Element.Water;
        static Element Countered(Element e)=>e==Element.Wood?Element.Earth:e==Element.Fire?Element.Metal:e==Element.Earth?Element.Water:e==Element.Metal?Element.Wood:Element.Fire;
        static Element Generated(Element e)=>e==Element.Wood?Element.Fire:e==Element.Fire?Element.Earth:e==Element.Earth?Element.Metal:e==Element.Metal?Element.Water:Element.Wood;
        static void Run(Report r)
        {
            var root=new GameObject("EmphasisContactAudit");root.SetActive(false);
            var config=ScriptableObject.CreateInstance<CombatConfigSO>();var cp=Resources.Load<KtpContactProfile>(KtpContactProfile.ResourcePath);
            var wiring=root.AddComponent<CombatLoopWiring>();Set(wiring,"_config",config);Set(wiring,"_contactVfx",cp);
            var judge=new ParryJudge(config);wiring.Construct(null,judge,new GroggyMeter(config),new InkPool(config,null));root.SetActive(true);
            Action<bool,string> need=(ok,label)=>{if(!ok)throw new Exception(label);r.checks.Add(label);};
            var contacts=(IList)Get(wiring,"_contacts");
            Func<KtpContactEffect> last=()=>contacts.Count==0?null:(KtpContactEffect)contacts[contacts.Count-1];
            need(cp.SpellProfiles.Length==15,"Exactly fifteen selected spell contact overrides");need(cp.ForSpell('무')==null&&cp.ForSpell(default)==null,"Other and missing letters retain default contacts");
            foreach(char glyph in new[]{'가','나','사','마','아'})
            {
                var go=new GameObject("Target_"+glyph);go.transform.SetParent(root.transform);go.SetActive(false);var enemy=go.AddComponent<EnemyVitals>();Set(enemy,"_config",config);go.SetActive(true);
                var type=typeof(CombatLoopWiring).GetNestedType("PendingCast",BindingFlags.NonPublic);var pending=(IList)Get(wiring,"_pendingCasts");
                Action<float,char> hit=(power,letter)=>{var item=Activator.CreateInstance(type);foreach(var pair in new Dictionary<string,object>{{"Target",enemy},{"Letter",letter},{"Element",ElementFor(glyph)},{"Power",power},{"ImpactTime",Time.time}})type.GetField(pair.Key).SetValue(item,pair.Value);pending.Add(item);Invoke(wiring,"TickPendingCasts");};
                int count=contacts.Count;hit(0,glyph);need(contacts.Count==count,glyph+" zero damage emits nothing");
                hit(5,glyph);need(contacts.Count==count+1&&Mathf.Approximately(last().transform.localScale.x,cp.ParryScale*3.5f),glyph+" confirmed damage emits one enlarged contact");
                hit(5,'무');need(contacts.Count==count+2&&Mathf.Approximately(last().transform.localScale.x,cp.ParryScale),"Other glyph uses unchanged scale");
                hit(100000,glyph);need(!enemy.IsAlive&&contacts.Count==count+3,glyph+" lethal hit emits once");hit(5,glyph);need(contacts.Count==count+3,glyph+" dead target emits nothing");
            }
            foreach(char glyph in new[]{'거','너','서','머','어'})
            {
                Element element=ElementFor(glyph);
                Element success=Countered(element);
                var cast=new SpellCast(glyph,SpellKind.Parry,element,1,default,1);
                Set(wiring,"_ink",new InkPool(config,null));Invoke(wiring,"ResolveParry",cast);int count=contacts.Count;
                need(judge.ResolveImpact(success,Time.time,Vector3.forward*3)==ParryOutcome.Success,glyph+" actual success judgment");
                need(contacts.Count==count+1&&Mathf.Approximately(last().transform.localScale.x,cp.ParryScale*2),glyph+" one doubled success contact");
                need(last().Source==cp.ForSpell(glyph).GuardContactPrefab&&last().Content.GetComponentsInChildren<Renderer>().Count(x=>x.enabled&&x.name.IndexOf("pattern",StringComparison.OrdinalIgnoreCase)>=0)==1,glyph+" uses one small pattern with elemental debris");
                need(cp.ForSpell(glyph).NativeFieldPrefab.GetComponentsInChildren<Renderer>().Any(x=>x.name=="ShieldPanel")&&cp.ForSpell(glyph).NativeCastPrefab==null,glyph+" rectangular shield replaces separate cast");
                need((char)Get(wiring,"_guardVisualLetter")==default,"Consumed guard clears its visual letter");
                Set(wiring,"_ink",new InkPool(config,null));Invoke(wiring,"ResolveParry",cast);count=contacts.Count;
                need(judge.ResolveImpact(element,Time.time,Vector3.forward*3)==ParryOutcome.Half,"Half outcome retained");
                need(contacts.Count==count+1&&Mathf.Approximately(last().transform.localScale.x,cp.ParryScale*2*cp.HalfScale),glyph+" half-size rule retained");
                judge.RaiseGuard(element,Time.time);judge.ResolveImpact(success,Time.time,Vector3.forward*3);
                need(Mathf.Approximately(last().transform.localScale.x,cp.ParryScale),"External replacement invalidates old glyph treatment");
                Set(wiring,"_ink",new InkPool(config,null));Invoke(wiring,"ResolveParry",cast);
                var fail=Generated(element);count=contacts.Count;
                need(judge.ResolveImpact(fail,Time.time,Vector3.forward*3)==ParryOutcome.Fail&&contacts.Count==count,"Failed guard emits no contact");
                need((char)Get(wiring,"_guardVisualLetter")==default,"Failed guard clears visual letter");
            }
            root.SetActive(false);need((char)Get(wiring,"_guardVisualLetter")==default,"Disable clears selected guard treatment");
            Object.Destroy(root);Object.Destroy(config);
        }
    }
}
