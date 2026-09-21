# 잔향 AFTERECHO — RUNNER

[브라우저에서 바로 플레이](https://play.unity.com/en/games/c81e0f9d-00d9-4904-87ee-8844fdfe0af6/afterecho-runner)

고정된 중앙 원으로 리듬을 맞추면서 캐릭터가 네 벽을 달리는 한 손 리듬게임입니다. 원곡 136.36초, 세 난이도, Gameaify UI·스프라이트, Feel·DOTween 피드백을 포함합니다.

현재 개발 브랜치: `feature/runner-circle` · Unity **6000.6.0f1**

## 실행

1. 저장소 권한이 있는 계정으로 복제합니다.
   ```sh
   git clone --branch feature/runner-circle https://github.com/eddy0626/afterecho.git
   ```
2. Unity Hub에서 폴더를 추가하고 6000.6.0f1로 엽니다.
3. 패키지 복원이 끝나면 `Assets/Afterecho/Scenes/Stage01_RunnerCircle.unity`를 열고 Play를 누릅니다.
4. 입문·보통·도전을 고르고 안전한 연습 또는 4박 후 시작을 누릅니다.

원이 겹치는 순간 화면 탭·클릭 또는 일반 키보드 키를 누릅니다. Esc는 일시정지입니다. HP 100, 성공 +1, 미스 −10, 헛입력 −3이며 15콤보부터 2.4배로 질주합니다. 음악 속도와 채보 시각은 바뀌지 않습니다. PC와 모바일 가로 화면용 테스트 버전입니다.

## 빌드와 테스트

개편용 프로필: `Assets/Settings/Build Profiles/Afterecho_RunnerCircle_Web_Test.asset`

Web Build Support 모듈을 설치하고 위 프로필로 `Builds/RunnerCircleWeb`에 빌드합니다. 사용자 지정 웹 실행 화면은 빌드 후 `python3 Tools/Runner/web_launcher.py`로 복원합니다. 로컬 플레이는 `python3 Tools/Runner/serve.py` 실행 후 `http://127.0.0.1:8777`을 엽니다.

- `Afterecho > Runner > Run Tests`: 판정·HP·질주·입력·완주·잘못된 설정 138개 검사.
- `Afterecho > Chart Lab`: runner 모드의 실제 채보와 규칙 편집, 자동 입력·무적·로그.
- 로컬 작업은 원본 CLI를 호출하지 않는 `Tools/unity-local`을 사용합니다. [키체인 재발 후 변경한 로컬 연결 방식](Tools/LOCAL_EDITOR_WORKFLOW.md)
- [코드 검토 수정 및 회귀검사](PlaytestExports/Runner/CODE_REVIEW_FIXES_20260921.md)
- [팀 실행·규칙·채보 안내](PlaytestExports/Runner/README.md)
- [Gameaify UI 제작 및 적용](PlaytestExports/Runner/UI_GAMEAIFY_20260921.md)
- [전체 개편 검증 기록](PlaytestExports/Runner/검증기록.md)

실제 휴대폰 오디오 지연·한 손 난이도·첫 로딩은 팀 실기기 검수 항목입니다. Chrome 화면 크기 에뮬레이션에서 새로고침 직후 화면이 작게 표시될 수 있으며 창 크기 변경/회전으로 정상화됩니다.

## 원본 보존과 팀 공유

기존 복도 씬 `Stage01_BlackCorridor.unity`와 [이전 공개 빌드](https://play.unity.com/en/games/cc1a30d0-6ce8-46ae-90f4-9dab209b3d83/afterecho-0913)는 비교용으로 보존합니다. 기존 씬 안내는 [이전 README](PlaytestExports/Runner/LEGACY_README_20260913.md)를 참고하세요.

구입한 Feel 6.1, DOTween 및 DOTween Pro 원본이 포함된 **팀 개발용 비공개 저장소**입니다. 각 에셋의 라이선스가 적용됩니다. 웹에는 실행 빌드만 게시하며 프로젝트 원본·계정 정보·개발 로그를 올리지 않습니다. 빌드·Library·Deliverables는 Git에서 제외합니다.
