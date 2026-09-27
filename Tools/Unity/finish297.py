"""#297 finish pass on the #296 candidate (serialized through the playtest polish queue)."""
import argparse
from playtest_polish import call
if __name__ == '__main__':
    p = argparse.ArgumentParser(); p.add_argument('command'); p.add_argument('--wait', type=int, default=900); a = p.parse_args()
    call('Oheangbu.EditorTools.WorldMacro.CompactFinish297', 'Run', a.command, a.wait)
