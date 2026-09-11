from pathlib import Path
R=Path(__file__).resolve().parents[2];S=R/'Oheangbu/Assets/_Project/Scripts'
def rename(s):return s.replace('FireHaetae','MetalTiger').replace('FIRE_HAETAE','METAL_TIGER').replace('Haetae','Tiger').replace('haetae','tiger').replace('놈','솜').replace('Nom','Som')
runtime=rename((S/'App/SpellVFX120/FireHaetaeVfx.cs').read_text())
runtime=runtime.replace('anchors.Count!=3','anchors.Count!=4').replace('Three planted paw','Four planted paw').replace('new Vector3[3]','new Vector3[4]').replace('new float[3]','new float[4]').replace('i<3','i<4').replace('j<3','j<4').replace('3.1f/diameter','3.8f/diameter')
start=runtime.index('   foreach(var ps in systems)\n   {\n    float local=')
end=runtime.index('\n  }\n  public Vector3[] FeetWorld',start)
runtime=runtime[:start]+'''   foreach(var ps in systems)
   {
    bool forming=ps.name=="FormationFilings";float local=forming?Age:Age-3.8f;
    if(local>=0&&!Ended&&(!forming||Age<1.2f))ps.Simulate(local,false,true,false);else ps.Clear();
   }'''+runtime[end:]
(S/'App/SpellVFX120/MetalTigerVfx.cs').write_text(runtime)
build=rename((S/'Editor/SpellVFX120/FireHaetaeBuild.cs').read_text())
build=build.replace('12532','12414').replace('new Vector3[3]','new Vector3[4]').replace('new int[3]','new int[4]').replace('c<3','c<4').replace('i<3','i<4').replace('Enumerable.Range(0,3)','Enumerable.Range(0,4)')
build=build.replace('three planted paw positions (the fourth paw is raised in the source pose)','four planted paw positions')
build=build.replace('"M_CharcoalEmber"','"M_BrushedSilver"').replace('"BaseColor","Normal","EmberMask"','"BaseColor","Normal"')
build=build.replace('mat.SetTexture("_EmberMask",AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Textures/T_MetalTiger_EmberMask.png"));','')
build=build.replace('AssetDatabase.LoadAssetAtPath<GameObject>(Vfx120Editor.AssetRoot+"/FixedWards/Pattern_1.prefab")','KtpQuickCastBuild.Pattern(Folder,3,true)')
build=build.replace('new GradientAlphaKey(.4f,.075f)','new GradientAlphaKey(.55f,.075f)').replace('new GradientAlphaKey(.18f,.28f)','new GradientAlphaKey(.3f,.28f)').replace('new GradientAlphaKey(.18f,.825f)','new GradientAlphaKey(.3f,.825f)')
start=build.index('    KtpOffsetGuardBuild.Debris(');end=build.index('    p.MetalTigerDebris=',start)
build=build[:start]+'''    KtpOffsetGuardBuild.Debris(debris,Folder,"FormationFilings",24,new Color(.68f,.73f,.76f),0,.6f,new Vector3(.014f,.055f,.005f),-.1f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveShavings",48,new Color(.58f,.64f,.68f),0,.65f,new Vector3(.012f,.065f,.005f),.22f);
    KtpOffsetGuardBuild.Debris(debris,Folder,"DissolveGlints",18,new Color(.95f,.98f,1f),0,.3f,new Vector3(.014f,.028f,.004f),.05f);
    foreach(var ps in debris.GetComponentsInChildren<ParticleSystem>())
    {
     var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(.18f,.6f);main.playOnAwake=false;
     var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(.8f,1.1f,2.2f);shape.position=new Vector3(0,.75f,0);
     if(ps.name=="FormationFilings"){shape.position=new Vector3(0,.05f,0);shape.scale=new Vector3(1.1f,.08f,2.5f);}
    }
''' + build[end:]
(S/'Editor/SpellVFX120/MetalTigerBuild.cs').write_text(build)
review=rename((S/'Editor/SpellVFX120/FireHaetaeReview.cs').read_text())
review=review.replace('Cleanup();clip=null;view++;Save();GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();','Cleanup();clip=null;view++;Save();GC.Collect();EditorUtility.UnloadUnusedAssetsImmediate();if(view==1){report.status="PLAY_COMPLETE_EXTERNAL_PENDING";Save();report=null;}')
(S/'Editor/SpellVFX120/MetalTigerReview.cs').write_text(review)
shader=rename((R/'Oheangbu/Assets/_Project/Shaders/FireHaetae.shader').read_text())
shader=shader.replace('  _EmberMask("Ember mask",2D)="black"{}\n','').replace('TEXTURE2D(_EmberMask);SAMPLER(sampler_EmberMask);','').replace('Charcoal','Silver')
shader=shader.replace('    float mask=SAMPLE_TEXTURE2D(_EmberMask,sampler_EmberMask,i.uv).r;\n','')
shader=shader.replace('color+=float3(1,.13,.012)*(mask*(.7+.08*sin(_Age*4))+edge*.7);','''float3 view=normalize(GetWorldSpaceViewDir(i.world));float3 halfDir=normalize(view+l.direction);
    float spec=pow(saturate(dot(n,halfDir)),48)*saturate(dot(n,l.direction))*l.shadowAttenuation;
    color+=float3(.65,.72,.78)*spec*.65*l.color+float3(.65,.75,.82)*edge*.28;''').replace('smoothstep(0,.045','smoothstep(0,.02')
(R/'Oheangbu/Assets/_Project/Shaders/MetalTiger.shader').write_text(shader)
print('METAL_TIGER_SCAFFOLDED')
