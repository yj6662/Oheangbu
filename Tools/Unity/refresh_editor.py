"""Refresh via the project's existing local Editor queue; no remote credentials needed."""
import json
import time
import uuid
from pathlib import Path

root = Path(__file__).resolve().parents[2]
output = root / 'Art/SpellVFX120'
command = output / 'command.json'
if command.exists():
    raise RuntimeError('An existing Unity command is pending; it was not overwritten.')
identifier = uuid.uuid4().hex
temporary = output / ('refresh_' + identifier + '.tmp')
temporary.write_text(json.dumps({'id': identifier, 'method': 'Refresh', 'request': ''}), encoding='utf-8')
temporary.rename(command)
response = output / ('response_' + identifier + '.json')
deadline = time.monotonic() + 120
while time.monotonic() < deadline:
    if response.exists():
        result = json.loads(response.read_text(encoding='utf-8-sig'))
        print(json.dumps(result))
        if result.get('status') != 'COMPLETE':
            raise SystemExit(1)
        break
    time.sleep(.5)
else:
    raise TimeoutError('Refresh not acknowledged; inspect the pending queue before retrying.')
