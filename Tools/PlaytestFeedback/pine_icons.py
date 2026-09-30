"""Three bounded Recraft requests; original SVGs and durable request records retained."""
import os, sys
from pathlib import Path
import generate_assets as gen

gen.OUT=gen.ROOT/'Art/World/PineRest/MenuIcons'
gen.OUT.mkdir(parents=True,exist_ok=True)
for line in (gen.ROOT/'.env').read_text(encoding='utf-8-sig').splitlines():
 if line.startswith('RECRAFT_API_KEY='):
  os.environ['RECRAFT_API_KEY']=line.split('=',1)[1].strip().strip('\"\'')
common=' Minimal Korean ink brush pictogram for a game menu. Extremely simple bold shape, only two to five confident strokes, subtle brush taper, legible at 32 pixels. Single black silhouette on pure white background, centered with generous margins. No lettering, no characters, no decorative motifs, no border, no texture speckles, no shading, no gradients, no scene. Flat vector icon.'
gen.UI={
 'resume':'One right-pointing arrowhead made with two broad tapered calligraphy strokes, an open chevron conveying continue.'+common,
 'save':'One tiny vertical traditional paper scroll, a blank rectangular sheet with a short rolled bar at top and bottom, shown flat from front. Empty center, thick minimal contour, conveying record.'+common,
 'exit':'One simple open wooden doorway with a short right-pointing arrow leaving the opening. Three thick strokes form the door frame, no lattice or roof, conveying exit.'+common,
}
if __name__=='__main__':
 for name in gen.UI:gen.run('ui',name)
