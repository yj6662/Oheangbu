from pathlib import Path
r=Path('Oheangbu/Assets/_Project/Scripts/App/SpellVFX120');p=r/'Vfx120Effect.cs';s=p.read_text(encoding='utf-8');assert 'ConfirmOriginalImpact' not in s
s=s.replace('private void SampleNative()','''private readonly System.Collections.Generic.List<Vfx120TraditionalMotif> _originalContacts=new System.Collections.Generic.List<Vfx120TraditionalMotif>();
        private readonly System.Collections.Generic.List<float> _originalContactTimes=new System.Collections.Generic.List<float>();
        public bool ConfirmOriginalImpact(Vector3 worldPoint)
        {
            if(Profile==null||!Profile.UseOriginalKtp||Age>=Life||!Vfx120InterceptionMotion.Finite(worldPoint))return false;
            var fx=CreateNative(Profile.NativeImpactPrefab,Vfx120TraditionalMotif.Role.Impact,transform.InverseTransformPoint(worldPoint),1);
            if(fx==null)return false;_originalContacts.Add(fx);_originalContactTimes.Add(Age);return true;
        }
        private void SampleOriginalContacts()
        {
            for(int i=_originalContacts.Count-1;i>=0;i--){var fx=_originalContacts[i];if(fx==null){_originalContacts.RemoveAt(i);_originalContactTimes.RemoveAt(i);continue;}if(PreviewControlled)fx.Sample(Age-_originalContactTimes[i]);}
        }
        private void SampleNative()''')
s=s.replace('if (Profile.NativeImpactPrefab != null && impactAt >= 0','if (!(Profile.UseOriginalKtp&&IsFireAura(Profile)) && Profile.NativeImpactPrefab != null && impactAt >= 0')
s=s.replace('SampleFireCompanions();','SampleFireCompanions();\n            SampleOriginalContacts();')
s=s.replace('ReleaseOriginalTail(ref _nativeCast,false);','foreach(var contact in _originalContacts){var fx=contact;ReleaseOriginalTail(ref fx,false);}_originalContacts.Clear();_originalContactTimes.Clear();\n            ReleaseOriginalTail(ref _nativeCast,false);')
s=s.replace('ClearNative(ref _nativeCast); ClearNative(ref _nativeImpact);','foreach(var contact in _originalContacts){var fx=contact;ClearNative(ref fx);}_originalContacts.Clear();_originalContactTimes.Clear();\n            ClearNative(ref _nativeCast); ClearNative(ref _nativeImpact);')
p.write_text(s,encoding='utf-8')
p=r/'Vfx120Effect.FireAura.cs';s=p.read_text(encoding='utf-8').replace('_auraHitAt=Age;FireAuraContacts++;','_auraHitAt=Age;FireAuraContacts++;if(Profile.UseOriginalKtp)ConfirmOriginalImpact(point);').replace('if(_auraHitAt>=0&&at>=_auraHitAt)foreach','if(!Profile.UseOriginalKtp&&_auraHitAt>=0&&at>=_auraHitAt)foreach').replace('_auraMark.enabled=FireAuraMarkOpacity>0;','_auraMark.enabled=!Profile.UseOriginalKtp&&FireAuraMarkOpacity>0;');p.write_text(s,encoding='utf-8')
