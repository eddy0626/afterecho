# 잔향 로컬 Unity 작업

이미 열려 있는 이 프로젝트의 Unity Editor를 조작할 때는 `Tools/unity-local`을 사용한다. Python 3 표준 라이브러리로 Unity Pipeline의 인증된 로컬 연결을 사용하는 프로젝트 전용 명령 도구다. 원본 `unity` 실행 파일을 호출하지 않는다.

프로젝트 루트에서 실행한다. Unity Editor가 이 프로젝트를 열고 Pipeline 서버가 준비되어 있어야 한다. Unity AI Assistant 크레딧이나 Unity 계정 로그인은 이 연결에 사용하지 않는다.

```sh
# 연결된 Editor 상태와 제공하는 명령 확인
Tools/unity-local status --format json
Tools/unity-local list --format json

# Play 종료 후, 스크립트 컴파일이 완료된 Editor에서 도구 회귀검사
Tools/unity-local command editor_stop --format json
Tools/unity-local command eval_file --file "$PWD/Tools/Runner/tooling_regressions.cs" --format json

# 플레이 확인
Tools/unity-local command editor_play --format json
```

`tooling_regressions.cs`는 편집 중인 채보 검증, 기본 채보 검사와의 구분, 기존 전투 모드 자동 입력의 게임오버 처리를 확인한다. Play 중에는 실행을 거절한다. 성공 메시지뿐 아니라 반환된 검사 결과와 Editor 컴파일 오류도 확인한다.

실행 규칙:

- `command`(`cmd`), `status`, `list`, `job`만 제공한다. 다른 프로젝트·실행 중인 게임·원격 서버로 연결 대상을 바꾸지 않는다.
- 이 저장소의 `Library/Pipeline/.unity-pipeline-port`에서 열린 Editor의 연결 정보를 읽는다. 이 파일은 Unity가 생성하는 현재 사용자 전용 파일이다. 프로젝트·모드·프로세스·포트·파일 권한을 확인하고 기존 세션 인증을 유지한다.
- HTTP 연결은 `127.0.0.1`로 제한한다. 프록시와 리다이렉트를 사용하지 않으며, 연결 인증정보를 출력하거나 저장소에 복사하지 않는다.
- 계정 조회·로그인·doctor·클라우드 명령 및 원본 CLI로의 자동 재시도는 없다. 연결 실패 시 Unity 화면에서 열린 프로젝트와 Pipeline 상태를 확인한다.
- 원본 Unity CLI의 완전한 대체품은 아니다. 설치·라이선스·클라우드 작업이 별도로 필요하면 해당 작업으로 분리한다.

## 키체인 재발 후 변경

2026-09-21 처음 만든 래퍼는 원본 CLI에 비대화형 옵션만 전달했다. 사용자 화면에서 키체인 창이 계속 나타나 이 방식이 충분하지 않음을 확인했다. 현재 버전은 원본 실행 파일 호출 자체를 제거했다. 원본 CLI를 직접 실행하면 키체인 요청이 다시 나타날 수 있다.

이미 떠 있는 요청 창은 이 도구가 닫지 않는다. 사용자가 macOS 창의 **거부**를 눌러 닫는다. 비밀번호나 '항상 허용'은 이 로컬 작업에 필요하지 않다. macOS 키체인 보호, 저장된 비밀번호, Unity Hub 로그인은 변경하지 않는다. 다른 앱이나 원본 CLI에서 만드는 인증창을 전역 차단하는 기능도 아니다.

프로토콜 근거는 설치된 `com.unity.pipeline` 패키지의 `Documentation~/connectivity.md` 중 'Discovering and calling an instance', 'Authentication', 'Capability negotiation'이다. 로컬 도구의 회귀검사는 `python3 Tools/test_unity_local.py`로 실행한다.
