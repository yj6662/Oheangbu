from original_scope import call
import time
deadline=time.monotonic()+90
while 'playing=False' not in call('Probe',None):
 if time.monotonic()>deadline:raise TimeoutError('Play still active')
 time.sleep(2)
for glyph in ['나','가','마','사','아']:
 print('CAPTURE '+glyph,flush=True)
 print(call('OriginalReview',glyph),flush=True)
