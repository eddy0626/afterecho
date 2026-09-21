"""Small, dependency-free safety helpers for offline chart generation."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import uuid
import wave


def sha256_file(path):
    h = hashlib.sha256()
    with Path(path).open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def fingerprint(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'),
                                    allow_nan=False).encode()).hexdigest()


def pcm_identity(path):
    with wave.open(str(path), 'rb') as audio:
        spec = {'sampleRate': audio.getframerate(), 'channels': audio.getnchannels(),
                'sampleWidth': audio.getsampwidth(), 'frames': audio.getnframes()}
        if (spec['sampleRate'], spec['channels'], spec['sampleWidth']) != (44100, 1, 2):
            raise ValueError('Expected canonical 44100 Hz mono 16-bit PCM')
        h = hashlib.sha256()
        sample_bytes = 0
        while True:
            block = audio.readframes(65536)
            if not block:
                break
            h.update(block)
            sample_bytes += len(block)
        if spec['frames'] <= 0:
            raise ValueError('Audio must contain samples')
        if sample_bytes != spec['frames'] * spec['channels'] * spec['sampleWidth']:
            raise ValueError('Audio sample data is truncated or inconsistent with its WAV header')
        return {**spec, 'pcmSHA256': h.hexdigest(),
                'duration': spec['frames'] / spec['sampleRate']}


def validate_cache(directory, identity):
    """Never silently accept old-format, partial, or changed-input results."""
    directory = Path(directory)
    if not directory.exists():
        return False
    try:
        manifest = json.loads((directory / 'generation-manifest.json').read_text())
        if manifest['fingerprint'] != fingerprint(identity) or manifest['identity'] != identity:
            raise ValueError('Input audio, environment, model or settings changed')
        files = manifest['files']
        required = {'track.wav'} | {f'{p}.{ext}' for p in ('easy', 'normal', 'hard')
                                   for ext in ('json', 'ibf')}
        if set(files) != required:
            raise ValueError('Incomplete generation artifacts')
        for name, digest in files.items():
            if sha256_file(directory / name) != digest:
                raise ValueError(f'Cached artifact changed: {name}')
    except (OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        raise ValueError('Unverifiable legacy or incomplete generation cache') from error
    return True


def publish_directory(staging, destination, replace=False):
    """Swap a validated generation as one directory; restore on a failed swap."""
    staging, destination = Path(staging), Path(destination)
    if not staging.is_dir() or staging.parent.resolve() != destination.parent.resolve():
        raise ValueError('Staging and destination must be sibling directories')
    backup = destination.with_name('.' + destination.name + '.backup-' + uuid.uuid4().hex)
    existed = destination.exists()
    if existed and not replace:
        raise ValueError('Output already exists; choose another output or explicit --replace')
    if existed:
        os.replace(destination, backup)
    try:
        os.replace(staging, destination)
    except BaseException:
        if existed:
            os.replace(backup, destination)
        raise
    if existed:
        shutil.rmtree(backup)
