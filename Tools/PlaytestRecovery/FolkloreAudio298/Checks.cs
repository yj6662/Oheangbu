using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Oheangbu.App.World;
using Oheangbu.App.Demo;
using Oheangbu.App.Prologue;
using Oheangbu.Combat;
using UnityEngine;

class Checks
{
    static readonly List<string> Passed=new();
    static void Check(bool ok,string id) { if(!ok)throw new Exception(id); Passed.Add(id); }
    static void Invoke(object value,string method)=>value.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null);
    static EnemyAudioProfile298 Profile()
    {
        var p=new EnemyAudioProfile298{ActorId="test"};
        foreach(var c in new[]{p.Alert,p.Windup,p.HitA,p.HitB,p.Death}) c.Clip=new AudioClip();
        return p;
    }
    static (EnemyAudioEmitter298 e,EnemyVitals v,EnemyController c,CompactSoundscape255 a) Actor()
    {
        var audio=new GameObject().AddComponent<CompactSoundscape255>(); CompactSoundscape255.Current=audio;
        var go=new GameObject();var v=go.AddComponent<EnemyVitals>();var c=go.AddComponent<EnemyController>();
        var e=go.AddComponent<EnemyAudioEmitter298>();e.Profile=Profile();e.Soundscape=audio;e.Rebind();return(e,v,c,audio);
    }
    static void Main()
    {
        var (e,v,c,a)=Actor();
        Check(a.Played.Count==0,"activation does not invent alert");
        Check(e.NotifyAlert()&&a.Played.Count==1,"explicit awareness alert");
        c.Start(10);Time.unscaledTime+=1;c.Start(10);
        Check(a.Played.Count==2,"same authored attack deduplicated");
        c.Start(11);Check(a.Played.Count==3,"next actual attack plays");
        v.Hit();Time.unscaledTime+=1;v.Hit();
        Check(ReferenceEquals(a.Cues[^2],e.Profile.HitA)&&ReferenceEquals(a.Cues[^1],e.Profile.HitB),"hit variants alternate");
        int old=a.Played.Count;v.Hit(true);v.RepeatDeath();
        Check(a.Played.Count==old+1&&a.Played[^1]=="test:Death","lethal hit death once after revision increment");
        Check(!e.NotifyAlert(),"dead actor cannot alert");
        v.Restore();c.Start(10);Check(a.Played.Count==old+2,"restore permits old attack id in new life");
        old=a.Played.Count;Invoke(e,"OnDisable");c.Start(30);v.Hit();
        Check(a.Played.Count==old,"disable unsubscribes damage and attack");
        e.Rebind();e.Rebind();Time.unscaledTime+=1;c.Start(31);
        Check(a.Played.Count==old+1,"rebind does not multiply subscriptions");
        Check(EnemyAudioEmitter298.OwnsCue(v,EnemyAudioRole298.Windup,a),"configured role suppresses generic");
        e.Profile.Windup.Clip=null;
        Check(!EnemyAudioEmitter298.OwnsCue(v,EnemyAudioRole298.Windup,a),"missing clip keeps generic");
        Check(e.Status.Contains("windup"),"missing role visible");
        a.Observed=true;old=a.Played.Count;Time.unscaledTime+=1;c.Start(32);
        Check(a.Played.Count==old,"registered generic observer not duplicated by fallback");
        a.Observed=false;Time.unscaledTime+=1;c.Start(33);
        Check(a.Played[^1]=="enemy_windup","dynamic actor missing role has generic fallback");
        e.Profile=Profile();a.EnemyAudioReady298=false;
        Check(!EnemyAudioEmitter298.OwnsCue(v,EnemyAudioRole298.Death,a),"unavailable shared pool cannot suppress");
        a.EnemyAudioState298="Unfocused";old=e.Played;Time.unscaledTime+=1;
        Check(!e.NotifyAlert()&&e.Played==old&&e.LastDropReason=="Unfocused"&&e.Status=="WaitingForSoundscape:Unfocused","focus suspension stays silent and exposes actual cause");
        a.EnemyAudioState298="MissingPool";Time.unscaledTime+=1;
        Check(!e.NotifyAlert()&&e.LastDropReason=="MissingPool","missing pool is distinguished from focus suspension");
        a.EnemyAudioState298="Ready";
        a.EnemyAudioReady298=true;c.AttackProfile=null;
        Check(!EnemyAudioEmitter298.OwnsCue(v,EnemyAudioRole298.Windup,a),"legacy polling windup remains generic");
        var p=Profile();p.HitA.Clip=null;
        Check(ReferenceEquals(p.Find(EnemyAudioRole298.Hit),p.HitB),"one missing hit variant falls back to real other clip");
        p.HitB.Clip.loadState=AudioDataLoadState.Failed;
        Check(p.Find(EnemyAudioRole298.Hit)==null,"failed clip load is missing");
        var boss=new GameObject();var bv=boss.AddComponent<EnemyVitals>();var bg=boss.AddComponent<SouthGateGeneralController>();
        var be=boss.AddComponent<EnemyAudioEmitter298>();be.Profile=Profile();be.Soundscape=a;be.Rebind();
        var plan=new SouthGateGeneralAttackPlan();plan.Pulses.Add(new SouthGateAttackPulse{Attack=new AttackProvenance(80)});
        old=a.Played.Count;bg.Start(plan);Time.unscaledTime+=1;bg.Start(plan);
        Check(a.Played.Count==old+1,"general actual plan starts once");
        old=a.Played.Count;bg.End(plan);Time.unscaledTime+=1;bg.StaleStart(plan);
        Check(a.Played.Count==old,"cancelled boss event cannot restart windup");
        old=a.Played.Count;c.End(90);c.StaleStart(90);
        Check(a.Played.Count==old,"cancelled regular event cannot restart windup");
        var host=new GameObject();var pool=new WorldMacroAudioVoicePool(host.transform,4,1,false,"check");
        object ownerA=new(),ownerB=new();var cue=Profile().Windup;
        pool.Play(cue,new Vector3(),1,null,slot:0,loop:true);
        pool.Play(cue,new Vector3(),1,null,owner:ownerA);pool.Play(cue,new Vector3(),1,null,owner:ownerB);
        pool.ReleaseOwner(ownerA);pool.Tick();
        Check(pool.Sources[0].isPlaying&&pool.Sources[2].isPlaying&&!pool.Sources[1].isPlaying,"owner cancellation preserves ambient and other actor");
        var pending=new WorldMacroAudioVoicePool(new GameObject().transform,1,0,false,"pending");
        pending.Play(cue,new Vector3(),1,null,owner:ownerB);
        pending.Play(cue,new Vector3(),1,null,owner:ownerA);pending.ReleaseOwner(ownerA);pending.Tick();
        Check(pending.Starts==1,"cancelled pending owner never starts");
        var retain=new WorldMacroAudioVoicePool(new GameObject().transform,1,0,false,"retain");
        retain.Play(cue,new Vector3(),1,null,owner:ownerA);
        retain.Play(cue,new Vector3(),1,null,owner:ownerB);retain.ReleaseOwner(ownerA);retain.Tick();
        Check(retain.Starts==2&&retain.Sources[0].isPlaying,"another owner pending request survives release");
        var aware=new GameObject();var av=aware.AddComponent<EnemyVitals>();var perception=aware.AddComponent<PrologueEncounter>();
        var ae=aware.AddComponent<EnemyAudioEmitter298>();ae.Profile=Profile();ae.Soundscape=a;ae.Rebind();
        old=ae.Played;perception.Detect();Time.unscaledTime+=1;perception.Detect();
        Check(ae.Played==old+1&&ae.LastRole=="Alert","perception transition emits once while chase continues");
        Invoke(ae,"OnDisable");ae.Rebind();Time.unscaledTime+=1;perception.Detect();
        Check(ae.Played==old+1,"rebind while already chasing does not invent another alert");
        perception.Current=PrologueEncounter.Behaviour.Patrol;av.Restore();Time.unscaledTime+=1;perception.Detect();
        Check(ae.Played==old+2,"restored actor receives later real perception transition");
        Invoke(ae,"OnDisable");perception.Current=PrologueEncounter.Behaviour.Patrol;Time.unscaledTime+=1;perception.Detect();
        Check(ae.Played==old+2,"disabled emitter unsubscribes perception owner");
        Console.WriteLine(JsonSerializer.Serialize(new{scope="Actual profile/emitter/pool sources, fake engine/event boundary; no Unity DSP or listening claim",count=Passed.Count,passed=Passed}));
    }
}
