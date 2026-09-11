using UnityEngine;

namespace Oheangbu.App.SpellVFX120
{
    public sealed partial class Vfx120Effect
    {
        GameObject _fireBolt;
        Transform _fireTravel, _fireContact;
        ParticleSystem[] _fireTravelSystems, _fireContactSystems;
        float _fireSimulated;
        bool _fireImpacted;
        Vector3 _fireContactPoint;
        MeshRenderer _fireHitPattern;
        Vector3 _firePatternLocalPoint;
        Quaternion _firePatternFacing;
        MaterialPropertyBlock _firePatternBlock;
        public float FireHitPatternOpacity { get; private set; }
        float _chargeDetonatedAt=-1;
        ParticleSystem[] _chargeHoldSystems;
        ParticleSystem[] _splitFlames;
        public float FireSplitSeparation => _splitFlames!=null&&_fireImpacted?Vector3.Distance(_splitFlames[0].transform.position,_splitFlames[1].transform.position):0;
        public float EmberChargeDetonatedAt=>_chargeDetonatedAt;
        public bool IsEmberCharge=>Profile!=null&&Profile.Glyph=="남"&&IsFireBolt(Profile);
        public bool DetonateEmberCharge(float at=-1)
        {
            float when=at<0?Age:at;
            float attached=ReceivedImpactClock>0?_flight:(PreviewControlled&&DemonstrationCues?_flight:-1);
            if(!IsEmberCharge||!FireBoltConfigured||_chargeDetonatedAt>=0||attached<0||when<attached||when>=Life||float.IsNaN(when)||float.IsInfinity(when))return false;
            _chargeDetonatedAt=when;return true;
        }
        public static bool IsFireBolt(Vfx120Profile p) => p != null && p.NativeBodyPrefab != null &&
            (p.Glyph == "나" && p.NativeBodyPrefab.name == "PF_FireBolt_025" || p.Glyph == "낙" && p.NativeBodyPrefab.name == "PF_AttachedFlame_026" || p.Glyph == "난" && p.NativeBodyPrefab.name == "PF_HeavyFlame_027" || p.Glyph=="남"&&p.NativeBodyPrefab.name=="PF_EmberCharge_028" || p.Glyph=="낫"&&p.NativeBodyPrefab.name=="PF_FlameCrescent_029" || p.Glyph=="낭"&&p.NativeBodyPrefab.name=="PF_PiercingFlame_030");
        bool IsAttachedFlame => Profile.Glyph=="낙";
        bool IsPiercingFlame => Profile.Glyph=="낭";
        public float FirePenetrationDistance => IsPiercingFlame&&_fireImpacted?Vector3.Distance(FireBoltPosition,_fireContactPoint):0;
        Vector3 FireTravelPoint(float at,float hit)
        {
            if(IsPiercingFlame&&hit>=0&&at>hit)
            {
                Vector3 contact=_fireImpacted?_fireContactPoint:TargetPoint();
                Vector3 direction=(contact-ReceivedOrigin).normalized;
                return contact+direction*(Mathf.Min(at-hit,.36f)*6f);
            }
            return Vector3.Lerp(ReceivedOrigin,TargetPoint(),Mathf.Clamp01(at/Mathf.Max(.001f,_flight)));
        }
        public bool FireBoltConfigured => _fireBolt != null;
        public int FireBoltParticles { get { int n=0; if(_fireBolt!=null) foreach(var ps in _fireBolt.GetComponentsInChildren<ParticleSystem>()) n+=ps.particleCount; return n; } }
        public bool FireBoltImpacted => _fireImpacted;
        public Vector3 FireBoltPosition => _fireTravel==null?ReceivedOrigin:_fireTravel.position;

        void BuildFireBolt()
        {
            if(!IsFireBolt(Profile))return;
            _fireBolt=Instantiate(Profile.NativeBodyPrefab,transform,false);
            _fireTravel=_fireBolt.transform.Find("Travel"); _fireContact=_fireBolt.transform.Find("Contact");
            _fireTravelSystems=_fireTravel.GetComponentsInChildren<ParticleSystem>();
            _fireContactSystems=_fireContact.GetComponentsInChildren<ParticleSystem>();
            _chargeHoldSystems=IsEmberCharge?_fireBolt.transform.Find("Hold").GetComponentsInChildren<ParticleSystem>():null;
            if(Profile.Glyph=="낫")_splitFlames=new[]{_fireBolt.transform.Find("SplitLeft").GetComponent<ParticleSystem>(),_fireBolt.transform.Find("SplitRight").GetComponent<ParticleSystem>()};
            _fireHitPattern=WardRenderer("KTP_FireContact_110",_fireBolt.transform,QuadMesh.Value,Profile.PatternMaterial);
            _firePatternBlock=new MaterialPropertyBlock();
            ResetFireBolt();
        }
        void ResetFireBolt()
        {
            _fireSimulated=0; _fireImpacted=false;
            FireHitPatternOpacity=0;if(_fireHitPattern!=null)_fireHitPattern.enabled=false;
            foreach(var ps in _fireTravelSystems){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);var emission=ps.emission;emission.enabled=true;ps.Simulate(0,false,true,false);}
            foreach(var ps in _fireContactSystems){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(0,false,true,false);}
            if(_chargeHoldSystems!=null)foreach(var ps in _chargeHoldSystems){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(0,false,true,false);}
            if(_splitFlames!=null)foreach(var ps in _splitFlames){ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);ps.Simulate(0,false,true,false);}
            _fireTravel.position=ReceivedOrigin;
        }
        void SampleFireBolt()
        {
            if(_fireBolt==null)return;
            float end=Mathf.Min(Age,Life);
            if(end+0.00001f<_fireSimulated)ResetFireBolt();
            // Fixed visual integration: identical particle history at 30/60/120 Hz.
            // Only this owner advances these systems; Simulate leaves them paused.
            // At most Life*60 steps for a seek, normally one or two per frame.
            const float step=1f/60f;
            float hit=NativeImpactClock();
            float contactAt=IsEmberCharge?(ReceivedImpactClock>0?_flight:PreviewControlled&&DemonstrationCues?_flight:-1):hit;
            while(_fireSimulated+step<=end+0.00001f)
            {
                float at=_fireSimulated+step;
                _fireTravel.position=FireTravelPoint(at,hit);
                Vector3 dir=TargetPoint()-ReceivedOrigin;
                _fireTravel.rotation=Quaternion.LookRotation(dir.sqrMagnitude>.001f?dir:Vector3.forward,Vector3.up);
                foreach(var ps in _fireTravelSystems)
                {
                    var emission=ps.emission; emission.enabled=IsPiercingFlame&&hit>=0?at<hit+.36f:at<_flight && (hit<0 || at<hit);
                    ps.Simulate(step,false,false,false);
                }
                if(contactAt>=0 && at>=contactAt && contactAt<Life)
                {
                    if(!_fireImpacted)
                    {
                        _fireImpacted=true;_fireContactPoint=TargetPoint();_fireContact.position=_fireContactPoint;_fireContact.rotation=Quaternion.identity;
                        Vector3 axis=(_fireContactPoint-ReceivedOrigin).normalized;if(axis.sqrMagnitude<.001f)axis=Vector3.forward;
                        _firePatternFacing=Quaternion.LookRotation(axis,Mathf.Abs(axis.y)>.98f?Vector3.forward:Vector3.up);
                        _firePatternLocalPoint=ReceivedTarget!=null?ReceivedTarget.InverseTransformPoint(_fireContactPoint):_fireContactPoint;
                    }
                    if((IsAttachedFlame||IsEmberCharge)&&ReceivedTarget!=null)_fireContact.position=ReceivedTarget.TransformPoint(_firePatternLocalPoint);
                    if(_chargeHoldSystems!=null)foreach(var ps in _chargeHoldSystems)
                    {
                        ps.transform.parent.position=_fireContact.position;
                        var em=ps.emission;em.enabled=(hit<0||at<hit)&&at<Life-.5f;ps.Simulate(step,false,false,false);
                    }
                    foreach(var ps in _fireContactSystems)
                    {
                        if(Profile.UseOriginalKtp&&!IsAttachedFlame)continue;
                        if(IsEmberCharge&&(hit<0||at<hit))continue;
                        if(IsAttachedFlame){var em=ps.emission;em.enabled=at<Life-.65f;}
                        ps.Simulate(Mathf.Min(step,at-hit),false,false,false);
                    }
                }
                _fireSimulated=at;
                if(_splitFlames!=null&&hit>=0&&at>=hit)
                {
                    float elapsed=at-hit;
                    for(int i=0;i<2;i++)
                    {
                        float side=i==0?-1:1;var ps=_splitFlames[i];
                        var facing=_firePatternFacing*Quaternion.Euler(0,side*32,0);
                        ps.transform.SetPositionAndRotation(_fireContactPoint+facing*Vector3.forward*(elapsed*2.7f),facing);
                        var em=ps.emission;em.enabled=elapsed<.6f&&at<Life-.2f;
                        ps.Simulate(Mathf.Min(step,elapsed),false,false,false);
                    }
                }
            }
            _fireTravel.position=FireTravelPoint(end,hit);
            float after=Age-hit;
            FireHitPatternOpacity=_fireImpacted&&hit>=0&&Age<Life ? 1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.32f,.78f,after)):0;
            if(IsAttachedFlame&&_fireImpacted&&hit>=0&&Age<Life)FireHitPatternOpacity=(.85f+.15f*Mathf.Cos(after*3))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(Life-.65f,Life,Age)));
            if(IsEmberCharge&&_fireImpacted&&hit<0&&Age<Life)FireHitPatternOpacity=.8f;
            _fireHitPattern.enabled=FireHitPatternOpacity>.001f;
            if(_fireHitPattern.enabled)
            {
                Vector3 contact=ReceivedTarget!=null?ReceivedTarget.TransformPoint(_firePatternLocalPoint):_fireContactPoint;
                _fireHitPattern.transform.SetPositionAndRotation(contact-(_firePatternFacing*Vector3.forward)*.018f,_firePatternFacing);
                _fireHitPattern.transform.localScale=Vector3.one*Mathf.Lerp(.9f,1.5f,Mathf.SmoothStep(0,1,after/.14f));
                if(Profile.Glyph=="난")_fireHitPattern.transform.localScale*=1.65f;
                if(IsEmberCharge)_fireHitPattern.transform.localScale=Vector3.one*(hit<0?.48f:Mathf.Lerp(.48f,1.9f,Mathf.Clamp01(after/.18f)));
                _firePatternBlock.SetFloat("_Alpha",FireHitPatternOpacity);
                _firePatternBlock.SetFloat("_Erode",Mathf.Clamp01((after-.45f)/.4f));
                if(IsAttachedFlame||IsEmberCharge&&hit<0)_firePatternBlock.SetFloat("_Erode",0);
                _firePatternBlock.SetColor("_BaseColor",Color.Lerp(new Color(1.5f,.62f,.16f),new Color(.38f,.08f,.025f),Mathf.Clamp01(after/.65f)));
                _fireHitPattern.SetPropertyBlock(_firePatternBlock);
                if(Profile.UseOriginalKtp&&!IsAttachedFlame&&!(IsEmberCharge&&hit<0))_fireHitPattern.enabled=false;
            }
            if(Age>=Life)
            {
                foreach(var ps in _fireTravelSystems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                foreach(var ps in _fireContactSystems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                if(_chargeHoldSystems!=null)foreach(var ps in _chargeHoldSystems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
                if(_splitFlames!=null)foreach(var ps in _splitFlames)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
        void ClearFireBolt()
        {
            if(_fireBolt!=null){_fireBolt.SetActive(false);if(Application.isPlaying)Destroy(_fireBolt);else DestroyImmediate(_fireBolt);}
            _fireBolt=null;_fireTravel=null;_fireContact=null;_fireTravelSystems=null;_fireContactSystems=null;
            _fireHitPattern=null;_firePatternBlock=null;FireHitPatternOpacity=0;
            _chargeHoldSystems=null;_chargeDetonatedAt=-1;
            _splitFlames=null;
        }
    }
}
