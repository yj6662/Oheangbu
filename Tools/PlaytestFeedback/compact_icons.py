"""Five bounded, receipted Recraft vector requests for Compact UI."""
import os
import generate_assets as gen
gen.OUT=gen.ROOT/'Art/World/Compact/Rebuild/UIIcons'
gen.OUT.mkdir(parents=True,exist_ok=True)
for line in (gen.ROOT/'.env').read_text(encoding='utf-8-sig').splitlines():
 if line.startswith('RECRAFT_API_KEY='): os.environ['RECRAFT_API_KEY']=line.split('=',1)[1].strip().strip(chr(34)+chr(39))
common=' Minimal Korean ink brush pictogram, two to five bold tapered strokes, simple silhouette readable at 24 pixels. Single black on pure white. No text, border, shading, gradients, speckles or decoration. Flat vector.'
gen.UI={
 'mountain':'Two overlapping rounded mountain peaks, broad dry-brush base.'+common,
 'inn':'Small traditional Korean inn: one broad gently curved tiled roof above two posts, single open door.'+common,
 'cave':'Low irregular rock arch with a large empty opening and short path entering.'+common,
 'hand':'Simple open hand reaching sideways to touch an object, four joined fingers and a thumb.'+common,
 'heart':'Simple anatomical heart reduced to a rounded asymmetric brush silhouette with two short arteries.'+common}
if __name__=='__main__':
 for name in gen.UI:gen.run('ui',name)
