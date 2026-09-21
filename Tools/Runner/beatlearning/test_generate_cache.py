import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import wave
import zipfile
from cache_utils import fingerprint, pcm_identity, publish_directory, sha256_file, validate_cache
from generate import save_portable_ibf


class CacheSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.identity = {'audio': 'song-a', 'seed': 42, 'model': 'public-v1', 'params': {'temperature': .1}}
        self.output = self.root / 'raw'
        self.output.mkdir()
        for name in ['track.wav'] + [f'{p}.{ext}' for p in ('easy', 'normal', 'hard') for ext in ('json', 'ibf')]:
            (self.output / name).write_text(name)
        self.manifest = {'fingerprint': fingerprint(self.identity), 'identity': self.identity,
                         'files': {p.name: sha256_file(p) for p in self.output.iterdir()}}
        self.write_manifest()

    def tearDown(self):
        self.temp.cleanup()

    def write_manifest(self):
        (self.output / 'generation-manifest.json').write_text(json.dumps(self.manifest))

    def test_exact_cache_reused(self):
        self.assertTrue(validate_cache(self.output, self.identity))

    def test_different_audio_model_seed_and_params_rejected(self):
        for key, value in [('audio', 'song-b'), ('seed', 43), ('model', 'public-v2'), ('params', {'temperature': .2})]:
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_cache(self.output, {**self.identity, key: value})

    def test_edited_or_missing_artifact_rejected(self):
        (self.output / 'normal.json').write_text('edited')
        with self.assertRaises(ValueError):
            validate_cache(self.output, self.identity)
        (self.output / 'normal.json').unlink()
        with self.assertRaises(ValueError):
            validate_cache(self.output, self.identity)

    def test_legacy_cache_not_silently_reused(self):
        (self.output / 'generation-manifest.json').unlink()
        with self.assertRaises(ValueError):
            validate_cache(self.output, self.identity)

    def test_manifest_cannot_traverse_paths(self):
        self.manifest['files']['../outside'] = 'anything'
        self.write_manifest()
        with self.assertRaises(ValueError):
            validate_cache(self.output, self.identity)

    def test_failed_swap_restores_previous_generation(self):
        staging = self.root / '.staging'; staging.mkdir(); (staging / 'new').write_text('new')
        import os
        original_replace = os.replace
        def fail_stage(source, dest):
            if Path(source) == staging:
                raise OSError('simulated interrupted swap')
            original_replace(source, dest)
        with patch('cache_utils.os.replace', side_effect=fail_stage), self.assertRaises(OSError):
            publish_directory(staging, self.output, replace=True)
        self.assertTrue(validate_cache(self.output, self.identity))

    def test_replace_requires_explicit_flag(self):
        staging = self.root / '.staging'; staging.mkdir()
        with self.assertRaises(ValueError):
            publish_directory(staging, self.output)
        self.assertTrue(validate_cache(self.output, self.identity))

    def test_successful_swap_does_not_mix_generations(self):
        staging = self.root / '.staging'; staging.mkdir(); (staging / 'new').write_text('new')
        publish_directory(staging, self.output, replace=True)
        self.assertEqual([p.name for p in self.output.iterdir()], ['new'])

    def test_pcm_identity_tracks_audio_not_wav_container_headers(self):
        path = self.root / 'canonical.wav'
        with wave.open(str(path), 'wb') as audio:
            audio.setnchannels(1); audio.setsampwidth(2); audio.setframerate(44100)
            audio.writeframes(b'\0\0' * 4410)
        info = pcm_identity(path)
        self.assertEqual(info['frames'], 4410)
        self.assertEqual(info['duration'], .1)
        with wave.open(str(self.root / 'stereo.wav'), 'wb') as audio:
            audio.setnchannels(2); audio.setsampwidth(2); audio.setframerate(44100)
            audio.writeframes(b'\0\0\0\0' * 100)
        with self.assertRaises(ValueError):
            pcm_identity(self.root / 'stereo.wav')

    def test_truncated_pcm_does_not_produce_false_duration_identity(self):
        path = self.root / 'truncated.wav'
        with wave.open(str(path), 'wb') as audio:
            audio.setnchannels(1); audio.setsampwidth(2); audio.setframerate(44100)
            audio.writeframes(b'\0\0' * 100)
        path.write_bytes(path.read_bytes()[:-2])
        with self.assertRaisesRegex(ValueError, 'truncated'):
            pcm_identity(path)

    def test_ibf_audio_reference_survives_publish_and_package_move(self):
        staging = self.root / '.staging'; staging.mkdir()
        (staging / 'track.wav').write_bytes(b'canonical-audio')
        # A tiny stand-in implements the public IBF save contract; no model,
        # pandas, or checkpoint is loaded for this metadata portability check.
        class Draft:
            meta = {'audio': str(staging / 'track.wav'), 'tracks': ['LEFT']}
            data = b'unchanged-event-parquet'
            def save(self, path):
                with zipfile.ZipFile(path, 'w') as archive:
                    archive.writestr('meta.json', json.dumps(self.meta))
                    archive.writestr('data.pq', self.data)
        draft = Draft()
        save_portable_ibf(draft, staging / 'easy.ibf')
        self.assertEqual(draft.meta['audio'], str(staging / 'track.wav'))
        publish_directory(staging, self.output, replace=True)
        moved = self.root / 'moved-team-pack'; self.output.rename(moved)
        with zipfile.ZipFile(moved / 'easy.ibf') as archive:
            saved_meta = json.loads(archive.read('meta.json'))
            self.assertEqual(saved_meta['audio'], 'track.wav')
            self.assertEqual((moved / saved_meta['audio']).read_bytes(), b'canonical-audio')
            self.assertEqual(archive.read('data.pq'), draft.data)


if __name__ == '__main__':
    unittest.main()
