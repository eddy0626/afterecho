#!/usr/bin/env python3
"""Deterministic candidate selection, not a learned model or a listening approval.

All model times and previous edits survive in the source bundle and reports.
No live writes occur until --apply. A/B raw-time outputs are never applicable.
"""
import argparse
import bisect
import copy
import csv
from datetime import datetime
import hashlib
import io
import json
import math
import os
from pathlib import Path
import shutil
import sys
import tempfile
import wave

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
DATA = Path('Assets/Afterecho/Resources/Afterecho')
PRESETS = ('easy', 'normal', 'hard')
VERSION = 'runner-selector-v2'
EPS = 1e-8


class SelectionError(ValueError):
    pass


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, sort_keys=True, indent=2,
                       allow_nan=False) + '\n').encode()


def sha(value):
    return hashlib.sha256(value).hexdigest()


def read(path):
    return json.loads(Path(path).read_text())


def finite(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def require(condition, message):
    if not condition:
        raise SelectionError(message)


def pcm_identity(path):
    with wave.open(str(path), 'rb') as w:
        pcm = w.readframes(w.getnframes())
        identity = dict(sha256=sha(pcm), sampleRate=w.getframerate(), channels=w.getnchannels(),
                        sampleWidth=w.getsampwidth(), frames=w.getnframes(),
                        duration=w.getnframes() / w.getframerate())
    return identity, pcm


def verify_sources(source, audio):
    manifest = read(source / 'manifest.json')
    require(manifest['format'] == 'afterecho-runner-source-bundle-v1', 'Unknown source bundle')
    raw = {}
    for preset in PRESETS:
        info = manifest['rawFiles'][preset]
        path = source / info['path']
        require(path.resolve().is_relative_to(source.resolve()), 'Source path escapes bundle')
        require(sha(path.read_bytes()) == info['sha256'], 'Raw source hash mismatch: ' + preset)
        raw[preset] = read(path)
        require(len(raw[preset]['notes']) == info['notes'], 'Raw source note count mismatch')
    require(sha((source / 'prior_timing.json').read_bytes()) == manifest['priorTimingSHA256'],
            'Prior timing hash mismatch')
    require(sha((source / 'stage.json').read_bytes()) == manifest['stageSHA256'], 'Stage hash mismatch')
    identity, pcm = pcm_identity(audio)
    require(all(identity[k] == manifest['pcm'][k] for k in identity), 'PCM identity mismatch; regenerate a bound source bundle')
    require(identity['sampleWidth'] == 2, 'Only signed 16-bit PCM is supported')
    return manifest, raw, read(source / 'prior_timing.json'), read(source / 'stage.json'), identity, pcm


def collect_reviews(charts):
    """Only the current, time-matching human decision can lock or reject a note."""
    times, locks, rejected, history, warnings = {}, {}, set(), {}, []
    edges = {}
    # Merge append histories as a partial order, not preset concatenation. This
    # preserves newer invalidations even when another difficulty contains an old copy.
    for preset in PRESETS:
        previous = None
        for event in charts[preset].get('humanReviews', []):
            key = event.get('id') or 'legacy-' + sha(encoded(event))
            require(key not in history or history[key] == event, 'Review ID has conflicting evidence: ' + key)
            history[key] = copy.deepcopy(event)
            edges.setdefault(key, set())
            if previous and previous != key:
                edges[previous].add(key)
            previous = key
    indegree = {key: 0 for key in history}
    for next_keys in edges.values():
        for key in next_keys:
            indegree[key] += 1
    ordered = []
    while len(ordered) < len(history):
        available = [k for k, degree in indegree.items() if degree == 0]
        require(available, 'Conflicting review append histories; resolve before selecting')
        key = min(available, key=lambda k: (history[k].get('reviewedUtc') or '', k))
        ordered.append(history[key])
        indegree[key] = -1
        for following in edges[key]:
            indegree[following] -= 1
    global_latest = {r.get('noteId'): r for r in ordered}
    for preset in PRESETS:
        chart = charts[preset]
        events = chart.get('humanReviews', [])
        by_id = {r.get('id'): r for r in events if r.get('id')}
        for note in chart['notes']:
            nid, at = note['id'], note['time']
            require(finite(at), 'Nonfinite live note time: ' + nid)
            if nid in times:
                require(abs(times[nid] - at) <= EPS, 'Shared note has conflicting live times: ' + nid)
            times[nid] = at
            decision = note.get('teamReview', 'pending')
            if decision not in ('approved', 'rejected'):
                continue
            event = by_id.get(note.get('humanReviewId'))
            latest = next((r for r in reversed(events) if r.get('noteId') == nid), None)
            try:
                datetime.fromisoformat(event.get('reviewedUtc', '').replace('Z', '+00:00'))
                timestamp_valid = True
            except (ValueError, TypeError, AttributeError):
                timestamp_valid = False
            source_time = None if note.get('source') == 'manual_edit' else note.get('originalTime')
            valid = (event and event is latest and event == global_latest.get(nid) and event.get('decision') == decision
                     and event.get('noteId') == nid and finite(event.get('reviewedTime'))
                     and event['reviewedTime'] == at and event.get('sourceTime') == source_time
                     and timestamp_valid and str(event.get('reviewer') or '').strip()
                     and str(event.get('reason') or '').strip()
                     and (decision != 'approved' or event.get('humanConfirmed') is True))
            if not valid:
                warnings.append(dict(preset=preset, noteId=nid, reason='stale or incomplete human decision; not locked'))
                continue
            if decision == 'approved':
                locks.setdefault(nid, {'note': copy.deepcopy(note), 'event': copy.deepcopy(event), 'presets': []})['presets'].append(preset)
            else:
                rejected.add(nid)
    require(not (set(locks) & rejected), 'Conflicting current approved/rejected decisions across difficulties')
    return dict(times=times, locks=locks, rejected=rejected,
                history=ordered, warnings=warnings)


def attack_features(pcm, identity):
    samples = np.frombuffer(pcm, dtype='<i2').reshape(-1, identity['channels']).astype(np.float64) / 32768
    hop = max(1, round(identity['sampleRate'] * .005))
    # Channel energies are averaged, not waveforms: stereo phase cancellation cannot erase an attack.
    power = np.mean(samples * samples, axis=1)
    rms = np.sqrt(power[:len(power) // hop * hop].reshape(-1, hop).mean(axis=1))
    flux = np.maximum(0, np.diff(rms, prepend=0))
    scale = max(1e-9, float(np.quantile(flux, .95)))

    def strength(at):
        index = round(at * identity['sampleRate'] / hop)
        a, b = max(0, index - 5), min(len(flux), index + 6)
        value = float(flux[a:b].max()) if b > a else 0.
        return round(value, 6), min(1000, round(1000 * value / scale))
    return strength


def beat_index(at, stage):
    return max(0, bisect.bisect_right(stage['beats'], at) - 1)


def halfbeat_grid(stage):
    beats = stage['beats']
    return sorted(beats + [(a+b)/2 for a, b in zip(beats, beats[1:])])


def beat_proximity(at, grid):
    index = bisect.bisect_left(grid, at)
    nearest = min(range(max(0, index-1), min(len(grid), index+1)), key=lambda i: abs(grid[i]-at))
    intervals = ([grid[nearest]-grid[nearest-1]] if nearest else []) + ([grid[nearest+1]-grid[nearest]] if nearest+1 < len(grid) else [])
    return max(0., 1. - 2. * abs(grid[nearest]-at) / min(intervals))


def make_candidates(raw, prior, reviews, stage, config, strength, raw_comparison=False):
    rows, excluded, seen = [], [], set()
    for preset in PRESETS:
        for note in raw[preset]['notes']:
            nid, raw_time = note['id'], note['time']
            require(nid not in seen and finite(raw_time), 'Duplicate ID or invalid raw candidate: ' + nid)
            seen.add(nid)
            record = prior['records'].get(nid)
            require(record and abs(record['rawTime'] - raw_time) <= EPS, 'Missing/mismatched provenance: ' + nid)
            previous = reviews['times'].get(nid, record['priorTime'])
            at = raw_time if raw_comparison else previous
            require(finite(previous) and finite(at), 'Invalid prior timing: ' + nid)
            rows.append(dict(id=nid, rawTime=raw_time, priorTime=previous, time=at,
                             sourcePreset=preset, source='BeatLearning', legacyDeletionReasons=record.get('legacyDeletionReasons', [])))
    for nid, lock in reviews['locks'].items():
        if nid not in seen:
            note = lock['note']
            require(note.get('source') in ('manual_edit', 'manual'), 'Unknown locked source not present in bundle: ' + nid)
            rows.append(dict(id=nid, rawTime=note.get('originalTime', note['time']), priorTime=note['time'],
                             time=note['time'], sourcePreset=lock['presets'][0], source='manual_edit', legacyDeletionReasons=[]))
    require(not raw_comparison or not reviews['locks'], 'Raw comparison cannot override approved locks; use Lab A/B')
    # Raw-time event clusters link evidence, but never average or move any candidate.
    rows.sort(key=lambda n: (n['rawTime'], n['id']))
    groups = []
    for row in rows:
        if not groups or row['rawTime'] - groups[-1][0]['rawTime'] > config['eventToleranceSeconds'] + EPS:
            groups.append([])
        groups[-1].append(row)
    for group in groups:
        anchor = 'A-' + sha(encoded(sorted(n['id'] for n in group)))[:12]
        for row in group:
            row['anchorId'] = anchor
    rejected_times = {row['time'] for row in rows if row['id'] in reviews['rejected']}
    rejected_anchors = {row['anchorId'] for row in rows if row['id'] in reviews['rejected']}
    by_time = {}
    for row in rows:
        reason = None
        if row['time'] in rejected_times or row['anchorId'] in rejected_anchors:
            reason = 'current human rejection of this event or one of its source aliases'
        elif row['time'] < config['firstNoteSeconds'] - EPS:
            reason = 'first note requires full 1.2 second preview'
        elif row['time'] > stage['duration'] - config['endingMarginSeconds'] + EPS:
            reason = 'final judgement safety margin'
        if reason:
            require(row['id'] not in reviews['locks'], 'Human lock conflicts with boundary: ' + row['id'])
            excluded.append(dict(**row, reason=reason))
        else:
            by_time.setdefault(row['time'], []).append(row)
    candidates = []
    for at, aliases in sorted(by_time.items()):
        locked = [n for n in aliases if n['id'] in reviews['locks']]
        require(len(locked) <= 1, 'Multiple locked IDs at the same time; resolve explicitly')
        canonical = copy.deepcopy(locked[0] if locked else min(aliases, key=lambda n: (PRESETS.index(n['sourcePreset']), n['id'])))
        canonical['aliases'] = aliases
        canonical['support'] = len(set(n['sourcePreset'] for n in aliases))
        canonical['strength'], canonical['strengthRank'] = strength(at)
        canonical['phrase'] = int(beat_index(at, stage) // config['phraseBeats'])
        candidates.append(canonical)
    return candidates, excluded


def note_windows(times, maximum):
    return [min([maximum] + ([.4 * (t - times[i-1])] if i else [])
                + ([.4 * (times[i+1] - t)] if i + 1 < len(times) else [])) for i, t in enumerate(times)]


def visible_peak(times, windows, preview):
    # Closed intervals: a start is processed before an end at the same timestamp.
    events = [(round(t-preview, 9), 0, 1) for t in times]
    events += [(round(t+w, 9), 1, -1) for t, w in zip(times, windows)]
    current = peak = 0
    for _, _, change in sorted(events):
        current += change
        peak = max(peak, current)
    return peak


def can_append(times, at, rules, preview):
    if not times:
        return True
    gap = at - times[-1]
    if gap < rules['minGap'] - EPS:
        return False
    burst = 1
    for i in range(len(times) - 1, 0, -1):
        if times[i] - times[i-1] >= rules['fastGap']:
            break
        burst += 1
    if burst >= rules['burstLimit'] and gap < rules['recoveryGap'] - EPS:
        return False
    # Config validation ensures maxWindow <= .4*minGap, hence all legal-note windows are maxWindow.
    cap = rules['maxVisibleNotes']
    if len(times) >= cap and at - times[-cap] <= preview + rules['maxWindow'] + EPS:
        return False
    return True


def valid_times(times, rules, preview):
    prefix = []
    for at in times:
        if not finite(at) or not can_append(prefix, at, rules, preview):
            return False
        prefix.append(at)
    return visible_peak(times, note_windows(times, rules['maxWindow']), preview) <= rules['maxVisibleNotes']


def validate_config(config):
    require(config.get('format') == 'afterecho-runner-selection-rules-v2', 'Unknown selection rules')
    for name in ('previewSeconds', 'firstNoteSeconds', 'endingMarginSeconds', 'phraseBeats', 'beamWidth'):
        require(finite(config.get(name)) and config[name] > 0, 'Invalid configuration: ' + name)
    require(config['firstNoteSeconds'] >= max(1.2, config['previewSeconds']), 'First note must allow the runtime 1.2 second introduction and full preview')
    require(isinstance(config['beamWidth'], int), 'beamWidth must be an integer')
    for preset in PRESETS:
        rules = config['difficulties'][preset]
        for name in ('minGap', 'maxWindow', 'fastGap', 'recoveryGap', 'burstLimit', 'maxVisibleNotes', 'targetNps'):
            require(finite(rules.get(name)) and rules[name] > 0, 'Invalid ' + preset + ' rule: ' + name)
        require(rules['maxWindow'] <= .4 * rules['minGap'] + EPS, 'Window violates neighbor bound')
        require(rules['recoveryGap'] >= rules['fastGap'] >= rules['minGap'], 'Inconsistent burst rules')
        require(isinstance(rules['maxVisibleNotes'], int) and isinstance(rules['burstLimit'], int), 'Caps must be integers')
        require(rules['maxVisibleNotes'] <= 12, 'Visible cap exceeds the runtime ring pool')


def select_notes(candidates, rules, config, stage, mandatory=(), reserved=()):
    """Bounded chronological beam scores whole phrase alternatives, not local pair deletion.

    Mandatory lower-difficulty anchors and human locks must survive. Reserved higher-
    difficulty locks are checked before pruning so the beam cannot make them impossible.
    """
    required = set(mandatory)
    require(required <= {n['id'] for n in candidates}, 'Required note absent from candidates')
    mandatory_times = sorted(n['time'] for n in candidates if n['id'] in required)
    require(valid_times(mandatory_times, rules, config['previewSeconds']), 'Required notes violate difficulty constraints')
    states = [(0, ())]
    scoring = config['scoring']
    phrase_length = stage['beat'] * config['phraseBeats']
    target = max(1, round(phrase_length * rules['targetNps']))
    grid = halfbeat_grid(stage)
    for index, note in enumerate(candidates):
        options = []
        required_here = note['id'] in required
        future = [t for t in mandatory_times if t > note['time'] + EPS]
        for score, chosen in states:
            if not required_here:
                options.append((score, chosen))
            times = [candidates[j]['time'] for j in chosen]
            if not can_append(times, note['time'], rules, config['previewSeconds']):
                continue
            extended = times + [note['time']]
            if future and not valid_times(extended + future, rules, config['previewSeconds']):
                continue
            if any(not valid_times(sorted(set(extended + lock_times)), harder, config['previewSeconds'])
                   for harder, lock_times in reserved):
                continue
            phrase_count = sum(candidates[j]['phrase'] == note['phrase'] for j in chosen)
            gain = scoring['candidateBase'] + scoring['sourceConsensus'] * (note['support'] - 1)
            gain += round(scoring['attackStrength'] * note['strengthRank'] / 1000)
            gain += round(scoring['beatProximity'] * beat_proximity(note['time'], grid))
            if len(times) >= 2 and abs((note['time'] - times[-1]) - (times[-1] - times[-2])) <= .035:
                gain += scoring['repeatedInterval']
            if phrase_count >= target:
                gain -= scoring['overPhraseTarget']
            options.append((score + gain, chosen + (index,)))
        require(options, 'No feasible selection; inspect human locks at ' + str(note['time']))
        options.sort(key=lambda x: (-x[0], x[1]))
        states = options[:config['beamWidth']]
    result = [candidates[i] for i in states[0][1]]
    require(required <= {n['id'] for n in result}, 'Selection lost mandatory notes')
    return result


def validate_outputs(charts, config, stage, reviews):
    validate_config(config)
    previous = set()
    summaries = []
    for preset in PRESETS:
        chart = charts[preset]
        rules, notes = config['difficulties'][preset], chart['notes']
        times = [n['time'] for n in notes]
        require(chart['format'] == 'afterecho-chart-v1' and chart['gameplayMode'] == 'runner', 'Invalid output format')
        require(len(notes) >= 24 and len({n['id'] for n in notes}) == len(notes), 'Too few notes or duplicate IDs')
        require(valid_times(times, rules, config['previewSeconds']), 'Output constraint failure: ' + preset)
        require(times[0] >= config['firstNoteSeconds'] - EPS and times[-1] <= stage['duration'] - config['endingMarginSeconds'] + EPS, 'Output boundary failure')
        pairs = {(n['id'], n['time']) for n in notes}
        require(previous <= pairs, 'Difficulties are not nested')
        require(all(abs(n['window'] - w) < 1e-8 for n, w in zip(notes, note_windows(times, rules['maxWindow']))), 'Stored window mismatch')
        for nid, lock in reviews['locks'].items():
            if any(PRESETS.index(p) <= PRESETS.index(preset) for p in lock['presets']):
                require((nid, lock['note']['time']) in pairs, 'Human approved lock lost: ' + nid)
        for note in notes:
            require(note.get('teamReview') != 'approved' or (note['id'] in reviews['locks']
                    and preset in reviews['locks'][note['id']]['presets']), 'Cannot manufacture or transfer difficulty approval')
        require(chart['review']['trainingEligible'] is False, 'Selection is never training approval')
        summaries.append(dict(preset=preset, notes=len(notes), first=times[0], last=times[-1],
                              minGap=min(b-a for a, b in zip(times, times[1:])),
                              maxVisible=visible_peak(times, note_windows(times, rules['maxWindow']), config['previewSeconds']),
                              inheritedNotes=len(previous), inheritedRate=1.0 if previous else None,
                              first15Combo=times[14]))
        previous = pairs
    return summaries


def create_outputs(candidates, excluded, live, prior, manifest, stage, config, reviews, run_id, raw_comparison):
    charts, queue, decisions, previous = {}, [], [], set()
    for preset in PRESETS:
        rules = config['difficulties'][preset]
        locks = {nid for nid, lock in reviews['locks'].items() if preset in lock['presets']}
        reserved = []
        for later in PRESETS[PRESETS.index(preset)+1:]:
            later_times = [lock['note']['time'] for lock in reviews['locks'].values() if later in lock['presets']]
            if later_times:
                reserved.append((config['difficulties'][later], later_times))
        chosen = select_notes(candidates, rules, config, stage, previous | locks, reserved)
        chosen_ids = {n['id'] for n in chosen}
        chart = copy.deepcopy(live[preset])
        chart.update(format='afterecho-chart-v1', gameplayMode='runner', preset=preset,
                     runnerRevision=VERSION, selectionRunId=run_id, encounters=[], humanReviews=reviews['history'])
        chart.pop('modifiedUtc', None)
        chart['rules'] = dict(rules, health=100, previewSeconds=config['previewSeconds'])
        chart['origin']['selection'] = dict(method=VERSION, timingPolicy='raw_comparison_only' if raw_comparison else config['timingPolicy'],
            candidateSources={p: manifest['rawFiles'][p]['origin'] for p in PRESETS}, noTraining=True)
        chart['review'] = dict(technical='automated_checked', musicalAndOneHand='team_pending', trainingEligible=False)
        chart['edits'] = copy.deepcopy(live[preset].get('edits', prior['edits'][preset]))
        chart['runnerEdits'] = copy.deepcopy(live[preset].get('runnerEdits', prior['runnerEdits'][preset]))
        chart['runnerEdits'].append(dict(action='selection_revision', runId=run_id, reason='runner-only nested phrase selection; prior timing retained; human listening pending'))
        windows = note_windows([n['time'] for n in chosen], rules['maxWindow'])
        notes = []
        for candidate, window in zip(chosen, windows):
            nid = candidate['id']
            lock = reviews['locks'].get(nid)
            approved_here = bool(lock and preset in lock['presets'])
            at = candidate['time']
            current_beat = beat_index(at, stage)
            section = [s['code'] for s in stage['sections'] if s['fromBeat'] <= current_beat][-1]
            note = dict(id=nid, time=at, originalTime=candidate['rawTime'], rawTime=candidate['rawTime'],
                        parentTime=candidate['priorTime'], priorTime=candidate['priorTime'], source=candidate['source'],
                        sourcePreset=candidate['sourcePreset'], anchorId=candidate['anchorId'], phraseId='P%03d' % candidate['phrase'],
                        section=section, role='run', window=round(window, 9),
                        selectionReason='inherited lower-difficulty anchor' if nid in previous else 'phrase beam: source agreement, measured attack and repeated rhythm',
                        timingPolicy='human_locked' if lock else ('raw_comparison_only' if raw_comparison else config['timingPolicy']),
                        reviewStatus='human_approved_locked' if approved_here else 'selection_pending', teamReview='approved' if approved_here else 'pending',
                        reviewCandidates=[dict(kind='raw_model', time=candidate['rawTime'], approval='not_inferred'),
                                          dict(kind='prior_curated', time=candidate['priorTime'], approval='not_inferred')],
                        sourceCandidates=[dict(id=a['id'], preset=a['sourcePreset'], rawTime=a['rawTime'], priorTime=a['priorTime']) for a in candidate['aliases']])
            if approved_here:
                note['humanReviewId'] = lock['event']['id']
                note['selectionReason'] = 'current time-matching human approval locked'
            else:
                queue.append(dict(preset=preset, noteId=nid, anchorId=note['anchorId'], phraseId=note['phraseId'],
                    time=at, originalTime=candidate['rawTime'], parentTime=candidate['priorTime'], deltaMs=round((at-candidate['rawTime'])*1000, 3),
                    priority='timing_exceeds_window' if abs(at-candidate['rawTime']) > window + EPS else 'musical_and_one_hand',
                    status='pending', reason='A/B audition is required; attack proxy does not certify timing'))
            notes.append(note)
        chart['notes'] = notes
        charts[preset] = chart
        decisions.extend(dict(preset=preset, noteId=n['id'], time=n['time'], selected=n['id'] in chosen_ids,
                              sourceCandidates=[a['id'] for a in n['aliases']],
                              reason='selected phrase combination' if n['id'] in chosen_ids else 'not selected under phrase score, spacing, burst, visibility and nested anchors') for n in candidates)
        previous = chosen_ids
    summaries = validate_outputs(charts, config, stage, reviews)
    for summary in summaries:
        old = {n['id']: n['time'] for n in live[summary['preset']]['notes']}
        summary['existingSameIdRetimed'] = sum(n['id'] in old and abs(n['time']-old[n['id']]) > EPS for n in charts[summary['preset']]['notes'])
        summary['existingSameIdRetained'] = sum(n['id'] in old for n in charts[summary['preset']]['notes'])
        new_times = {n['time'] for n in charts[summary['preset']]['notes']}
        summary['existingTimingPositionsRetained'] = sum(at in new_times for at in old.values())
        summary['sourceIdsRepresented'] = len({a['id'] for n in charts[summary['preset']]['notes'] for a in n['sourceCandidates']})
    return charts, dict(validation=summaries, reviewQueue=queue, candidateDecisions=decisions,
                        excludedCandidates=excluded, reviewWarnings=reviews['warnings'])


def stage_selection(root=ROOT, source_dir=None, output=None, raw_comparison=False, review_charts=None):
    root = Path(root)
    source = Path(source_dir or HERE / 'sources').resolve()
    config = read(HERE / 'selection_rules.json')
    validate_config(config)
    manifest, raw, prior, stage, identity, pcm = verify_sources(source, root / DATA / 'Audio/Untitled.wav')
    require(sha((root / DATA / 'stage.json').read_bytes()) == manifest['stageSHA256'], 'Live stage differs from source bundle')
    live_files = {p: root / DATA / 'RunnerCharts' / (p + '.json') for p in PRESETS}
    live_hashes = {p: sha(path.read_bytes()) for p, path in live_files.items()}
    live = {p: read(path) for p, path in live_files.items()}
    review_inputs = copy.deepcopy(live)
    for preset, path in (review_charts or {}).items():
        require(preset in PRESETS, 'Unknown review preset: ' + preset)
        document = read(path)
        require(document.get('format') == 'afterecho-chart-v1' and document.get('gameplayMode') == 'runner'
                and document.get('preset') == preset, 'Review chart format or preset mismatch: ' + str(path))
        require(document.get('song', {}).get('id') == manifest['song']['id']
                and document.get('song', {}).get('duration') == stage['duration'], 'Review chart belongs to a different song')
        review_inputs[preset] = document
    reviews = collect_reviews(review_inputs)
    inputs = dict(version=VERSION, pipelineSHA256=sha(Path(__file__).read_bytes()), rulesSHA256=sha(encoded(config)),
                  sourceManifestSHA256=sha((source / 'manifest.json').read_bytes()), stageSHA256=manifest['stageSHA256'],
                  liveChartSHA256=live_hashes, reviewChartSHA256={p: sha(encoded(review_inputs[p])) for p in PRESETS},
                  pcm=identity, numpyVersion=np.__version__, rawComparison=raw_comparison)
    run_id = sha(encoded(inputs))
    candidates, excluded = make_candidates(raw, prior, reviews, stage, config, attack_features(pcm, identity), raw_comparison)
    charts, reports = create_outputs(candidates, excluded, review_inputs, prior, manifest, stage, config, reviews, run_id, raw_comparison)
    destination = Path(output or root / 'PlaytestExports/Runner/staging' / run_id[:16]).resolve()
    live_dir = (root / DATA / 'RunnerCharts').resolve()
    require(not destination.is_relative_to(live_dir), 'Staging must not write inside live charts')
    require(not destination.exists(), 'Staging destination exists; choose a new --output to preserve review work')
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix='.runner-stage-', dir=destination.parent))
    try:
        for preset, chart in charts.items():
            (temporary / (preset + '.json')).write_bytes(encoded(chart))
            (temporary / ('review-input-' + preset + '.json')).write_bytes(encoded(review_inputs[preset]))
            csv_text = io.StringIO(newline='')
            fields = ['id', 'time', 'window', 'originalTime', 'parentTime', 'anchorId', 'phraseId', 'source', 'teamReview']
            writer = csv.DictWriter(csv_text, fieldnames=fields, extrasaction='ignore')
            writer.writeheader()
            writer.writerows(chart['notes'])
            (temporary / (preset + '-notes.csv')).write_text(csv_text.getvalue())
        for name, data in dict(reports, rules=config, stage=stage).items():
            (temporary / (name + '.json')).write_bytes(encoded(data))
        staged_manifest = dict(format='afterecho-runner-stage-v2', runId=run_id, inputs=inputs,
            applicationAllowed=not raw_comparison, observedLiveHashes=live_hashes,
            files={p.name: sha(p.read_bytes()) for p in sorted(temporary.iterdir())})
        (temporary / 'manifest.json').write_bytes(encoded(staged_manifest))
        validate_outputs({p: read(temporary / (p + '.json')) for p in PRESETS}, config, stage, reviews)
        os.replace(temporary, destination)
    except BaseException:
        shutil.rmtree(temporary, ignore_errors=True)
        raise
    return dict(staging=str(destination), runId=run_id, validation=reports['validation'],
                pendingReviews=len(reports['reviewQueue']), lockedNotes=len(reviews['locks']))


def replace_batch(directory, content, expected=None):
    """Prepare all files before replacement; restore all originals on a caught failure."""
    before = {name: (directory / name).read_bytes() for name in content}
    require(expected is None or {name: sha(data) for name, data in before.items()} == expected,
            'Concurrent chart edits detected immediately before replacement; nothing was replaced')
    temporary = {}
    try:
        for name, data in content.items():
            fd, path = tempfile.mkstemp(prefix='.runner-', dir=directory)
            with os.fdopen(fd, 'wb') as f:
                f.write(data)
                f.flush()
                os.fsync(f.fileno())
            temporary[name] = Path(path)
        for name, path in temporary.items():
            os.replace(path, directory / name)
        require(all((directory / n).read_bytes() == data for n, data in content.items()), 'Applied bytes differ')
    except BaseException:
        for name, data in before.items():
            (directory / name).write_bytes(data)
        raise
    finally:
        for path in temporary.values():
            path.unlink(missing_ok=True)


def apply_stage(staging, root=ROOT):
    root, staging = Path(root), Path(staging)
    manifest = read(staging / 'manifest.json')
    require(manifest.get('format') == 'afterecho-runner-stage-v2' and manifest.get('applicationAllowed') is True, 'Stage is not publishable (raw A/B is comparison only)')
    require(sha(encoded(manifest['inputs'])) == manifest['runId'], 'Run identity mismatch')
    for name, expected in manifest['files'].items():
        require(Path(name).name == name and sha((staging / name).read_bytes()) == expected, 'Staging hash mismatch: ' + name)
    required = {p + '.json' for p in PRESETS} | {'review-input-' + p + '.json' for p in PRESETS} | {'rules.json', 'stage.json'}
    require(required <= set(manifest['files']), 'Manifest omits required validated output')
    require(sha(Path(__file__).read_bytes()) == manifest['inputs']['pipelineSHA256'], 'Selector changed since staging; regenerate')
    identity, _ = pcm_identity(root / DATA / 'Audio/Untitled.wav')
    require(identity == manifest['inputs']['pcm'], 'Live PCM changed since staging')
    require(sha((root / DATA / 'stage.json').read_bytes()) == manifest['inputs']['stageSHA256'], 'Live stage changed since staging')
    config, stage = read(staging / 'rules.json'), read(staging / 'stage.json')
    require(sha(encoded(config)) == manifest['inputs']['rulesSHA256'], 'Staged rules mismatch')
    require(sha(encoded(read(HERE / 'selection_rules.json'))) == manifest['inputs']['rulesSHA256'],
            'Selection rules changed since staging; regenerate')
    directory = root / DATA / 'RunnerCharts'
    before = {p: sha((directory / (p + '.json')).read_bytes()) for p in PRESETS}
    require(before == manifest['observedLiveHashes'], 'Live chart/reviews changed; regenerate staging before apply')
    review_inputs = {p: read(staging / ('review-input-' + p + '.json')) for p in PRESETS}
    require({p: sha(encoded(review_inputs[p])) for p in PRESETS} == manifest['inputs']['reviewChartSHA256'], 'Review source identity mismatch')
    reviews = collect_reviews(review_inputs)
    charts = {p: read(staging / (p + '.json')) for p in PRESETS}
    validate_outputs(charts, config, stage, reviews)
    backup = root / 'PlaytestExports/Runner/chart-backups' / manifest['runId'][:16]
    require(not backup.exists(), 'Backup already exists; inspect previous application')
    backup.mkdir(parents=True)
    for preset in PRESETS:
        shutil.copyfile(directory / (preset + '.json'), backup / (preset + '.json'))
    require(all(sha((backup / (p + '.json')).read_bytes()) == before[p] for p in PRESETS),
            'Live chart changed while backing up; apply stopped')
    after = {p: sha((staging / (p + '.json')).read_bytes()) for p in PRESETS}
    (backup / 'manifest.json').write_bytes(encoded(dict(format='afterecho-runner-backup-v1', before=before, after=after, runId=manifest['runId'])))
    replace_batch(directory, {p + '.json': (staging / (p + '.json')).read_bytes() for p in PRESETS},
                  {p + '.json': before[p] for p in PRESETS})
    return dict(applied=str(staging), backup=str(backup), validation=read(staging / 'validation.json'))


def rollback(backup, root=ROOT):
    backup, root = Path(backup), Path(root)
    manifest = read(backup / 'manifest.json')
    require(manifest.get('format') == 'afterecho-runner-backup-v1', 'Unknown backup')
    directory = root / DATA / 'RunnerCharts'
    current = {p: sha((directory / (p + '.json')).read_bytes()) for p in PRESETS}
    require(current == manifest['after'] or all(current[p] in (manifest['before'][p], manifest['after'][p]) for p in PRESETS), 'Live edits exist after application; preserve them before rollback')
    require(all(sha((backup / (p + '.json')).read_bytes()) == manifest['before'][p] for p in PRESETS), 'Backup hash mismatch')
    replace_batch(directory, {p + '.json': (backup / (p + '.json')).read_bytes() for p in PRESETS},
                  {p + '.json': current[p] for p in PRESETS})
    return dict(restored=str(backup))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument('--apply', type=Path, metavar='STAGING')
    actions.add_argument('--rollback', type=Path, metavar='BACKUP')
    parser.add_argument('--output', type=Path)
    parser.add_argument('--source-dir', type=Path)
    parser.add_argument('--raw-comparison', action='store_true')
    parser.add_argument('--review-chart', action='append', default=[], metavar='PRESET=PATH',
                        help='Use an exported Lab review chart for this preset; repeat for other presets')
    args = parser.parse_args()
    try:
        require(not (args.apply or args.rollback) or not (args.output or args.source_dir or args.raw_comparison or args.review_chart), 'Selection options cannot accompany apply/rollback')
        overrides = {}
        for spec in args.review_chart:
            require('=' in spec, 'Review input must be PRESET=PATH')
            preset, path = spec.split('=', 1)
            require(preset in PRESETS and preset not in overrides and path, 'Invalid or duplicate review preset: ' + preset)
            overrides[preset] = Path(path)
        result = apply_stage(args.apply) if args.apply else rollback(args.rollback) if args.rollback else stage_selection(source_dir=args.source_dir, output=args.output, raw_comparison=args.raw_comparison, review_charts=overrides)
        print(json.dumps(result, ensure_ascii=False, indent=2))
    except (SelectionError, OSError, ValueError, KeyError) as exc:
        print('Runner selection stopped: ' + str(exc), file=sys.stderr)
        raise SystemExit(1)


if __name__ == '__main__':
    main()
