import copy
import importlib.util
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import tempfile

spec = importlib.util.spec_from_file_location('engine', Path(__file__).with_name('build_pack.py'))
engine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(engine)

class PackValidationTests(unittest.TestCase):
    def setUp(self):
        self.base = engine.ROOT / 'packs/demo'
        self.config = json.loads((self.base / 'pack.json').read_text(encoding='utf-8'))

    def test_sample_and_legacy_save_keys(self):
        engine.validate(self.config, self.base)
        # A user's sample may have been renamed; verify the known legacy IDs
        # as an explicit fixture rather than imposing them on editable content.
        self.config.update(id='music_card_one', receiptKey='delivery_gift_v2')
        code = engine.generate_runtime(self.config)
        self.assertIn('"music_card_one"', code)
        self.assertIn('"delivery_gift_v2"', code)
        self.assertIn('new TapeDefinition("02", "cassette10")', code)

    def test_sorting_retains_track_keys(self):
        original = {t['audio']: t['key'] for t in self.config['tracks']}
        self.config['tracks'].reverse()
        engine.validate(self.config, self.base)
        self.assertEqual(original, {t['audio']: t['key'] for t in self.config['tracks']})

    def test_reject_duplicate_keys(self):
        self.config['tracks'][1]['key'] = self.config['tracks'][0]['key']
        with self.assertRaises(ValueError): engine.validate(self.config, self.base)

    def test_reject_path_escape(self):
        self.config['tracks'][0]['audio'] = '../../README.md'
        with self.assertRaises(ValueError): engine.validate(self.config, self.base)

    def test_reject_install_path_id(self):
        self.config['id'] = '../other_mod'
        with self.assertRaises(ValueError): engine.validate(self.config, self.base)

    def test_pack_ids_generate_distinct_assemblies(self):
        first = engine.generate_runtime(self.config)
        self.config['id'] = 'second_pack'
        second = engine.generate_runtime(self.config)
        self.assertNotEqual(first, second)

    def test_install_backs_up_and_replaces(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            game, package = root / 'game', root / 'package'
            old = game / 'Mods/test_pack'
            old.mkdir(parents=True)
            package.mkdir()
            (old / 'manifest.xml').write_text('<Manifest><ID>test_pack</ID></Manifest>')
            (old / 'old.dll').write_bytes(b'previous version')
            (package / 'manifest.xml').write_text('<Manifest><ID>test_pack</ID></Manifest>')
            (package / 'new.dll').write_bytes(b'new version')
            with patch.object(engine.subprocess, 'check_output', return_value=''):
                engine.install(package, game, 'test_pack')
            self.assertTrue((old / 'new.dll').exists())
            self.assertFalse((old / 'old.dll').exists())
            self.assertEqual(len(list((game / 'CassetteStudioBackups').glob('*/old.dll'))), 1)

    def test_failed_install_restores_previous_package(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            game, package = root / 'game', root / 'package'
            old = game / 'Mods/test_pack'
            old.mkdir(parents=True)
            package.mkdir()
            (old / 'manifest.xml').write_text('<Manifest><ID>test_pack</ID></Manifest>')
            (old / 'old.dll').write_bytes(b'previous version')
            original_rename = Path.rename
            def fail_stage(path, target):
                if path.name.startswith('.cassette-studio-stage-'):
                    raise OSError('Simulated locked installation')
                return original_rename(path, target)
            with patch.object(engine.subprocess, 'check_output', return_value=''), patch.object(Path, 'rename', fail_stage):
                with self.assertRaises(OSError): engine.install(package, game, 'test_pack')
            self.assertEqual((old / 'old.dll').read_bytes(), b'previous version')

    def test_running_game_blocks_install(self):
        with patch.object(engine.subprocess, 'check_output', return_value='"Probably Stolen.exe","123"'):
            with self.assertRaises(ValueError): engine.install(Path('unused'), Path('unused'), 'test_pack')

if __name__ == '__main__': unittest.main()
