"""One-shot original KTP mode migration; refuses a repeated application."""
from pathlib import Path
r=Path('Oheangbu/Assets/_Project/Scripts/App/SpellVFX120')
p=r/'Vfx120TraditionalMotif.cs';s=p.read_text(encoding='utf-8');assert 'PreserveAuthored' not in s
s=s.replace('public bool HierarchyScaling;', 'public bool HierarchyScaling;\n            public bool PreserveAuthored;')
s=s.replace('if (SourceSystemCount > settings.MaxSystems || SourceSystemCount > settings.MaxParticles)','if (!settings.PreserveAuthored && (SourceSystemCount > settings.MaxSystems || SourceSystemCount > settings.MaxParticles))')
s=s.replace('Age = 0; IsComplete = false; _startedAt = Time.time;', '''if(settings.PreserveAuthored)
            {
                _settings.HoldAt=-1;
                float cycle=0,tail=0;
                foreach(var ps in sourceSystems){var m=ps.main;float speed=Mathf.Max(.001f,m.simulationSpeed);cycle=Mathf.Max(cycle,(m.duration+CurveMaximum(m.startDelay))/speed);tail=Mathf.Max(tail,CurveMaximum(m.startLifetime)/speed);}
                foreach(var animator in sourceAnimators)foreach(var clip in animator.runtimeAnimatorController.animationClips)if(clip!=null)cycle=Mathf.Max(cycle,clip.length);
                _settings.FadeSeconds=Mathf.Max(.1f,tail);
                _releaseAt=settings.Sustain?float.PositiveInfinity:Mathf.Max(.1f,cycle);
                _endAt=_releaseAt+_settings.FadeSeconds;
            }
            Age = 0; IsComplete = false; _startedAt = Time.time;''')
s=s.replace('AllocateBudget(settings.MaxParticles, totalDemand);','if(settings.PreserveAuthored){foreach(var row in _layers){row.Capacity=row.System.main.maxParticles;ParticleBudget+=row.Capacity;}}\n                else AllocateBudget(settings.MaxParticles, totalDemand);')
s=s.replace('foreach (var light in _instance.GetComponentsInChildren<Light>(true)) light.enabled = false;','if(!settings.PreserveAuthored)foreach (var light in _instance.GetComponentsInChildren<Light>(true)) light.enabled = false;')
s=s.replace('float opacity = 1 - dry;','float opacity = _settings.PreserveAuthored?1:1-dry;')
s=s.replace('float sourceValue = Mathf.Min(1.35f, Mathf.Max(original.r, Mathf.Max(original.g, original.b)));','float sourceValue = _settings.PreserveAuthored?Mathf.Max(original.r,Mathf.Max(original.g,original.b)):Mathf.Min(1.35f, Mathf.Max(original.r, Mathf.Max(original.g, original.b)));')
s=s.replace('slot.Block.SetColor(slot.Properties[i], value);','if(_settings.PreserveAuthored)value=new Color(sourceValue*_pigment.r/peak,sourceValue*_pigment.g/peak,sourceValue*_pigment.b/peak,original.a);\n                    slot.Block.SetColor(slot.Properties[i], value);')
s=s.replace('ClearInstance(); IsComplete = true; Status = "RUNTIME_ENDED";','ClearInstance(); IsComplete = true; Status = "RUNTIME_ENDED";\n                if(DestroyHostOnCompletion)Destroy(gameObject);')
s=s.replace('public bool PreviewControlled =>','public bool DestroyHostOnCompletion {get;set;}\n        public bool PreserveAuthored => _settings.PreserveAuthored;\n        public bool PreviewControlled =>')
p.write_text(s,encoding='utf-8')
p=r/'Vfx120Profile.cs';s=p.read_text(encoding='utf-8').replace('public GameObject NativeCastPrefab;', 'public bool UseOriginalKtp;\n        public GameObject NativeCastPrefab;');p.write_text(s,encoding='utf-8')
p=r/'Vfx120Effect.cs';s=p.read_text(encoding='utf-8').replace('options.Lifetime = lifetime;','options.PreserveAuthored=Profile.UseOriginalKtp;\n            options.Sustain=Profile.UseOriginalKtp&&(role==Vfx120TraditionalMotif.Role.Shield||role==Vfx120TraditionalMotif.Role.Summon);\n            options.Lifetime = lifetime;')
s=s.replace('if (_nativeImpactAttempted || Profile.NativeImpactPrefab == null','if (Profile.UseOriginalKtp && !PreviewControlled) return;\n            if (_nativeImpactAttempted || Profile.NativeImpactPrefab == null')
s=s.replace('else ClearNative();','else if(Profile.UseOriginalKtp)ReleaseOriginalTails();\n                else ClearNative();')
s=s.replace('if (Age >= Life)\n            {\n                if (PreviewControlled)','if (Age >= Life)\n            {\n                if(Profile.UseOriginalKtp&&PreviewControlled){if(_nativeField!=null)_nativeField.Release();return;}\n                if (PreviewControlled)')
# Releasing natural tails detaches presentation only, without extending the spell/gameplay root.
s=s.replace('private void ClearNative()\n', '''private void ReleaseOriginalTails()
        {
            ReleaseOriginalTail(ref _nativeCast,false);ReleaseOriginalTail(ref _nativeImpact,false);ReleaseOriginalTail(ref _nativeField,true);
        }
        private static void ReleaseOriginalTail(ref Vfx120TraditionalMotif motif,bool release)
        {
            if(motif==null)return;if(release)motif.Release();
            if(motif.IsComplete){ClearNative(ref motif);return;}
            motif.transform.SetParent(null,true);motif.DestroyHostOnCompletion=true;motif=null;
        }
        private void ClearNative()
''')
p.write_text(s,encoding='utf-8')
