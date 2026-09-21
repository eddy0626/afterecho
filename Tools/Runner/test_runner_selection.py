import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import wave

import runner_selection as s


class SelectionTests(unittest.TestCase):
    def setUp(self):
        self.config = s.read(s.HERE / 'selection_rules.json')
        self.stage = {'duration': 30., 'beat': .75, 'beats': [.11+i*.75 for i in range(41)], 'sections': [{'fromBeat': 0, 'code': 'TEST'}]}

    def candidate(self, nid, at, support=1):
        return dict(id=nid, time=at, phrase=int(at / 6), support=support, strengthRank=0)

    def charts(self, notes, reviews=()):
        return {p: {'notes': copy.deepcopy(notes), 'humanReviews': copy.deepcopy(list(reviews))} for p in s.PRESETS}

    def review(self, at=2., decision='approved', rid='R1'):
        return dict(id=rid, noteId='A', decision=decision, reviewer='Tester', reason='auditioned',
                    reviewedTime=at, reviewedUtc='2026-09-21T00:00:00Z', humanConfirmed=True, sourceTime=None)

    def test_closed_visibility_counts_late_pending_note(self):
        times = [10.805442, 11.004989, 11.284354, 11.573696, 11.85, 12.04]
        self.assertEqual(6, s.visible_peak(times, [.06] * 6, 1.2))
        self.assertFalse(s.valid_times(times, self.config['difficulties']['hard'], 1.2))
        self.assertEqual(2, s.visible_peak([1.2, 2.5], [.1, .1], 1.2))

    def test_beat_reference_uses_measured_offset_and_variable_intervals(self):
        stage = dict(self.stage, beats=[.110329, .888130, 1.661400])
        grid = s.halfbeat_grid(stage)
        self.assertEqual(0, s.beat_index(.88, stage))
        self.assertEqual(1, s.beat_index(.88813, stage))
        self.assertAlmostEqual(1., s.beat_proximity(.110329, grid))
        self.assertAlmostEqual(1., s.beat_proximity((.110329+.888130)/2, grid))

    def test_minimum_gap_and_window_boundaries(self):
        rules = self.config['difficulties']['easy']
        self.assertTrue(s.valid_times([1.2, 1.5], rules, 1.2))
        self.assertFalse(s.valid_times([1.2, 1.499], rules, 1.2))
        self.assertAlmostEqual(.08, s.note_windows([1., 1.2], .1)[0])

    def test_four_fast_hits_require_recovery(self):
        rules = self.config['difficulties']['hard']
        self.assertTrue(s.valid_times([1.2, 1.4, 1.6, 1.8, 2.2], rules, 1.2))
        self.assertFalse(s.valid_times([1.2, 1.4, 1.6, 1.8, 2.19], rules, 1.2))

    def test_phrase_alternatives_keep_legal_outer_pair(self):
        candidates = [self.candidate('L', 1.2), self.candidate('M', 1.35), self.candidate('R', 1.51)]
        rules = dict(self.config['difficulties']['normal'])
        selected = s.select_notes(candidates, rules, self.config, self.stage)
        self.assertEqual(['L', 'R'], [n['id'] for n in selected])

    def test_mandatory_anchor_survives_stronger_conflict(self):
        candidates = [self.candidate('L', 1.2, 3), self.candidate('M', 1.35), self.candidate('R', 1.51, 3)]
        selected = s.select_notes(candidates, self.config['difficulties']['normal'], self.config, self.stage, ['M'])
        self.assertEqual(['M'], [n['id'] for n in selected])

    def test_incompatible_locks_fail_instead_of_moving_them(self):
        candidates = [self.candidate('A', 1.2), self.candidate('B', 1.3)]
        with self.assertRaises(s.SelectionError):
            s.select_notes(candidates, self.config['difficulties']['normal'], self.config, self.stage, ['A', 'B'])

    def test_time_matching_current_human_review_is_locked(self):
        note = dict(id='A', time=2., teamReview='approved', humanReviewId='R1')
        result = s.collect_reviews(self.charts([note], [self.review()]))
        self.assertEqual({'A'}, set(result['locks']))

    def test_stale_or_superseded_approval_is_not_locked(self):
        note = dict(id='A', time=2.1, teamReview='approved', humanReviewId='R1')
        self.assertFalse(s.collect_reviews(self.charts([note], [self.review()]))['locks'])
        note['time'] = 2.
        events = [self.review(), self.review(decision='pending', rid='R2')]
        self.assertFalse(s.collect_reviews(self.charts([note], events))['locks'])

    def test_approval_requires_human_confirmation_timestamp_and_source_evidence(self):
        note = dict(id='A', time=2., originalTime=1.95, teamReview='approved', humanReviewId='R1')
        for field, value in [('humanConfirmed', False), ('reviewedUtc', 'invalid'), ('sourceTime', 1.94)]:
            event = dict(self.review(), sourceTime=1.95)
            event[field] = value
            self.assertFalse(s.collect_reviews(self.charts([note], [event]))['locks'])

    def test_merged_history_does_not_revive_old_approval(self):
        note = dict(id='A', time=2., teamReview='approved', humanReviewId='R1')
        charts = self.charts([note], [self.review()])
        invalidation = self.review(decision='pending', rid='R2')
        invalidation['reviewedUtc'] = '2026-09-21T00:01:00Z'
        charts['easy']['humanReviews'].append(invalidation)
        charts['easy']['notes'][0].update(teamReview='pending', humanReviewId='R2')
        result = s.collect_reviews(charts)
        self.assertFalse(result['locks'])
        self.assertEqual(['R1', 'R2'], [r['id'] for r in result['history']])

    def test_conflicting_append_history_fails_closed(self):
        charts = self.charts([dict(id='A', time=2.)], [self.review(), self.review(rid='R2')])
        charts['hard']['humanReviews'].reverse()
        with self.assertRaises(s.SelectionError):
            s.collect_reviews(charts)

    def test_conflicting_live_shared_note_times_are_rejected(self):
        charts = self.charts([dict(id='A', time=2.)])
        charts['normal']['notes'][0]['time'] = 2.1
        with self.assertRaises(s.SelectionError):
            s.collect_reviews(charts)

    def test_current_rejection_prevents_reintroduction(self):
        note = dict(id='A', time=2., teamReview='rejected', humanReviewId='R1')
        result = s.collect_reviews(self.charts([note], [self.review(decision='rejected')]))
        self.assertEqual({'A'}, result['rejected'])

    def test_raw_sources_do_not_inherit_legacy_start_or_door_deletions(self):
        raw = {p: {'notes': [{'id': p+'-early', 'time': 1.3}, {'id': p+'-end', 'time': 29.8}, {'id': p+'-pre', 'time': .8}]} for p in s.PRESETS}
        prior = {'records': {n['id']: {'rawTime': n['time'], 'priorTime': n['time'], 'legacyDeletionReasons': ['listening / ready / ending separation']} for r in raw.values() for n in r['notes']}}
        reviews = dict(times={}, locks={}, rejected=set())
        pool, excluded = s.make_candidates(raw, prior, reviews, self.stage, self.config, lambda _: (0., 0))
        self.assertEqual([1.3, 29.8], [n['time'] for n in pool])
        self.assertEqual(3, len(excluded))
        self.assertEqual(3, len(pool[0]['aliases']))

    def test_human_rejection_cannot_be_bypassed_through_an_alias(self):
        raw = {p: {'notes': [{'id': p, 'time': 2.}]} for p in s.PRESETS}
        prior = {'records': {p: {'rawTime': 2., 'priorTime': 2.04} for p in s.PRESETS}}
        reviews = dict(times={}, locks={}, rejected={'normal'})
        pool, excluded = s.make_candidates(raw, prior, reviews, self.stage, self.config, lambda _: (0., 0))
        self.assertEqual([], pool)
        self.assertEqual(3, len(excluded))
        reviews['locks']['easy'] = {'note': {'time': 2.04}, 'presets': ['easy']}
        with self.assertRaises(s.SelectionError):
            s.make_candidates(raw, prior, reviews, self.stage, self.config, lambda _: (0., 0))

    def test_prior_time_retained_and_near_anchor_never_averaged(self):
        raw = {p: {'notes': [{'id': p, 'time': 2. + i*.01}]} for i, p in enumerate(s.PRESETS)}
        prior = {'records': {p: {'rawTime': raw[p]['notes'][0]['time'], 'priorTime': 2.07 + i*.01} for i,p in enumerate(s.PRESETS)}}
        reviews = dict(times={'easy': 2.09}, locks={}, rejected=set())
        pool, _ = s.make_candidates(raw, prior, reviews, self.stage, self.config, lambda _: (0., 0))
        self.assertIn(2.09, [n['time'] for n in pool])
        self.assertEqual(1, len({n['anchorId'] for n in pool}))
        self.assertTrue(all(any(abs(n['time']-t) < s.EPS for t in [2.08, 2.09]) for n in pool))

    def test_invalid_configuration_rejected(self):
        for field, value in [('minGap', 0), ('maxWindow', float('nan')), ('maxVisibleNotes', 2.5), ('maxVisibleNotes', 13)]:
            config = copy.deepcopy(self.config)
            config['difficulties']['easy'][field] = value
            with self.subTest(field=field), self.assertRaises(s.SelectionError):
                s.validate_config(config)
        config = copy.deepcopy(self.config)
        config.update(previewSeconds=.6, firstNoteSeconds=.6)
        with self.assertRaises(s.SelectionError):
            s.validate_config(config)

    def test_batch_replacement_rolls_back_after_second_file_failure(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp)
            before = {'easy.json': b'old easy', 'normal.json': b'old normal', 'hard.json': b'old hard'}
            for name, data in before.items():
                (path / name).write_bytes(data)
            original_replace = s.os.replace
            calls = 0
            def fail_second(source, destination):
                nonlocal calls
                calls += 1
                if calls == 2:
                    raise OSError('injected disk error')
                return original_replace(source, destination)
            with mock.patch.object(s.os, 'replace', side_effect=fail_second), self.assertRaises(OSError):
                s.replace_batch(path, {name: b'new' for name in before})
            self.assertEqual(before, {name: (path / name).read_bytes() for name in before})


class TransactionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.data = self.root / s.DATA
        (self.data / 'RunnerCharts').mkdir(parents=True)
        (self.data / 'Audio').mkdir()
        self.config = s.read(s.HERE / 'selection_rules.json')
        self.stage = {'duration': 30., 'beat': .75, 'beats': [.11+i*.75 for i in range(41)], 'sections': [{'fromBeat': 0, 'code': 'TEST'}]}
        (self.data / 'stage.json').write_bytes(s.encoded(self.stage))
        with wave.open(str(self.data / 'Audio/Untitled.wav'), 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(1000); w.writeframes(b'\0\0' * 30000)
        self.charts = {}
        for preset in s.PRESETS:
            notes = [dict(id='N'+str(i), time=1.2+i, window=self.config['difficulties'][preset]['maxWindow'], teamReview='pending') for i in range(24)]
            chart = dict(format='afterecho-chart-v1', gameplayMode='runner', notes=notes, rules=self.config['difficulties'][preset], review={'trainingEligible': False})
            self.charts[preset] = chart
            (self.data / 'RunnerCharts' / (preset+'.json')).write_bytes(s.encoded(chart))
        self.staged = self.root / 'staged'
        self.staged.mkdir()
        self.before = {p: s.sha((self.data / 'RunnerCharts' / (p+'.json')).read_bytes()) for p in s.PRESETS}
        for preset, chart in self.charts.items():
            changed = copy.deepcopy(chart); changed['selectionRunId'] = 'new'
            (self.staged / (preset+'.json')).write_bytes(s.encoded(changed))
            (self.staged / ('review-input-'+preset+'.json')).write_bytes(s.encoded(chart))
        for name, data in [('stage', self.stage), ('rules', self.config), ('validation', [])]:
            (self.staged / (name+'.json')).write_bytes(s.encoded(data))
        inputs = dict(pipelineSHA256=s.sha(Path(s.__file__).read_bytes()), rulesSHA256=s.sha(s.encoded(self.config)),
                      stageSHA256=s.sha((self.data / 'stage.json').read_bytes()), pcm=s.pcm_identity(self.data / 'Audio/Untitled.wav')[0],
                      reviewChartSHA256={p:s.sha(s.encoded(c)) for p,c in self.charts.items()})
        self.manifest = dict(format='afterecho-runner-stage-v2', applicationAllowed=True, runId=s.sha(s.encoded(inputs)),
                             inputs=inputs, observedLiveHashes=self.before,
                             files={p.name:s.sha(p.read_bytes()) for p in self.staged.iterdir()})
        self.write_manifest()

    def tearDown(self):
        self.temp.cleanup()

    def write_manifest(self):
        (self.staged / 'manifest.json').write_bytes(s.encoded(self.manifest))

    def test_apply_and_rollback_preserve_exact_originals(self):
        result = s.apply_stage(self.staged, self.root)
        self.assertEqual('new', s.read(self.data / 'RunnerCharts/easy.json')['selectionRunId'])
        s.rollback(result['backup'], self.root)
        self.assertEqual(self.before, {p: s.sha((self.data / 'RunnerCharts' / (p+'.json')).read_bytes()) for p in s.PRESETS})

    def test_raw_comparison_is_not_publishable(self):
        self.manifest['applicationAllowed'] = False
        self.write_manifest()
        with self.assertRaises(s.SelectionError):
            s.apply_stage(self.staged, self.root)

    def test_live_edits_after_staging_block_apply(self):
        path = self.data / 'RunnerCharts/easy.json'
        path.write_text(path.read_text() + ' ')
        with self.assertRaises(s.SelectionError):
            s.apply_stage(self.staged, self.root)

    def test_changed_staged_chart_blocks_apply(self):
        path = self.staged / 'normal.json'
        path.write_text(path.read_text() + ' ')
        with self.assertRaises(s.SelectionError):
            s.apply_stage(self.staged, self.root)

    def test_changed_pcm_blocks_apply(self):
        with wave.open(str(self.data / 'Audio/Untitled.wav'), 'wb') as w:
            w.setnchannels(1); w.setsampwidth(2); w.setframerate(1000); w.writeframes(b'\1\0' * 30000)
        with self.assertRaises(s.SelectionError):
            s.apply_stage(self.staged, self.root)

    def test_concurrent_edit_during_backup_is_preserved_and_stops_apply(self):
        original = s.shutil.copyfile
        edited = b'{"concurrentReview":"preserve this"}'
        def inject(source, destination):
            result = original(source, destination)
            if Path(source).name == 'hard.json':
                (self.data / 'RunnerCharts/easy.json').write_bytes(edited)
            return result
        with mock.patch.object(s.shutil, 'copyfile', side_effect=inject), self.assertRaises(s.SelectionError):
            s.apply_stage(self.staged, self.root)
        self.assertEqual(edited, (self.data / 'RunnerCharts/easy.json').read_bytes())

    def test_current_rule_change_after_staging_blocks_apply(self):
        original = s.read
        def altered(path):
            result = original(path)
            if Path(path) == s.HERE / 'selection_rules.json':
                result['difficulties']['easy']['targetNps'] = 1.7
            return result
        with mock.patch.object(s, 'read', side_effect=altered), self.assertRaises(s.SelectionError):
            s.apply_stage(self.staged, self.root)

    def test_full_staging_determinism_review_import_and_approval_scope(self):
        source = self.root / 'sources'
        (source / 'raw').mkdir(parents=True)
        song = dict(id='fixture-song', duration=30.)
        raw_info, records = {}, {}
        for preset in s.PRESETS:
            raw = {'notes': [{'id': preset+'-'+str(i), 'time': 1.2+i} for i in range(24)]}
            raw_path = source / 'raw' / (preset+'.json')
            raw_path.write_bytes(s.encoded(raw))
            raw_info[preset] = dict(path='raw/'+preset+'.json', sha256=s.sha(raw_path.read_bytes()), notes=24, origin={'generator':'fixture'})
            for note in raw['notes']:
                records[note['id']] = dict(rawTime=note['time'], priorTime=note['time'])
            chart = self.charts[preset]
            chart.update(preset=preset, song=song, origin={}, edits=[], runnerEdits=[])
            (self.data / 'RunnerCharts' / (preset+'.json')).write_bytes(s.encoded(chart))
        prior = dict(records=records, edits={p:[] for p in s.PRESETS}, runnerEdits={p:[] for p in s.PRESETS})
        (source / 'prior_timing.json').write_bytes(s.encoded(prior))
        (source / 'stage.json').write_bytes(s.encoded(self.stage))
        source_manifest = dict(format='afterecho-runner-source-bundle-v1', rawFiles=raw_info, song=song,
            priorTimingSHA256=s.sha((source / 'prior_timing.json').read_bytes()), stageSHA256=s.sha((source / 'stage.json').read_bytes()),
            pcm=s.pcm_identity(self.data / 'Audio/Untitled.wav')[0])
        (source / 'manifest.json').write_bytes(s.encoded(source_manifest))
        edited = copy.deepcopy(self.charts['easy'])
        note = edited['notes'][5]
        note.update(source='manual_edit', originalTime=note['time'], teamReview='approved', humanReviewId='review1')
        edited['humanReviews'] = [dict(id='review1', noteId=note['id'], decision='approved', reviewer='Team', reason='auditioned',
            reviewedTime=note['time'], sourceTime=None, reviewedUtc='2026-09-21T12:00:00Z', humanConfirmed=True)]
        edited['edits'] = [{'action':'manual-review-history', 'noteId':note['id']}]
        review_path = self.root / 'easy-edited.json'
        review_path.write_bytes(s.encoded(edited))
        before = {p:s.sha((self.data / 'RunnerCharts' / (p+'.json')).read_bytes()) for p in s.PRESETS}
        a = s.stage_selection(self.root, source, self.root / 'first', review_charts={'easy':review_path})
        b = s.stage_selection(self.root, source, self.root / 'second', review_charts={'easy':review_path})
        self.assertEqual(a['runId'], b['runId'])
        self.assertEqual({p.name:p.read_bytes() for p in Path(a['staging']).iterdir()},
                         {p.name:p.read_bytes() for p in Path(b['staging']).iterdir()})
        self.assertEqual(before, {p:s.sha((self.data / 'RunnerCharts' / (p+'.json')).read_bytes()) for p in s.PRESETS})
        for preset in s.PRESETS:
            output = s.read(Path(a['staging']) / (preset+'.json'))
            retained = next(n for n in output['notes'] if n['id'] == note['id'])
            self.assertEqual(note['time'], retained['time'])
            self.assertEqual('approved' if preset == 'easy' else 'pending', retained['teamReview'])
        self.assertEqual(edited['edits'], s.read(Path(a['staging']) / 'easy.json')['edits'])
        # Applying uses the immutable exported review snapshot, but still checks original live hashes.
        result = s.apply_stage(Path(a['staging']), self.root)
        self.assertTrue(Path(result['backup']).exists())

    def test_rollback_does_not_erase_edits_after_apply(self):
        result = s.apply_stage(self.staged, self.root)
        path = self.data / 'RunnerCharts/hard.json'
        path.write_text(path.read_text() + ' ')
        with self.assertRaises(s.SelectionError):
            s.rollback(result['backup'], self.root)


if __name__ == '__main__':
    unittest.main()
