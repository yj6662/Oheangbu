from pathlib import Path
r=Path('Oheangbu/Assets/_Project/Scripts');p=r/'App/SpellVFX120/KtpContactEffect.cs';s=p.read_text(encoding='utf-8');assert 'ApplyElementColor' not in s
s=s.replace('private static bool Finite(Vector3 v)', '''public void ApplyElementColor(Color pigment)
        {
            foreach(var ps in _systems){var m=ps.main;m.startColor=Vfx120TraditionalMotif.Neutral(m.startColor);var c=ps.colorOverLifetime;if(c.enabled)c.color=Vfx120TraditionalMotif.Neutral(c.color);var b=ps.colorBySpeed;if(b.enabled)b.color=Vfx120TraditionalMotif.Neutral(b.color);}
            float peak=Mathf.Max(.001f,Mathf.Max(pigment.r,Mathf.Max(pigment.g,pigment.b)));
            foreach(var renderer in Content.GetComponentsInChildren<Renderer>(true))
            {
                var mats=renderer.sharedMaterials;for(int i=0;i<mats.Length;i++){var mat=mats[i];if(mat==null)continue;var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block,i);
                    foreach(var prop in new[]{"_BaseColor","_Color","_TintColor","_EmissionColor"})if(mat.HasProperty(prop)){Color c=mat.GetColor(prop);float v=Mathf.Max(c.r,Mathf.Max(c.g,c.b));block.SetColor(prop,new Color(v*pigment.r/peak,v*pigment.g/peak,v*pigment.b/peak,c.a));}
                    renderer.SetPropertyBlock(block,i);
                }
            }
        }
        private static bool Finite(Vector3 v)''');p.write_text(s,encoding='utf-8')
p=r/'App/SpellVFX120/Vfx120TraditionalMotif.cs';s=p.read_text(encoding='utf-8').replace('private static ParticleSystem.MinMaxGradient Neutral','internal static ParticleSystem.MinMaxGradient Neutral');p.write_text(s,encoding='utf-8')
p=r/'App/CombatLoopWiring.cs';s=p.read_text(encoding='utf-8').replace('public float ImpactTime;\n            public float Power;', 'public float ImpactTime;\n            public Element Element;\n            public float Power;')
s=s.replace('Power = power });','Power = power, Element=plan.Cast.Element });')
s=s.replace('if (pending.Target != null && pending.Target.IsAlive) pending.Target.TakeDamage(pending.Power);','''if (pending.Target != null && pending.Target.IsAlive)
                {
                    var point=pending.Target.transform.position+Vector3.up*.8f;
                    bool positive=pending.Power*pending.Target.DamageMultiplier>0;
                    pending.Target.TakeDamage(pending.Power);
                    if(positive&&_contactVfx!=null)SpawnContact(_contactVfx.ParrySource(pending.Element),point,_contactVfx.ParryScale,pending.Element);
                }''')
s=s.replace('impactPoint, scale);','impactPoint, scale,guardElement);').replace('private bool SpawnContact(GameObject source, Vector3 point, float scale)','private bool SpawnContact(GameObject source, Vector3 point, float scale, Element? element=null)').replace('_contacts.Add(effect);','if(element.HasValue&&_palette!=null)effect.ApplyElementColor(_palette.GetBaseColor(InitialOf(element.Value)));\n            _contacts.Add(effect);')
p.write_text(s,encoding='utf-8')
