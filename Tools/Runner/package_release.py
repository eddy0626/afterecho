#!/usr/bin/env python3
"""Create local review packages; never deploys or alters the public build."""
from pathlib import Path
import shutil,zipfile,json,hashlib,os
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Deliverables';OUT.mkdir(exist_ok=True)
NAME='AFTERECHO_RunnerCircle_Team_20260921';team=OUT/NAME;team.mkdir(exist_ok=True)
shutil.copytree(ROOT/'Builds/RunnerCircleWeb',team/'WebGL',dirs_exist_ok=True)
shutil.copytree(ROOT/'PlaytestExports/Runner',team/'Review',dirs_exist_ok=True)
shutil.copytree(ROOT/'Assets/Afterecho/Resources/Afterecho/RunnerCharts',team/'Charts',dirs_exist_ok=True)
shutil.copy2(ROOT/'PlaytestExports/Runner/README.md',team/'README.md')
server=(ROOT/'Tools/Runner/serve.py').read_text().replace("Path(__file__).resolve().parents[2]/'Builds/RunnerCircleWeb'","Path(__file__).resolve().parent/'WebGL'")
server=server.replace("print('AFTERECHO runner test:","import threading,webbrowser\nthreading.Timer(1,lambda:webbrowser.open('http://127.0.0.1:8777')).start()\nprint('AFTERECHO runner test:")
(team/'serve.py').write_text(server)
(team/'실행.command').write_text('#!/bin/zsh\ncd "$(dirname "$0")"\npython3 serve.py\n')
os.chmod(team/'실행.command',0o755)
(team/'실행.bat').write_text('@echo off\r\ncd /d "%~dp0"\r\nwhere py >nul 2>nul\r\nif %errorlevel%==0 (py -3 serve.py) else (python serve.py)\r\npause\r\n')
def archive(path,files):
 with zipfile.ZipFile(path,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
  for p,arc in files:
   if p.name!='.DS_Store' and '__pycache__' not in p.parts and not p.name.endswith(('.log','.pyc')):z.write(p,arc)
 with zipfile.ZipFile(path) as z:
  bad=z.testzip()
  if bad:raise RuntimeError(bad)
 return {'file':path.name,'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()}
reports=[]
reports.append(archive(OUT/(NAME+'.zip'),[(p,p.relative_to(OUT)) for p in team.rglob('*') if p.is_file()]))
source='AFTERECHO_RunnerCircle_Unity_20260921';files=[]
for directory in ['Assets','Packages','ProjectSettings','Tools/Runner','ArtProduction/Runner','PlaytestExports/Runner']:
 for p in (ROOT/directory).rglob('*'):
  if p.is_file() and not p.name.startswith('PerformanceTestRun') and not any(x in p.parts for x in ['_Recovery','StreamingAssets']):files.append((p,Path(source)/p.relative_to(ROOT)))
files.append((ROOT/'PlaytestExports/Runner/README.md',Path(source)/'README_RUNNER.md'))
reports.append(archive(OUT/(source+'.zip'),files))
(OUT/'release-manifest.json').write_text(json.dumps(reports,indent=2))
print(json.dumps(reports,indent=2))
