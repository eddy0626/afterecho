#!/usr/bin/env python3
"""Create local review packages; never deploys or alters the public build.

Importing this module only exposes package filters; it does not create a release.
"""
from pathlib import Path
import shutil
import zipfile
import json
import hashlib
import os

ROOT = Path(__file__).resolve().parents[2]
NAME = 'AFTERECHO_RunnerCircle_Team_20260921'
SOURCE_NAME = 'AFTERECHO_RunnerCircle_Unity_20260921'
TOOLING_RUNTIME_DIRECTORIES = {
    '.git', '.venv', 'venv', 'env', '.env', 'cache', 'caches', '.cache',
    '__pycache__', 'weights', 'models', 'generated', 'staging',
}
COMMON_RUNTIME_DIRECTORIES = {'.git', '.venv', 'venv', '__pycache__'}
GENERATED_REVIEW_DIRECTORIES = {'staging', 'chart-backups'}


def excluded_directory(name, tooling=False):
    lower = name.lower()
    return (lower in COMMON_RUNTIME_DIRECTORIES or
            tooling and (lower.startswith('.') or lower in TOOLING_RUNTIME_DIRECTORIES or lower.startswith('.raw-') or
                         lower.startswith('.staging-') or lower.startswith('.candidate-') or
                         lower.startswith('.runner-staging-')))


def common_file_allowed(relative, tooling=False):
    relative = Path(relative)
    return (not any(excluded_directory(part, tooling) for part in relative.parts[:-1]) and
            relative.name != '.DS_Store' and relative.suffix.lower() not in {'.log', '.pyc'})


def generator_file_allowed(relative):
    """Small reproducibility tools/data, relative to Tools/Runner.

    Do not include an inference environment, downloaded model, canonical song copy,
    or generated drafts. sources/raw is the deliberately preserved baseline data.
    """
    relative = Path(relative)
    parts = relative.parts
    if not parts or not common_file_allowed(relative, tooling=True) or any(p.startswith('.') for p in parts):
        return False
    if len(parts) == 1:
        return relative.suffix.lower() in {'.py', '.json', '.md', '.txt'}
    if parts[0] == 'sources':
        return relative.suffix.lower() in {'.json', '.csv', '.md', '.txt'} or relative.name == 'LICENSE'
    if parts[0] == 'beatlearning':
        # The adapter is flat. Any child directory belongs to downloads, an
        # inference run or a user environment, not its declared source files.
        if len(parts) != 2:
            return False
        return relative.suffix.lower() in {'.py', '.md', '.txt'} or relative.name == 'LICENSE'
    return False


def review_file_allowed(relative):
    relative = Path(relative)
    return (common_file_allowed(relative) and
            (not relative.parts or relative.parts[0] not in GENERATED_REVIEW_DIRECTORIES))


def team_file_allowed(relative):
    relative = Path(relative)
    if relative.parts and relative.parts[0] == 'Review':
        return review_file_allowed(Path(*relative.parts[1:]))
    return common_file_allowed(relative)


def source_file_allowed(relative):
    """Unity source filter; retain the existing Assets/license exclusion policy."""
    relative = Path(relative)
    if (not common_file_allowed(relative) or relative.name.startswith('PerformanceTestRun') or
            any(part in {'_Recovery', 'StreamingAssets'} for part in relative.parts)):
        return False
    if relative.parts[:2] == ('Tools', 'Runner'):
        tool = Path(*relative.parts[2:])
        return generator_file_allowed(tool) or (len(tool.parts) == 1 and tool.suffix.lower() in {'.cs', '.sh', '.command', '.bat'})
    if relative.parts[:2] == ('PlaytestExports', 'Runner'):
        return review_file_allowed(Path(*relative.parts[2:]))
    return True


def iter_files(base, predicate=common_file_allowed, skip_directories=(), tooling=False, skip_relative_directories=()):
    """Prune caches before walking them, and never follow files outside a tree."""
    base = Path(base)
    for folder, directories, filenames in os.walk(base, followlinks=False):
        directories[:] = [name for name in directories
                          if not excluded_directory(name, tooling) and name not in skip_directories and
                          (Path(folder) / name).relative_to(base).as_posix() not in skip_relative_directories and
                          not (Path(folder) / name).is_symlink()]
        for name in filenames:
            path = Path(folder) / name
            if path.is_file() and not path.is_symlink() and predicate(path.relative_to(base)):
                yield path


def archive(path, files):
    with zipfile.ZipFile(path, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as output:
        for original, relative in files:
            if common_file_allowed(relative):
                output.write(original, relative)
    with zipfile.ZipFile(path) as output:
        bad = output.testzip()
        if bad:
            raise RuntimeError(bad)
    return {'file': path.name, 'bytes': path.stat().st_size,
            'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


def main():
    out = ROOT / 'Deliverables'; out.mkdir(exist_ok=True)
    team = out / NAME; team.mkdir(exist_ok=True)
    shutil.copytree(ROOT / 'Builds/RunnerCircleWeb', team / 'WebGL', dirs_exist_ok=True)
    review = ROOT / 'PlaytestExports/Runner'
    for original in iter_files(review, review_file_allowed, skip_relative_directories=GENERATED_REVIEW_DIRECTORIES):
        destination = team / 'Review' / original.relative_to(review)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(original, destination)
    shutil.copytree(ROOT / 'Assets/Afterecho/Resources/Afterecho/RunnerCharts', team / 'Charts', dirs_exist_ok=True)
    shutil.copy2(ROOT / 'PlaytestExports/Runner/README.md', team / 'README.md')
    server = (ROOT / 'Tools/Runner/serve.py').read_text().replace(
        "Path(__file__).resolve().parents[2]/'Builds/RunnerCircleWeb'", "Path(__file__).resolve().parent/'WebGL'")
    server = server.replace("print('AFTERECHO runner test:",
                            "import threading,webbrowser\nthreading.Timer(1,lambda:webbrowser.open('http://127.0.0.1:8777')).start()\nprint('AFTERECHO runner test:")
    (team / 'serve.py').write_text(server)
    (team / '실행.command').write_text('#!/bin/zsh\ncd "$(dirname "$0")"\npython3 serve.py\n')
    os.chmod(team / '실행.command', 0o755)
    (team / '실행.bat').write_text('@echo off\r\ncd /d "%~dp0"\r\nwhere py >nul 2>nul\r\nif %errorlevel%==0 (py -3 serve.py) else (python serve.py)\r\npause\r\n')

    tools = ROOT / 'Tools/Runner'
    generator_files = []
    for original in iter_files(tools, generator_file_allowed, tooling=True):
        destination = team / 'Generator' / original.relative_to(tools)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(original, destination)
        generator_files.append((destination, destination.relative_to(out)))
    guide = team / 'Generator/README_PACKAGE.md'
    guide.parent.mkdir(parents=True, exist_ok=True)
    guide.write_text('# 채보 제작 도구\n\n'
                     '이 폴더는 Unity 프로젝트의 `Tools/Runner` 재현 도구 사본입니다. '
                     'Unity 소스 패키지에서 작업하거나, 이 폴더 내용을 해당 프로젝트의 `Tools/Runner`에 복사해 사용합니다. '
                     '선별 도구는 프로젝트의 원곡과 원본 채보를 사용하므로 WebGL 파일만으로는 실행하지 않습니다.\n\n'
                     '`sources/`에는 기준 후보와 출처 기록, `beatlearning/`에는 공개 모델 초안 생성 코드·환경 고정 파일·라이선스가 있습니다. '
                     '모델 가중치, Python 환경, 임시 생성 결과, 중복 음원은 포함하지 않았습니다. '
                     'AI 추론은 자동 실행되지 않으며 생성기 안내를 먼저 확인합니다.\n', encoding='utf-8')
    generator_files.append((guide, guide.relative_to(out)))
    # Use this run's explicit Generator file list, never stale files left in the
    # local delivery folder by an earlier run or a teammate's inference session.
    team_files = [(p, p.relative_to(out)) for p in iter_files(
        team, team_file_allowed, skip_directories={'Generator'},
        skip_relative_directories={'Review/' + name for name in GENERATED_REVIEW_DIRECTORIES})]
    reports = [archive(out / (NAME + '.zip'), team_files + generator_files)]

    files = []
    for directory in ['Assets', 'Packages', 'ProjectSettings', 'Tools/Runner', 'ArtProduction/Runner', 'PlaytestExports/Runner']:
        excluded_review = GENERATED_REVIEW_DIRECTORIES if directory == 'PlaytestExports/Runner' else ()
        for original in iter_files(ROOT / directory, skip_directories={'_Recovery', 'StreamingAssets'}, tooling=directory == 'Tools/Runner',
                                   skip_relative_directories=excluded_review):
            relative = original.relative_to(ROOT)
            if source_file_allowed(relative):
                files.append((original, Path(SOURCE_NAME) / relative))
    for relative in ['Tools/unity-local', 'Tools/test_unity_local.py', 'Tools/LOCAL_EDITOR_WORKFLOW.md']:
        files.append((ROOT / relative, Path(SOURCE_NAME) / relative))
    files.append((ROOT / 'PlaytestExports/Runner/README.md', Path(SOURCE_NAME) / 'README_RUNNER.md'))
    reports.append(archive(out / (SOURCE_NAME + '.zip'), files))
    (out / 'release-manifest.json').write_text(json.dumps(reports, indent=2))
    print(json.dumps(reports, indent=2))


if __name__ == '__main__':
    main()
