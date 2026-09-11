from pathlib import Path
r=Path('Oheangbu/Assets/_Project/Scripts/App/SpellVFX120')
p=r/'Vfx120Effect.FireCompanions.cs';s=p.read_text(encoding='utf-8').replace('FirePelletContacts++;return true;','FirePelletContacts++;if(Profile.UseOriginalKtp)ConfirmOriginalImpact(point);return true;').replace('if(s.Impact>=0&&at>=s.Impact)foreach','if(!Profile.UseOriginalKtp&&s.Impact>=0&&at>=s.Impact)foreach').replace('s.Mark.enabled=opacity>0;','s.Mark.enabled=opacity>0&&(!Profile.UseOriginalKtp||s.Impact<0);');p.write_text(s,encoding='utf-8')
p=r/'Vfx120Effect.CompanionSeeds.cs';s=p.read_text(encoding='utf-8').replace('CompanionHitMask|=1<<index;return true;','CompanionHitMask|=1<<index;if(Profile.UseOriginalKtp)ConfirmOriginalImpact(point);return true;').replace('float markFade=hit?', 'float markFade=hit&&!Profile.UseOriginalKtp?');p.write_text(s,encoding='utf-8')
# Hide replaced impact overlays after all custom sampling, not the model or persistent status marks.
p=r/'Vfx120Effect.cs';s=p.read_text(encoding='utf-8').replace('SampleOriginalContacts();','SampleOriginalContacts();\n            if(Profile.UseOriginalKtp&&IsBambooBolt(Profile)){if(_boltInk!=null)_boltInk.enabled=false;if(_boltLeaves!=null)foreach(var leaf in _boltLeaves)if(leaf!=null)leaf.enabled=false;}')
s=s.replace('!(Profile.UseOriginalKtp&&IsFireAura(Profile))','!(Profile.UseOriginalKtp&&(IsFireAura(Profile)||IsFireCompanions(Profile)||IsCompanionSeeds(Profile)))')
# Preview-only synthetic impact is limited to attacks. Buff/heal/environment casts do not invent enemy hits.
s=s.replace('return PreviewControlled && DemonstrationCues ? _flight : -1;', 'return PreviewControlled && DemonstrationCues && (!Profile.UseOriginalKtp || Profile.Behavior==Vfx120Behavior.Projectile || Profile.Behavior==Vfx120Behavior.Bind || Profile.Behavior==Vfx120Behavior.Burst || Profile.Behavior==Vfx120Behavior.Wave || IsFireBolt(Profile) || IsFireJet(Profile)) ? _flight : -1;')
p.write_text(s,encoding='utf-8')
