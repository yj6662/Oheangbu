"""Continuous bedrock with sparse, directional erosion channels.

No closed polygon cells: major joints terminate or merge into the mass.
The surface and its relief are continuous across the six export sectors.
"""
import math
from mathutils import Vector, noise


def build_facades(route):
    # Unequal spacings and inclinations prevent a repeated fence of ribs.
    joints = [(9, .10, 3.4, 7), (58, -.14, 2.0, 5),
              (104, .20, 4.5, 10), (159, -.08, 2.5, 6),
              (218, .16, 3.3, 9), (286, -.19, 2.0, 6),
              (333, .06, 3.8, 8)]

    def point(z, t):
        rx, ry, _ = route(max(0, min(330, z)))
        crest = 68 + 12*math.sin(z*.024) + 7*math.sin(z*.051+.7)
        y = -8 + t*(crest+8)
        height = max(0, y)
        foot = min(1, height/9)
        # Broad sloping rock shoulders. A clipped triangular waveform gives
        # planes with narrow weathered corners, not inflated round boulders.
        q = z + y*.16
        rib = 2/math.pi*math.asin(.985*math.sin(q*.069+.5))
        x = 4.5 + height*.12 + max(0, y-13)*.40
        x += (rib*3.1 + noise.noise(Vector((z*.025,y*.017,4)))*1.4)*foot
        for center, lean, width, depth in joints:
            distance = abs(z-center-lean*y-1.3*math.sin(y*.13+center))
            channel = max(0, 1-distance/width)
            # Grooves end at unequal heights; they do not encircle blocks.
            extent = max(0, min(1, (y-4)/11))*max(0,min(1,(72-y)/17))
            x += depth*channel*extent
        # Sparse shallow cross-breaks, restricted to localized outcrops.
        for cz, cy, span in [(42,24,17),(124,41,22),(238,18,14),(305,36,19)]:
            window = max(0, 1-abs(z-cz)/span)
            dy = y-cy-(z-cz)*.20
            x += .65*window*max(0,1-abs(dy)/.65)
        # Intermediate fractures belong to the same mass. Angular ridged
        # relief breaks up a broad face without outlining each individual tile.
        weather = noise.noise(Vector((z*.027,y*.036,9.3)))
        strata = noise.ridged_multi_fractal(Vector((z*.20+y*.055,y*.11,5.3)), .9, 2, 3, 1, 2)
        x += (strata-1)*1.45*foot*(.65+.35*weather)
        # A few inclined flake lips; their influence dies away along the face.
        for cz, cy, span, lean in [(30,14,12,.38),(72,34,20,-.17),
                                  (132,22,15,.31),(183,44,21,.19),
                                  (244,33,16,-.24),(314,18,17,.27)]:
            window=max(0,1-abs(z-cz)/span)
            dy=y-cy-(z-cz)*lean
            lip=max(0,1-abs(dy+2)/3) if dy<0 else 0
            x-=1.25*lip*window
        x += noise.fractal(Vector((z*.8,y*.8,7.1)),1,2,3)*.22
        # Embed the upper edge inside the continuous mountain, eliminating
        # the old plate tops and exposed card-like side walls.
        shoulder = max(0, (t-.72)/.28)
        x += shoulder*shoulder*35
        return rx+max(3.85,x), ry+y, z

    sectors=[]
    rows=96
    cols=100
    for sector in range(6):
        vs=[point(-24+sector*64+r*64/rows, c/cols)
            for r in range(rows+1) for c in range(cols+1)]
        fs=[]
        for r in range(rows):
            for c in range(cols):
                a=r*(cols+1)+c
                # Outward normal faces the road (-X).
                fs.extend([(a,a+cols+1,a+1),(a+1,a+cols+1,a+cols+2)])
        sectors.append((vs,fs))
    return sectors
