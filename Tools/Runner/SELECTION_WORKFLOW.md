# AFTERECHO runner candidate selection

This is a game-specific selector and review queue around existing BeatLearning
candidates. It is **not a newly trained model**, and an automated pass is **not a
musical or one-hand difficulty approval**. New model inference is separate; see
`beatlearning/README.md`. The old HTML generator is retained as historical evidence.

## Reproduce and stage

From the Unity project root, using Python with NumPy:

```sh
python3 -m unittest discover -s Tools/Runner -p test_runner_selection.py -v
python3 Tools/Runner/prepare_charts.py
```

The default invocation writes only to
`PlaytestExports/Runner/staging/<execution-hash>/`; it never changes live Assets.
An existing destination is protected rather than overwritten. Use
`--output /absolute/path/to/new-directory` for another deterministic copy.
`--raw-comparison` stages raw-time A/B material, marked unpublishable. It cannot
override existing human-approved locks. The Lab's A/B playback is preferable
for auditioning an already approved chart.

To continue from the Lab's exported reviews without overwriting Assets:

```sh
python3 Tools/Runner/prepare_charts.py --review-chart normal=/absolute/path/normal-edited-review.json
```

Repeat `--review-chart PRESET=PATH` for other difficulties. Unspecified presets
use their live charts. Exact review input snapshots and hashes accompany staging;
the original live hashes remain separate for apply's concurrent-edit guard.
Conflicting times for the same shared ID stop selection, so divergent shared-
anchor edits must be reconciled explicitly rather than silently overwritten.

Each stage contains three chart JSON files and CSVs, `validation.json`, a full
`reviewQueue.json`, `candidateDecisions.json`, `excludedCandidates.json`, review
warnings, rules/stage snapshots, and a manifest covering every output hash.
`runId` covers the pipeline, rule configuration, source manifest, live chart
inputs, exact decoded PCM identity, NumPy version and comparison policy. No wall
clock timestamp or random seed is used by selection. Identical inputs produce
identical staged file bytes.

## What the selector does

- Reads the **original 323 / 323 / 316 model candidates**, rather than inheriting
  the old corridor's listening / ready / door deletion rules. The first note
  allows a full 1.2-second preview; candidates can continue to song duration−.12.
- Keeps each existing ID's current live `time`. A previously removed candidate
  uses its recorded prior curated time, then raw time if no correction exists.
  `originalTime`/`rawTime`, `parentTime`/`priorTime`, original `edits`,
  `runnerEdits`, and source candidate IDs are preserved. A measured attack is
  only a selection score; it never silently moves a timestamp.
- Makes one union candidate pool. Exact-time alternatives share one canonical
  ID and retain every alias in `sourceCandidates`. Nearby raw events get a
  common `anchorId`, without averaging their timestamps. Consequently a normal
  or hard note may intentionally have an `AI-easy-…` ID. Its `sourcePreset` and
  per-source generation metadata identify the actual provenance.
- Uses a deterministic bounded beam across chronological candidate combinations,
  scoring source agreement, measured attack, proximity to a half beat, repeated
  intervals, and a soft per-eight-beat-phrase density target. This is a bounded
  heuristic, not a globally optimal solver or a guarantee of musical quality.
  It can choose two legal outer candidates over a conflicting middle candidate.
- Builds easy ⊂ normal ⊂ hard with exact ID **and** timing inheritance, subject
  to approved locks. Hard constraints are minimum spacing, four-hit burst
  recovery, and actual closed visible intervals `[time−preview, time+window]`.
  The latter includes late, still-pending notes. Configured limits are 4/5/5.
- Treats raw/prior timing disagreement as pending A/B review. Shifts larger than
  the difficulty's window are prioritized, not labelled automatically wrong.

`selection_rules.json` is the single selection configuration. Window ≤40% of
minimum gap is required, so legal selected notes use the configured maximum
window. All outputs are reloaded and independently validated before the staging
directory is published. Actual Unity playback uses the same stricter pending-
ring density validation with the active RunnerRules preview.

## Human reviews and locks

Only `teamReview=approved`, a referenced **latest** `humanReviewId` whose event
is approved, and a matching `reviewedTime` constitute a lock. Reviewer and reason
must be present, `humanConfirmed` must be true, and the recorded source time and
parseable review timestamp must match the Lab's evidence requirements. Merged
histories preserve each source append order, including later invalidations.
Stale approvals become warnings; conflicting current decisions
or impossible locks stop selection instead of changing or dropping a lock.
Current human rejection excludes the shared event and its source aliases, so
choosing a different canonical ID cannot reintroduce it. Approved manual notes
can be carried as `source=manual_edit`. No pass sets `trainingEligible=true`.
The Lab appends pending review events after edits; old approval remains history.

## Apply and rollback (explicit operations)

After inspecting the staged results:

```sh
python3 Tools/Runner/prepare_charts.py --apply /absolute/path/to/staging
python3 Tools/Runner/prepare_charts.py --rollback /absolute/path/to/chart-backup
```

Apply rejects modified staged files, changed pipeline/rules/PCM/stage, and live
chart edits made since staging. It validates all three charts again, writes an
exact backup and recovery manifest first, prepares all three temporary files,
and then replaces the live JSONs without touching `.meta` files. Any caught
write failure restores originals. Filesystems do not provide a single atomic
three-file replace: after a process/power interruption, rollback also recognizes
a mixture of the exact before/after hashes. It refuses to erase unrelated later
edits. Close competing chart-editing operations during apply or rollback.

## Source bundle and limits

`sources/raw/*.json` are byte-for-byte copies of the original HTML prototype's
BeatLearning output. `sources/prior_timing.json` records the old correction and
deletion history and the previous runner revision; `sources/stage.json` fixes
the stage timing. `sources/manifest.json` hashes all of them. The original
encoded-song identity remains separate from the exact baseline shipped WAV PCM
identity; this bundle does not claim an independent decode comparison against
the original M4A. Every selection verifies the bound PCM bytes and format.

The upstream difficulty values .15/.45/.75 and their actual source metadata are
preserved. At the recorded BeatLearning revision these mean EASY/NORMAL/INSANE,
not three consecutive model buckets. The new runner difficulty is determined by
selection constraints, and no decoder change or fresh inference is implied.

Team review must still audition timing A/B, phrase boundaries and apparent rests,
then test one-handed play on real phones. The attack proxy, automatic completion
and ring limits cannot establish those conclusions.
