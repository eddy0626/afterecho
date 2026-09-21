from pathlib import Path
import json,subprocess,collections
root=Path(__file__).resolve().parents[2]
p=subprocess.run([str(root/'Tools/unity-local'),'command','build_status','--format','json'],capture_output=True,text=True,check=True)
envelope=json.loads(p.stdout)
if not envelope.get('success'):raise RuntimeError(envelope.get('errors'))
d=envelope['data']['result']
if isinstance(d,str):d=json.loads(d)
keys=['status','buildId','result','platform','outputPath','totalSizeBytes','buildTimeMs','totalWarnings','totalErrors'];s={k:d[k] for k in keys if k in d}
if d.get('status')=='completed':
 (root/'PlaytestExports/Runner/webgl-build.json').write_text(json.dumps(s,indent=2))
 warnings=d.get('warnings',[])
 if warnings:(root/'PlaytestExports/Runner/build-warnings.json').write_text(json.dumps(warnings,ensure_ascii=False,indent=2))
print(json.dumps(s,ensure_ascii=False))
