from pathlib import Path
code=Path(__file__).with_name('sample_grip_surface.py').read_text()
code=code.replace("O=R/'Art/PlayerPhase1/PlaytestReRig'","O=R/'Art/PlaytestRecovery/Hands'").replace('Player_C02_ReRig','Player_C02_GripA_Rebuilt')
exec(compile(code,'recovery_hand_surface','exec'))
