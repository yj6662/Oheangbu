"""#305 field enclosure phase 1 (SPEC-WORLD-ENCLOSURE-305), serialized through the playtest polish queue.
commands: status | build-scene:<scene> | remove-scene:<scene> | check-scene:<scene> | audit-scene:<scene>"""
import argparse
from playtest_polish import call
if __name__ == '__main__':
    p = argparse.ArgumentParser(); p.add_argument('command'); p.add_argument('--wait', type=int, default=900); a = p.parse_args()
    call('Oheangbu.EditorTools.WorldMacro.Enclosure305', 'Run', a.command, a.wait)
