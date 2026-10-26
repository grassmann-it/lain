import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('patch', ROOT/'scripts/clients/vscode_groups_patch.py')
patch = importlib.util.module_from_spec(spec)
spec.loader.exec_module(patch)


class PatchTests(unittest.TestCase):
    def test_unknown_bundle_structure_refused(self):
        for name in ['out/extension.js', 'webview/assets/model-queries-unknown.js',
                     'webview/assets/model-picker-options-unknown.js', 'webview/assets/access-row-unknown.js']:
            with self.subTest(name=name), self.assertRaises(ValueError):
                patch.patch_text(name, '// new build')

    def test_ambiguous_injection_refused(self):
        with self.assertRaises(ValueError):
            patch.replace_once('old old', 'old', 'new')

    def test_option_metadata_does_not_change_native_callback(self):
        original = 'return{disabled:A||U||n!=null,id:t.model,onSelect:()=>nativeSelect(t.model)}'
        changed = patch.patch_text('webview/assets/model-picker-options-test.js', original)
        self.assertIn('onSelect:()=>nativeSelect(t.model)', changed)
        self.assertIn('_lainGroups:t._lainGroups', changed)


if __name__ == '__main__':
    unittest.main()
