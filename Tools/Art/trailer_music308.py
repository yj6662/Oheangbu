"""#308 trailer music through the ElevenLabs Music API (the user's key ELEVENLABSMUSIC_API_KEY in .env; the key is never printed).
usage: python Tools/Art/trailer_music308.py 30|90 [--take N]
Output: Art/Pitch308/Trailer/music/bgm308_<seconds>_<take>.mp3 and a ledger line in Art/Pitch308/Trailer/music/ledger.jsonl
Commercial terms of this music are NOT confirmed by the user yet (SPEC-TRAILER-308: TEST).
"""
import json
import sys
import time
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/Pitch308/Trailer/music'; OUT.mkdir(parents=True, exist_ok=True)

PROMPTS = {
    30: ('Instrumental trailer cue for a Korean ink-painting action game, 30 seconds, no vocals. Korean traditional instruments only: '
         'daegeum bamboo flute, gayageum, low buk and janggu drums, a deep sustained drone. '
         '0-4 s: one hard buk hit and a short gayageum run, then a held low note. 4-15 s: sparse daegeum melody over the drone, slow and wide. '
         '15-18 s: a fast drum roll that rushes forward and stops on one heavy hit. 18-24 s: driving janggu rhythm with plucked gayageum. '
         '24-27 s: the daegeum resolves the melody. 27-30 s: a single struck note that rings out to silence. Dark, restrained, cinematic, no synth pads, no choir.'),
    90: ('Instrumental trailer cue for a Korean ink-painting action game, 90 seconds, no vocals. Korean traditional instruments only: '
         'daegeum bamboo flute, gayageum, haegeum, low buk and janggu drums, a deep sustained drone. '
         '0-5 s: one hard buk hit and a short gayageum run, then a held low note. 5-19 s: near silence in a cave, a low drone, the first daegeum phrase as the space opens. '
         '19-34 s: an unhurried travelling theme, gayageum ostinato with daegeum above. 34-40 s: janggu rhythm drives in for a fight. '
         '40-56 s: the theme returns wider, haegeum joins. 56-67 s: tension builds, low drums come forward. '
         '67-70 s: a fast drum roll rushes forward and stops on one heavy hit. 70-81 s: full driving rhythm, the strongest passage. '
         '81-85 s: the daegeum resolves the melody alone. 85-90 s: a single struck note rings out to silence. Dark, restrained, cinematic, no synth pads, no choir.'),
}


def key():
    for line in (ROOT / '.env').read_text(encoding='utf-8').splitlines():
        if line.startswith('ELEVENLABSMUSIC_API_KEY'):
            return line.split('=', 1)[1].strip().strip('"').strip("'")
    raise SystemExit('ELEVENLABSMUSIC_API_KEY not in .env')


def main():
    seconds = int(sys.argv[1]); take = int(sys.argv[sys.argv.index('--take') + 1]) if '--take' in sys.argv else 1
    body = json.dumps({'prompt': PROMPTS[seconds], 'music_length_ms': seconds * 1000}).encode('utf-8')
    req = urllib.request.Request('https://api.elevenlabs.io/v1/music?output_format=mp3_44100_192', data=body, method='POST',
                                 headers={'xi-api-key': key(), 'Content-Type': 'application/json'})
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=600) as r:
            data = r.read(); status = r.status
    except urllib.error.HTTPError as e:
        print('HTTP', e.code, e.read().decode('utf-8', 'replace')[:600]); raise SystemExit(1)
    out = OUT / f'bgm308_{seconds}_{take}.mp3'; out.write_bytes(data)
    with (OUT / 'ledger.jsonl').open('a', encoding='utf-8') as f:
        f.write(json.dumps({'file': out.name, 'seconds': seconds, 'bytes': len(data), 'at': time.strftime('%Y-%m-%d %H:%M:%S'), 'prompt': PROMPTS[seconds]}, ensure_ascii=False) + '\n')
    print(out, len(data), 'bytes', status, '%.0f s' % (time.time() - t0))


if __name__ == '__main__':
    main()
