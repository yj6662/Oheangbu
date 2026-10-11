"""One frame a second of a trailer file, as a contact sheet.   python Tools/Art/trailer_sheet308.py <file.mp4> <sheet.jpg>"""
import sys
import imageio.v2 as iio
from PIL import Image
r = iio.get_reader(sys.argv[1]); fps = r.get_meta_data()['fps']; n = r.count_frames(); secs = int(n / fps)
cols = 6 if secs <= 36 else 10; rows = (secs + cols - 1) // cols; w, h = 400, 225
im = Image.new('RGB', (cols * w, rows * h))
for s in range(secs):
    im.paste(Image.fromarray(r.get_data(min(n - 1, int((s + .5) * fps)))).resize((w, h)), ((s % cols) * w, (s // cols) * h))
im.save(sys.argv[2], quality=85); print(secs, 's', n, 'frames at', fps)
