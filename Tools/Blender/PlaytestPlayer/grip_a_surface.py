"""Repeat dense triangle-surface sampling on the NEW A export, never reuse an old PASS."""
from pathlib import Path
source=Path(__file__).with_name('sample_grip_surface.py').read_text()
source=source.replace("O=R/'Art/PlayerPhase1/PlaytestReRig'","O=R/'Art/PlaytestPolish/Hands'").replace('Player_C02_ReRig','Player_C02_GripA')
exec(compile(source,'grip_a_surface_kernel','exec'))
