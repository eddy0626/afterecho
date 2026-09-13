# 잔향 AFTERECHO

한 손으로 곡의 채보에 맞춰 이동하고 공격하는 어두운 실내 리듬게임의 Unity 프로토타입입니다.

현재 공유 빌드: **0.2.2-movement.20260913**

[브라우저에서 플레이](https://play.unity.com/en/games/cc1a30d0-6ce8-46ae-90f4-9dab209b3d83/afterecho-0913)

## 프로젝트 열기

1. 이 비공개 저장소에 접근할 수 있는 계정으로 복제합니다.
   ```sh
   git clone https://github.com/eddy0626/afterecho.git
   ```
2. Unity Hub에서 복제한 폴더를 추가하고 **Unity 6000.6.0f1**로 엽니다. 웹 빌드는 Web Build Support 모듈이 필요합니다.
3. 최초 실행 시 Unity가 패키지와 Library를 복원할 때까지 기다립니다.
4. `Assets/Afterecho/Scenes/Stage01_BlackCorridor.unity`를 열고 Play를 누릅니다.

Feel 6.1, DOTween 및 DOTween Pro, 음원, 채보, 게임 스프라이트와 `.meta` 파일을 함께 보관합니다. Git LFS 없이 일반 Git으로 복제할 수 있습니다. 구입한 에셋의 원본이 포함된 팀 개발용 비공개 저장소이며, 각 에셋의 기존 라이선스가 적용됩니다.

## 플레이와 이번 변경

- 화면 탭·클릭 또는 Space/F/J로 입력합니다. Esc로 일시정지합니다.
- 튜토리얼은 별도 연습입니다. 실전 시작을 누르면 4박 준비 후 원곡과 채보가 0초에서 함께 시작합니다.
- 오디오 준비를 기다린 뒤 예약하며, 재생이 예기치 않게 중단되면 판정도 멈추고 4박 준비 후 재개합니다.
- Feel 이동 피드백: 140ms 탄성 변형, 작은 도약과 기울기, 잔상과 발밑 먼지를 추가했습니다. 움직임 줄이기 옵션과 일시정지·재시작 시 초기화를 지원합니다.
- 기존 Feel 판정·공격 효과, Gameaify 스프라이트와 ElevenLabs 효과음을 유지합니다.

## 웹 빌드

Build Profiles에서 **Afterecho_Web_Team_0913 - Desktop - Release**를 선택합니다.

프로필 경로: `Assets/Settings/Build Profiles/Afterecho_Web_Team_0913 - Desktop - Release.asset`

이 프로필이 버전 `0.2.2-movement.20260913` 등 배포 설정을 덮어씁니다. 프로필을 사용하지 않는 기본 Player Settings에는 이전 버전 값이 남아 있으므로 팀 배포에는 위 프로필을 사용합니다. 출력 위치는 `Builds/` 아래로 지정합니다. 빌드 결과물·캐시·임시 백업은 Git에서 제외합니다.

## 검증과 개발 도구

- `Afterecho > Run Core Tests`: 채보 및 게임 규칙 111개 검사 통과.
- `Afterecho > Run Music and Movement Playtest`: Play 모드에서 실행하는 음악·이동 실시간 검사 19개 통과.
- `Afterecho > Chart Lab`: 채보 편집 및 테스트.
- `Afterecho > Feedback Test Panel`: 판정·전투 피드백 확인.
- 실제 공유 웹 빌드에서 튜토리얼, 직접 입력, 100% 완료와 메뉴 복귀를 확인했습니다.

[음악·이동 변경과 Unity CLI 실행 안내](PlaytestExports/MUSIC_MOVEMENT_20260913.md), [실시간 검사 결과](PlaytestExports/music-movement-live.json), [웹 배포 확인 기록](PlaytestExports/music-movement-web.json), [효과음·이펙트 출처와 이전 QA](PlaytestExports/FEEDBACK_PROVENANCE_QA.md)를 참고하세요.

실제 휴대폰의 스피커 지연과 한 손 난이도는 팀 실기기 검증 항목입니다. 기존 선택적 패키지의 빌드 경고와 웹 FSR 경고는 검사 기록에 별도로 남겨 두었습니다.
