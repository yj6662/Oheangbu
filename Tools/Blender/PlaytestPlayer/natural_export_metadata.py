from pathlib import Path
s=Path(__file__).with_name('recovery_export_metadata.py').read_text()
s=s.replace('Art/PlaytestRecovery/Motion','Art/PlaytestRecovery/NaturalLocomotion/Motion').replace('Player_C02_AttachedMotions','Player_C02_NaturalLocomotion')
s=s.replace("a.name=='PT_CrouchLeft'","a.name.endswith('Left')").replace("a.name=='PT_CrouchRight'","a.name.endswith('Right')").replace("a.name=='PT_CrouchBack'","a.name.endswith('Back')")
s=s.replace("moving=a.name in ['PT_WalkForward','PT_RunForward','PT_CrouchForward','PT_CrouchBack','PT_CrouchLeft','PT_CrouchRight']","moving=a.name.startswith(('PT_Walk','PT_Run','PT_Crouch'))")
exec(compile(s,__file__,'exec'))
