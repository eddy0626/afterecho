from pathlib import Path
import subprocess,json,shutil
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Assets/Afterecho/Runner/Audio';RAW=ROOT/'ArtProduction/Runner/AudioSources';RAW.mkdir(parents=True,exist_ok=True);OUT.mkdir(parents=True,exist_ok=True)
report=ROOT/'PlaytestExports/Runner/audio-provenance.json'
previous={r['clip']:r for r in json.loads(report.read_text())} if report.exists() else {}
rows=[]
for name,prefix,duration,gain in [('Good','GOO',.18,1.0),('Perfect','PER',.22,1.0),('Fall','FAL',.35,.85),('Boost','BOO',.55,.75),('Clear','CLE',1.0,.8)]:
    source=RAW/(name+'-ElevenLabs.wav')
    if not source.exists():
        download=sorted((Path.home()/'Downloads').glob(f'AFTERECHO_RUNNER_{prefix}_#1-*.wav'))[0]
        shutil.copy2(download,source)
    filters=f'silenceremove=start_periods=1:start_duration=0.001:start_threshold=-48dB,atrim=duration={duration},asetpts=PTS-STARTPTS,afade=t=out:st={max(0,duration-.035)}:d=0.035,volume={gain},alimiter=limit=0.85:level=false'
    subprocess.run(['ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(source),'-af',filters,'-ac','1','-ar','48000','-c:a','pcm_s16le',str(OUT/(name+'.wav'))],check=True)
    rows.append(dict(clip=name,source=previous.get(name,{}).get('source',source.name),service='ElevenLabs sound effects',variant=1,maximumDuration=duration,processing=filters))
(ROOT/'PlaytestExports/Runner/audio-provenance.json').write_text(json.dumps(rows,indent=2)+'\n')
print('5 generated SFX imported; original downloads preserved')
