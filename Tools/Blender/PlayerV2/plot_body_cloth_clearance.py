"""Plot measured triangle cross-sections and exact capsule plane intersections."""
import json
from pathlib import Path
import numpy as np
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.collections import LineCollection
from matplotlib.lines import Line2D

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Art/PlayerV2/Inspect/ClothBlender/BodyClearance'
data = json.loads((OUT / 'actual-body-pin-clearance.json').read_text(encoding='utf-8'))
capsules = json.loads((ROOT / 'Art/PlayerV2/Inspect/ClothBlender/Inputs/anatomy-capsules-final-46655083.json').read_text(encoding='utf-8-sig'))['capsules']

for section in data['sections']:
    fig, axes = plt.subplots(1, 2, figsize=(14, 6), constrained_layout=True)
    pin = np.array(section['pinXY'])
    ranges = [(-.25, .25, -.14, .14), (pin[0]-.055, pin[0]+.055, pin[1]-.055, pin[1]+.055)]
    for ax, (xmin, xmax, ymin, ymax) in zip(axes, ranges):
        x, y = np.meshgrid(np.linspace(xmin, xmax, 500), np.linspace(ymin, ymax, 400))
        points = np.stack((x, y, np.full_like(x, section['z'])), axis=-1)
        depth = np.full(x.shape, -np.inf)
        for cap in capsules:
            a, b = np.array(cap['startBlender']), np.array(cap['endBlender'])
            d = b-a
            length2 = d @ d
            t = np.clip(np.sum((points-a)*d, axis=-1)/length2, 0, 1) if length2 > 1e-18 else np.zeros_like(x)
            depth = np.maximum(depth, cap['radiusMeters'] - np.linalg.norm(points-(a+t[..., None]*d), axis=-1))
        ax.contourf(x, y, depth, levels=[0, 1], colors=['#f6c978'], alpha=.20)
        ax.contour(x, y, depth, levels=[0], colors=['#c98211'], linewidths=1.4)
        for layer in section['layers']:
            name = layer['mesh']
            if 'Lining' in name:
                color, width, order = '#b72242', 2.3, 3
            elif name.endswith('BodyCore'):
                color, width, order = '#444444', 1.0, 2
            else:
                color, width, order = '#2863b1', 1.3, 4
            ax.add_collection(LineCollection(layer['segments'], colors=color, linewidths=width, zorder=order))
        ax.scatter(*pin, marker='x', color='black', s=90, linewidths=2.2, zorder=8)
        ax.set(xlim=(xmin, xmax), ylim=(ymin, ymax), xlabel='Blender X (m)', ylabel='Blender Y (m)', aspect='equal')
        ax.grid(alpha=.18)
    axes[0].set_title('Measured horizontal section')
    axes[1].set_title('Pinned vertex neighbourhood')
    title = f"{section['label']} | Z={section['z']:.6f} m | source {data['source_sha256'][:8]}"
    fig.suptitle(title, fontsize=14)
    handles = [Line2D([0], [0], color=c, lw=2, label=l) for c,l in [('#c98211','Capsule union'), ('#b72242','Actual closed inner lining'), ('#444444','Original open BodyCore'), ('#2863b1','Actual cloth')]]
    axes[0].legend(handles=handles, fontsize=9, loc='lower left')
    path = OUT / f"{section['label']}-actual-section.png"
    fig.savefig(path, dpi=160)
    plt.close(fig)
    print(path)
