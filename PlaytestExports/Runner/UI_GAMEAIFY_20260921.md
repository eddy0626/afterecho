# Gameaify UI 적용 기록 — 2026-09-21

Gameaify Editor에서 GPT-Image-2로 메뉴 프레임·버튼 플레이트·HP 테두리·질주 배지를 한 장의 아틀라스로 생성한 뒤, 같은 서비스의 배경 지우기로 실제 알파 투명도를 만들었다. 원본과 출처는 `ArtProduction/Runner/UI`에 보관했다.

연결된 Unity Editor의 Sprite Editor Data Provider로 편집 가능 여부를 확인하고 네 개의 스프라이트와 9-slice 테두리를 저장했다. Unity uGUI 글자·실제 HP 잔량·노트 원은 별도 오브젝트로 유지한다. `Tools/Runner/import_ui.cs`와 `RunnerUiStyling.Apply()`로 임포트·적용을 재현할 수 있다.

기존 Square 스프라이트에 텍스처 연결이 없어서 새 아틀라스와 같은 UI 배치에서 HP가 깨져 보이는 문제를 확인했다. 러닝 모드 전용 `RunnerWhite.asset`을 Unity API로 생성하고 HP·곡 진행률에 연결했다. 기존 복도 에셋은 수정하지 않았다.

검증: 메뉴·일시정지 화면, 질주 배지, 전체 HP 표시를 실제 Unity Play 화면에서 확인했다. 러닝 규칙 101개 검사 통과. 웹 배포 후 결과는 별도 기록한다.

중앙 판정 UI와 테두리 러너가 겹치지 않도록 미리보기 원의 최대 표시 지름, 콤보·HP 위치와 러너 표시 크기를 조정했다. 1.2초 미리보기 시간과 실제 판정 시각은 유지한다.

최종 WebGL 빌드 `build_f935b3542eb2`: 오류 0, 경고 407, 약 33.5MB. 기존 선택적 패키지·vendor import 경고는 `build-warnings.json`에 보관한다. [게시된 러닝 버전](https://play.unity.com/en/games/c81e0f9d-00d9-4904-87ee-8844fdfe0af6/afterecho-runner)에서 새 UI, 카운트, 키보드 입력, HP 감소와 일시정지를 확인했고 검사 시 브라우저 console error는 0건이었다. 실제 휴대폰 검수는 별도이다.
