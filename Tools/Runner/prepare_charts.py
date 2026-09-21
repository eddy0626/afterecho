#!/usr/bin/env python3
"""Derive runner charts from committed BeatLearning candidates; no model retraining.
Uses local transient strength only to choose between density conflicts. Listening
review remains pending; original IDs, timings, edits and model provenance survive.
"""
import json, csv, copy, hashlib, wave
from datetime import datetime,timezone
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
SRC=ROOT/'Assets/Afterecho/Resources/Afterecho'
OUT=SRC/'RunnerCharts'
REPORT=ROOT/'PlaytestExports/Runner'
OUT.mkdir(exist_ok=True);REPORT.mkdir(exist_ok=True)
with wave.open(str(SRC/'Audio/Untitled.wav')) as w:
    rate=w.getframerate();channels=w.getnchannels();width=w.getsampwidth()
    assert width==2
    x=np.frombuffer(w.readframes(w.getnframes()),dtype='<i2').reshape(-1,channels).astype(float).mean(axis=1)/32768
hop=max(1,int(rate*.005));x=x[:len(x)//hop*hop].reshape(-1,hop)
rms=np.sqrt((x*x).mean(axis=1));flux=np.maximum(0,np.diff(rms,prepend=0))
def strength(t):
    i=round(t*rate/hop);a=max(0,i-5);b=min(len(flux),i+6)
    return float(flux[a:b].max()) if b>a else 0
summaries=[]
for preset,gap,win,fast,recovery,cap in [('easy',.30,.10,.39,.55,4),('normal',.22,.08,.33,.44,5),('hard',.16,.06,.27,.4,5)]:
    path=SRC/'Charts'/f'{preset}.json';original=path.read_bytes();c=json.loads(original)
    c['gameplayMode']='runner';c['runnerRevision']='runner-circle-v1';c['parentChartSHA256']=hashlib.sha256(original).hexdigest()
    c['rules'].update(minGap=gap,maxWindow=win,health=100,fastGap=fast,recoveryGap=recovery,burstLimit=4)
    c['modifiedUtc']=datetime.now(timezone.utc).isoformat();c['runnerEdits']=[];notes=copy.deepcopy(c['notes']);evidence=[]
    for n in notes:
        n['parentTime']=n['time'];n['role']='run';n.pop('encounter',None)
        n['reviewStatus']='automated_checked';n['teamReview']='pending'
        evidence.append({'id':n['id'],'time':n['time'],'transientStrength':round(strength(n['time']),6)})
    def delete(i,reason):
        n=notes.pop(i);c['runnerEdits'].append(dict(noteId=n['id'],action='delete',timestampUtc=c['modifiedUtc'],originalTime=n['originalTime'],parentTime=n['time'],revisedTime=None,source=n['source'],reason=reason,reviewStatus='automated_checked',teamReview='pending'))
    i=1
    while i<len(notes):
        if notes[i]['time']-notes[i-1]['time']<gap-1e-8:
            j=i if strength(notes[i]['time'])<=strength(notes[i-1]['time']) else i-1
            delete(j,'central rings minimum spacing; weaker nearby measured transient removed');i=max(1,i-1)
        else:i+=1
    i=1;burst=1
    while i<len(notes):
        d=notes[i]['time']-notes[i-1]['time']
        if burst>=4 and d<recovery-1e-8:delete(i,'four-hit burst followed by recovery');continue
        burst=burst+1 if d<fast else 1;i+=1
    i=0
    while i<len(notes):
        end=i+cap
        if end<len(notes) and notes[end]['time']-notes[i]['time']<=1.2:
            j=min(range(i+1,end+1),key=lambda j:strength(notes[j]['time']))
            delete(j,'preview readability density cap; weaker transient removed');i=max(0,i-cap)
        else:i+=1
    for i,n in enumerate(notes):
        ds=[win]
        if i:ds.append(.4*(n['time']-notes[i-1]['time']))
        if i+1<len(notes):ds.append(.4*(notes[i+1]['time']-n['time']))
        n['window']=round(min(ds),9)
    c['notes']=notes;c['encounters']=[];c['review']={'technical':'automated_checked','musicalAndOneHand':'team_pending','trainingEligible':False,'transientComparison':'5ms RMS positive-flux measured; density pruning only, no unverified retiming'}
    (OUT/f'{preset}.json').write_text(json.dumps(c,ensure_ascii=False,indent=2)+'\n')
    (REPORT/f'{preset}-original.json').write_bytes(original)
    (REPORT/f'{preset}-transients.json').write_text(json.dumps(evidence,indent=2)+'\n')
    with (REPORT/f'{preset}-notes.csv').open('w',newline='') as f:
        wr=csv.writer(f);wr.writerow(['id','time','window','originalTime','source','section','teamReview'])
        for n in notes:wr.writerow([n.get(k,'') for k in ['id','time','window','originalTime','source','section','teamReview']])
    summary=dict(preset=preset,originalNotes=len(json.loads(original)['notes']),revisedNotes=len(notes),deleted=len(c['runnerEdits']),minGap=min(b['time']-a['time'] for a,b in zip(notes,notes[1:])),maxPreview=max(sum(0<=m['time']-n['time']<=1.2 for m in notes) for n in notes),first=notes[0]['time'],last=notes[-1]['time'])
    assert summary['minGap']>=gap-1e-8 and summary['maxPreview']<=cap
    summaries.append(summary)
(REPORT/'chart-validation.json').write_text(json.dumps(summaries,indent=2)+'\n')
print(json.dumps(summaries,indent=2))
