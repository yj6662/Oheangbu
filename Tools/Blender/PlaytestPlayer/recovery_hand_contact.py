from pathlib import Path
R=Path('C:/Users/yj666/Oheangbu')
code=Path(__file__).with_name('finalize_contact.py').read_text()
code=code.replace("O=R/'Art/PlayerPhase1/PlaytestReRig'","O=R/'Art/PlaytestRecovery/Hands'")
code=code.replace('Work/Player_C02_FingerRig.blend','Work/Player_C02_GripA_Local.blend').replace('Work/Player_C02_ReRig.blend','Work/Player_C02_GripA_Rebuilt.blend').replace('Exports/Player_C02_ReRig.fbx','Exports/Player_C02_GripA_Rebuilt.fbx')
exec(compile(code,'recovery_hand_contact','exec'))
