"""Transport for first-section authoring and bounded checks; never automatic walking."""
import sys
import world_macro
world_macro.OUT = world_macro.OUT / 'Playtest'
if __name__ == '__main__':
    world_macro.call(sys.argv[1], sys.argv[2] if len(sys.argv)>2 else '')
