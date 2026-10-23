import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('guard', Path(__file__).with_name('legacy_guard.py'))
guard = importlib.util.module_from_spec(spec)
spec.loader.exec_module(guard)

class LegacyGuardTests(unittest.TestCase):
    def test_content_add_remove_and_mode_changes_are_detected_without_emitting_secrets(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / 'cpa-core').mkdir()
            (root / 'oauth/nested').mkdir(parents=True)
            for name in ['cpa-core/cli-proxy-api', 'cpa-core/config.yaml', 'oauth/nested/account.json']:
                (root / name).write_text('sensitive-secret-value')
            baseline = guard.capture(root)
            self.assertNotIn('sensitive-secret-value', str(baseline))
            self.assertEqual(guard.compare(baseline, guard.capture(root)), {'added': 0, 'removed': 0, 'changed': 0})
            (root / 'cpa-core/config.yaml').write_text('changed')
            (root / 'oauth/new.json').write_text('new')
            (root / 'oauth/nested/account.json').unlink()
            self.assertEqual(guard.compare(baseline, guard.capture(root)), {'added': 1, 'removed': 1, 'changed': 1})
            stable = guard.capture(root)
            (root / 'cpa-core/cli-proxy-api').chmod(0o700)
            self.assertEqual(guard.compare(stable, guard.capture(root))['changed'], 1)

if __name__ == '__main__':
    unittest.main()
