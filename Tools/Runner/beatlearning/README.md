# 원시 AI 후보를 다시 생성할 때

이 폴더는 공개 BeatLearning 기준 모델의 **제작용 추론 도구**입니다. Unity 실행 중에는 사용하지 않습니다. 기존 저장 후보로 러닝 채보를 선별할 때에는 상위 폴더의 `prepare_charts.py`를 사용하면 되며 모델 설치·실행이 필요 없습니다.

공개 코드 `e17503f8cb6c21d6d9dee6a410537a7aa9261dfb`, `quaver_beart_v1.pt` 해시 `38e57556b96cc23ab5a2ccfb32e71ae0fa9ea049da61093477cb5d343694b3c5`를 고정합니다. 모델·decoder 수정이나 추가 학습은 하지 않습니다. `.15/.45/.75`는 모델 내부 EASY/NORMAL/INSANE 등급입니다. 게임 난이도는 별도 선별 규칙으로 만듭니다.

기존 실행 환경 기록은 Python 3.12.13, CPU PyTorch 2.14.0입니다. 의존성 잠금 파일은 당시 환경 기록이며, 이번 변경에서 새 환경 설치나 추론은 실행하지 않았습니다. Git과 ffmpeg도 필요합니다.

```sh
python3.12 -m venv .venv
.venv/bin/python -m pip install -r requirements-lock.txt
.venv/bin/python generate.py --audio /absolute/path/to/song.m4a --output /absolute/path/to/drafts
```

명시한 곡을 44100Hz mono 16-bit PCM으로 변환하고, 원본 파일 해시·정규화 PCM 해시·모델·코드·환경·모든 설정을 실행 식별자에 포함합니다. `raw/`에 세 난이도의 JSON·IBF와 음원·manifest를 함께 저장합니다. 캐시는 동일한 실행 식별자와 모든 출력 해시가 일치할 때만 재사용합니다. 다른 곡·설정 또는 옛 형식의 결과가 있으면 중단합니다. 새 출력 폴더를 사용하거나 의도적인 재생성에만 `--replace`를 지정하세요.

세 난이도가 모두 성공하고 검증될 때까지 기존 결과는 유지합니다. 출력 교체 도중 실패하면 이전 폴더를 복원합니다. Unity Resources나 공개 웹 빌드는 변경하지 않습니다. 새 추론 결과도 모두 청음 검수 대기입니다.

안전성 검사(모델·인터넷 불필요):

```sh
python3 -m unittest discover -s Tools/Runner/beatlearning -p 'test_*.py'
```

원본 프로젝트: https://github.com/sedthh/BeatLearning · 모델: https://huggingface.co/sedthh/BeatLearning
