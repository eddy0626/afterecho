#!/usr/bin/env python3
"""Explicit offline AI draft generation; never changes Unity Resources or publishes."""
import argparse
import importlib.metadata
import json
import math
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
from cache_utils import fingerprint, pcm_identity, publish_directory, sha256_file, validate_cache

ROOT = Path(__file__).resolve().parent
REVISION = 'e17503f8cb6c21d6d9dee6a410537a7aa9261dfb'
MODEL_HASH = '38e57556b96cc23ab5a2ccfb32e71ae0fa9ea049da61093477cb5d343694b3c5'
PARAMS = {'use_tracks': ['LEFT'], 'audio_start': 0.0, 'beams': [2, 2, 2, 2],
          'max_beam_width': 16, 'temperature': 0.1, 'top_k': 1}
# Preserve the existing baseline; these are four discrete model buckets, not a slider.
PRESETS = [('easy', .15, 69420), ('normal', .45, 69421), ('hard', .75, 69422)]


def environment():
    result = {'python': '.'.join(map(str, sys.version_info[:3]))}
    for name in ('torch', 'numpy', 'librosa', 'soundfile', 'soxr', 'pandas', 'dill'):
        try:
            result[name] = importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:
            result[name] = 'not-installed'
    return result


def checkout_source(cache):
    source = cache / 'BeatLearning'
    if not source.exists():
        subprocess.run(['git', 'clone', 'https://github.com/sedthh/BeatLearning.git', str(source)], check=True)
    # Never erase a developer's experimental decoder changes.
    dirty = subprocess.check_output(['git', '-C', str(source), 'status', '--porcelain'], text=True)
    if dirty.strip():
        raise ValueError('Cached BeatLearning checkout has changes; use a separate clean --cache')
    subprocess.run(['git', '-C', str(source), 'checkout', '--detach', REVISION], check=True)
    if subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip() != REVISION:
        raise ValueError('BeatLearning source revision mismatch')
    return source


def save_portable_ibf(ibf, destination):
    """Keep the generated event data, but do not retain the temporary audio path.

    BeatLearning's converter resolves relative audio paths beside the input IBF.
    The sibling track.wav is published with this file and survives moving the pack.
    """
    original_audio = ibf.meta['audio']
    try:
        ibf.meta['audio'] = 'track.wav'
        ibf.save(str(destination))
    finally:
        ibf.meta['audio'] = original_audio


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--audio', type=Path, required=True, help='Explicit source song; no HTML extraction')
    parser.add_argument('--output', type=Path, required=True, help='Draft output folder; creates raw/ only')
    parser.add_argument('--cache', type=Path, default=ROOT / 'cache')
    parser.add_argument('--replace', action='store_true', help='Replace mismatched drafts only after all three succeed')
    args = parser.parse_args(argv)
    if not args.audio.is_file():
        parser.error('Source audio does not exist')
    args.output.mkdir(parents=True, exist_ok=True)
    target = args.output / 'raw'
    staging = Path(tempfile.mkdtemp(prefix='.raw-staging-', dir=args.output))
    try:
        wav = staging / 'track.wav'
        ffmpeg_version = subprocess.check_output(['ffmpeg', '-version'], text=True).splitlines()[0]
        subprocess.run(['ffmpeg', '-nostdin', '-y', '-v', 'error', '-i', str(args.audio),
                        '-map', '0:a:0', '-vn', '-ar', '44100', '-ac', '1', '-c:a', 'pcm_s16le', str(wav)], check=True)
        audio = {'sourceSHA256': sha256_file(args.audio), **pcm_identity(wav)}
        identity = {'schema': 'afterecho-generation-v2', 'audio': audio, 'codeRevision': REVISION,
                    'modelSHA256': MODEL_HASH, 'params': PARAMS,
                    'presets': [{'name': n, 'difficulty': d, 'seed': s} for n, d, s in PRESETS],
                    'holdTokensDisabled': list(range(9, 60)), 'environment': environment(),
                    'ffmpeg': ffmpeg_version, 'generatorSHA256': sha256_file(__file__),
                    'cacheHelperSHA256': sha256_file(ROOT / 'cache_utils.py')}
        try:
            if validate_cache(target, identity):
                print(json.dumps({'status': 'reused_verified_cache', 'fingerprint': fingerprint(identity), 'path': str(target)}))
                return 0
        except ValueError:
            if not args.replace:
                raise
        # Heavy libraries / network are required only for an actual new inference run.
        import torch
        import numpy as np
        from huggingface_hub import hf_hub_download
        args.cache.mkdir(parents=True, exist_ok=True)
        source = checkout_source(args.cache)
        sys.path.insert(0, str(source))
        from beatlearning.configs import QuaverBEaRT
        from beatlearning.tokenizers import BEaRTTokenizer
        from beatlearning.models import BEaRT
        torch.set_num_threads(4)
        torch.serialization.add_safe_globals([(np._core.multiarray.scalar, 'numpy.core.multiarray.scalar'),
                                              np.dtype, np.dtypes.Float64DType, np.dtypes.Float32DType])
        checkpoint = hf_hub_download(repo_id='sedthh/BeatLearning', filename='quaver_beart_v1.pt',
                                     local_dir=str(args.cache / 'weights'))
        if sha256_file(checkpoint) != MODEL_HASH:
            raise ValueError('Public model hash changed; existing draft output is preserved')
        model = BEaRT(BEaRTTokenizer(QuaverBEaRT()))
        model.load(checkpoint, map_location=torch.device('cpu'), weights_only=True)
        model.to('cpu'); model.eval()
        for name, difficulty, seed in PRESETS:
            started = time.monotonic()
            ibf = model.generate(audio_file=str(wav), difficulty=difficulty, random_seed=seed,
                                 logit_bias={i: -float('Inf') for i in range(9, 60)}, **PARAMS)
            notes = [{'id': f'AI-{name}-{i+1:04d}', 'time': float(t)/1000}
                     for i, t in enumerate(ibf.data.loc[ibf.data.LEFT == 1, 'TIME'].tolist())]
            if not notes or any(not math.isfinite(n['time']) or not 0 <= n['time'] <= audio['duration'] for n in notes):
                raise ValueError(f'{name}: invalid generated note times')
            if any(b['time'] <= a['time'] for a, b in zip(notes, notes[1:])):
                raise ValueError(f'{name}: duplicate or unsorted generated note times')
            save_portable_ibf(ibf, staging / f'{name}.ibf')
            meta = {'generator': 'BeatLearning public model (no fine tuning)',
                    'repository': 'https://github.com/sedthh/BeatLearning', 'revision': REVISION,
                    'model': 'quaver_beart_v1.pt', 'modelSHA256': MODEL_HASH,
                    'difficulty': difficulty, 'modelBucket': ['EASY', 'NORMAL', 'HARD', 'INSANE'][min(3, int(4*difficulty))],
                    'seed': seed, 'torch': torch.__version__, 'device': 'cpu', 'audio': audio,
                    'holdsDisabledTokens': list(range(9, 60)), 'params': PARAMS,
                    'runFingerprint': fingerprint(identity), 'elapsedSeconds': round(time.monotonic()-started, 2),
                    'review': {'musicalAndOneHand': 'team_pending', 'trainingEligible': False}, 'notes': notes}
            (staging / f'{name}.json').write_text(json.dumps(meta, ensure_ascii=False, indent=2)+'\n')
        files = {p.name: sha256_file(p) for p in staging.iterdir() if p.is_file()}
        manifest = {'fingerprint': fingerprint(identity), 'identity': identity, 'files': files}
        (staging / 'generation-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
        validate_cache(staging, identity)
        publish_directory(staging, target, replace=args.replace)
        print(json.dumps({'status': 'generated_unreviewed_drafts', 'fingerprint': fingerprint(identity), 'path': str(target)}))
        return 0
    finally:
        if staging.exists():
            shutil.rmtree(staging)


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        print('Generation stopped; previous output preserved: '+str(error), file=sys.stderr)
        raise SystemExit(1)
