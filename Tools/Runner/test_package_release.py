"""Filter-only package tests: never create or update a release ZIP."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import package_release as package


class PackageFilterTests(unittest.TestCase):
    def test_generator_contains_reproduction_code_rules_and_original_candidates(self):
        for name in ('prepare_charts.py', 'select_charts.py', 'selection_rules.json',
                     'requirements.txt', 'README.md', 'sources/manifest.json',
                     'sources/raw/easy.json', 'sources/prior_timing.json',
                     'sources/stage.json', 'beatlearning/generate.py',
                     'beatlearning/cache_utils.py', 'beatlearning/requirements-lock.txt',
                     'beatlearning/LICENSE'):
            with self.subTest(name=name):
                self.assertTrue(package.generator_file_allowed(name))
                self.assertTrue(package.source_file_allowed('Tools/Runner/' + name))

    def test_cached_models_environments_and_staging_never_enter_either_bundle(self):
        for name in ('beatlearning/cache/weights/quaver_beart_v1.pt',
                     'beatlearning/cache/BeatLearning/.git/config',
                     'beatlearning/.venv/lib/site-packages/torch/model.py',
                     'beatlearning/venv/bin/python', 'beatlearning/generated/raw/easy.json',
                     'beatlearning/raw/easy.json', 'beatlearning/.raw-staging-123/easy.json',
                     'beatlearning/experiment_output/stale_generate.py',
                     'beatlearning/.raw.backup-123/track.wav',
                     'staging/sources/easy.json', '.runner-staging-abc/easy.json',
                     'sources/.git/config', 'sources/__pycache__/data.pyc',
                     'beatlearning/model.pt', 'beatlearning/quaver.onnx',
                     'sources/track.wav', 'sources/song.m4a', 'sources/song.flac',
                     'sources/original-audio.mp3', 'sources/data.npz'):
            with self.subTest(name=name):
                self.assertFalse(package.generator_file_allowed(name))
                self.assertFalse(package.source_file_allowed('Tools/Runner/' + name))

    def test_source_keeps_existing_editor_and_unity_assets_policy(self):
        for name in ('Tools/Runner/finalize_art.cs', 'Tools/unity-local',
                     'Assets/Afterecho/Resources/Afterecho/Audio/Untitled.wav',
                     'Assets/Art/Models/runner.fbx', 'Assets/Art/Env/room.png',
                     'Assets/Feel/LICENSE.txt', 'Packages/manifest.json',
                     'ProjectSettings/ProjectSettings.asset'):
            with self.subTest(name=name):
                self.assertTrue(package.source_file_allowed(name))
        for name in ('Assets/_Recovery/old.unity', 'Assets/StreamingAssets/private.bin',
                     'Assets/PerformanceTestRun-123.json', 'Tools/Runner/debug.log',
                     'Assets/.DS_Store'):
            with self.subTest(name=name):
                self.assertFalse(package.source_file_allowed(name))

    def test_web_streaming_assets_are_not_removed_by_unity_source_filter(self):
        self.assertTrue(package.common_file_allowed('WebGL/StreamingAssets/runtime.dat'))
        self.assertTrue(package.common_file_allowed('WebGL/Build/runner.data'))
        self.assertFalse(package.source_file_allowed('Assets/StreamingAssets/runtime.dat'))

    def test_walk_prunes_environments_and_never_follows_external_symlinks(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder) / 'tools'; root.mkdir()
            for name in ('prepare_charts.py', 'sources/raw/easy.json',
                         'beatlearning/cache/large.json', 'beatlearning/.venv/model.py'):
                path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('x')
            outside = Path(folder) / 'outside'; outside.mkdir(); (outside / 'secret.py').write_text('x')
            (root / 'external.py').symlink_to(outside / 'secret.py')
            (root / 'sources' / 'linked').symlink_to(outside, target_is_directory=True)
            found = {p.relative_to(root).as_posix() for p in package.iter_files(root, package.generator_file_allowed, tooling=True)}
            self.assertEqual(found, {'prepare_charts.py', 'sources/raw/easy.json'})

    def test_team_walk_can_ignore_stale_generator_files(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name in ('WebGL/index.html', 'Generator/old.py', 'Generator/cache/model.pt', 'Review/notes.json'):
                path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('x')
            found = {p.relative_to(root).as_posix() for p in package.iter_files(root, skip_directories={'Generator'})}
            self.assertEqual(found, {'WebGL/index.html', 'Review/notes.json'})

    def test_import_has_no_packaging_side_effects(self):
        spec = importlib.util.spec_from_file_location('package_release_import_check', package.__file__)
        module = importlib.util.module_from_spec(spec)
        with patch('pathlib.Path.mkdir') as mkdir, patch('shutil.copytree') as copy, patch('zipfile.ZipFile') as archive:
            spec.loader.exec_module(module)
        mkdir.assert_not_called(); copy.assert_not_called(); archive.assert_not_called()

    def test_generated_review_runs_are_excluded_but_review_evidence_stays(self):
        for name in ('staging/run/easy.json', 'chart-backups/run/hard.json'):
            with self.subTest(name=name):
                self.assertFalse(package.review_file_allowed(name))
                self.assertFalse(package.team_file_allowed('Review/' + name))
                self.assertFalse(package.source_file_allowed('PlaytestExports/Runner/' + name))
        for name in ('easy-original.json', 'core-tests.txt', 'reviewQueue.json',
                     'review-notes/approved.json', 'review-notes/staging/example.json'):
            with self.subTest(name=name):
                self.assertTrue(package.review_file_allowed(name))
                self.assertTrue(package.team_file_allowed('Review/' + name))
                self.assertTrue(package.source_file_allowed('PlaytestExports/Runner/' + name))

    def test_stale_generated_review_folders_in_delivery_cannot_enter_zip_file_list(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for name in ('WebGL/index.html', 'Review/staging/old/easy.json',
                         'Review/chart-backups/old/normal.json', 'Review/human-review.json',
                         'Review/easy-original.json', 'WebGL/staging/runtime.dat'):
                path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text('x')
            selected = {p.relative_to(root).as_posix() for p in package.iter_files(
                root, package.team_file_allowed,
                skip_relative_directories={'Review/' + name for name in package.GENERATED_REVIEW_DIRECTORIES})}
            self.assertEqual(selected, {'WebGL/index.html', 'Review/human-review.json',
                                        'Review/easy-original.json', 'WebGL/staging/runtime.dat'})


if __name__ == '__main__':
    unittest.main()
