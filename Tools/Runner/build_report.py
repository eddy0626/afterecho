from pathlib import Path
import json,subprocess,collections
root=Path(__file__).resolve().parents[2]
p=subprocess.run([str(Path.home()/'.unity/bin/unity'),'command','build_status','--project-path',str(root),'--caller','plugin','--skill','unity-cli'],capture_output=True,text=True,check=True)
line=[x for x in p.stdout.splitlines() if x.startswith('build_status\t')][-1]
d=json.loads(line.split('\t')[2]);keys=['status','buildId','result','platform','outputPath','totalSizeBytes','buildTimeMs','totalWarnings','totalErrors'];s={k:d[k] for k in keys if k in d}
if d.get('status')=='completed':
 (root/'PlaytestExports/Runner/webgl-build.json').write_text(json.dumps(s,indent=2))
 warnings=d.get('warnings',[])
 if warnings:(root/'PlaytestExports/Runner/build-warnings.json').write_text(json.dumps(warnings,ensure_ascii=False,indent=2))
print(json.dumps(s,ensure_ascii=False))
