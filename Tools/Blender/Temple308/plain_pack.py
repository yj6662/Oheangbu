"""Pack the baked metal (__M) and roughness (__R) maps of the plain kit pieces into <name>_MS.png (RGB metal, A smoothness)."""
from pathlib import Path
from PIL import Image
T = Path(__file__).resolve().parents[3] / 'Art/World/Temple308/Kit/Textures'
for m in sorted(T.glob('plain_*__M.png')):
    n = m.name[:-7]; r = T / (n + '__R.png')
    a = Image.open(m).convert('L'); b = Image.open(r).convert('L')
    Image.merge('RGBA', (a, a, a, b.point(lambda v: 255 - v))).save(T / (n + '_MS.png')); m.unlink(); r.unlink(); print('packed', n)
