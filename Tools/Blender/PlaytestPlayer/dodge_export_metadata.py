from pathlib import Path
s=Path(__file__).with_name('recovery_export_metadata.py').read_text(encoding='utf-8')
s=s.replace('Art/PlaytestRecovery/Motion','Art/PlaytestRecovery/DodgeTurn/Motion').replace('Player_C02_AttachedMotions','Player_C02_DodgeRoll')
exec(compile(s,__file__,'exec'))
