# AFTERECHO Feel 연출 조정 가이드

> **마지막 갱신:** 2026-10-17 (콤보 30/180%, PERFECT 2배 반영) · 대상 씬: `Stage01_RunnerCircle`
> 연출을 추가하거나 바꿀 때마다 이 문서도 함께 고칩니다. 아래 "현재 값"은 씬과 프리팹에서 직접 읽은 값입니다.

---

## 1. 조정하는 방법 (공통)

1. `Stage01_RunnerCircle`에서 Play를 누른다.
   - HP 관련 연출(HP바, 휘청임)은 **실전에서만** 보인다. 연습 모드는 HP가 줄지 않는다.
2. Hierarchy에서 `Afterecho_RunnerCircle/Feedbacks/FB_Runner_…`를 선택한다.
3. 인스펙터의 **MMF Player**에서 피드백을 펼쳐 값을 바꾼다. 피드백 오른쪽의 재생 버튼으로 바로 확인할 수 있다.
4. **Keep Playmode Changes**를 켜 두면 Play를 멈춘 뒤에도 값이 남는다.
5. 정한 값은 인스펙터 상단 **Overrides → Apply All**로 프리팹에 반영한다. 이렇게 해야 팀원에게도 같은 값이 간다.
   - **Prefab Variant 주의:** `FB_Runner_HitPerfect`와 `FB_Runner_JudgementPerfect`는 Variant다. 반영할 대상을 고를 때 Variant 쪽을 고른다. 원본을 고르면 GOOD 효과까지 바뀐다.
6. Keep Playmode Changes를 끄고, 씬을 저장(Ctrl+S)한다.

**알아 둘 점**
- **값은 두 곳에 나뉘어 있다.**
  - 효과 값(길이·세기·곡선)은 **프리팹**에 있다.
  - 효과 대상(Target)은 **씬**에 있다.
- **설치 메뉴는 값을 덮어쓰지 않는다.** 메뉴 `Afterecho > Runner > Install Feedbacks In Open Scene`은 빠진 연결만 채우고, 이미 있는 프리팹 값은 건드리지 않는다.
  - **씬 저장 주의:** 이 메뉴는 씬을 저장한다. 저장 안 된 변경이 있으면 그것까지 함께 저장되니, 먼저 저장하거나 되돌린 뒤 실행한다.

**하지 말 것**
- `Assets/Feel/` 원본이나 데모 수정
- Timing을 Scaled로 바꾸기
- Initial Delay 넣기
- 화면 전체 번쩍임을 초당 3회 이상 내기 (접근성 기준 WCAG 2.3.1)
- 판정 효과를 0.15초보다 길게 만들기 (최소 노트 간격이 0.17초)
  - **예외:** 이 프로젝트의 효과는 다시 재생될 때 처음부터 시작하게 되어 있어서, 길어도 화면이 깨지지는 않는다. 다만 다음 판정 때 앞 효과가 중간에 끊긴다.

---

## 2. 필드 읽는 법

| Feel 효과 | 길이 | 세기 | 모양(곡선) |
|---|---|---|---|
| Squash & Stretch | `AnimateScaleDuration` | `RemapCurveOne` = 최대 배율 (`RemapCurveZero` 1이 원래 크기). 1보다 작으면 찌그러진다 | `AnimateCurve` |
| Scale | `AnimateScaleDuration` | `RemapCurveZero` = 곡선 0일 때 크기, `RemapCurveOne` = 곡선 1일 때 크기 | `AnimateScaleTweenX` (Uniform Scaling이라 X 곡선 하나로 전체) |
| Rotation | `AnimateRotationDuration` | `RemapCurveOne` = 최대 각도(도). **음수는 시계 방향** | `AnimateRotationTweenZ` |
| Image Alpha | `Duration` | `CurveRemapOne` = 최대 투명도(0~1) | `Curve` |
| TMP Color | `Duration` | `DestinationColor` = 바뀌는 색 | `ColorCurve` (0 = 원래 색, 1 = 바뀐 색) |

모든 효과의 Timing은 `TimescaleMode = Unscaled`, `InitialDelay = 0`을 유지한다.

---

## 3. 순간별 조정 위치

경로는 모두 `Afterecho_RunnerCircle/` 아래이고, 프리팹은 `Feedbacks/` 아래에 있다.

### 판정 성공 (GOOD / PERFECT)

| 무엇 | 프리팹 | 대상 | 현재 값 | 메모 |
|---|---|---|---|---|
| 판정 링 반응 (GOOD) | `FB_Runner_Hit` | `…/Judge/HitResponse` | Squash 1.16, 0.115초 | |
| 판정 링 반응 (PERFECT) | `FB_Runner_HitPerfect` | 같음 | 1.25 | `FB_Runner_Hit`의 Variant. 길이·곡선은 원본에서 물려받음 |
| 고정 링 튐 | `FB_Runner_TargetPop` | `…/Judge/FixedTarget` | Scale 1.12, 0.12초 | GOOD·PERFECT 공통 |
| 가운데 점 튐 | `FB_Runner_DotPop` | `…/Judge/CenterDot` | Scale 1.6, 0.12초 | GOOD·PERFECT 공통 |
| 판정 글자 (GOOD, OVERDRIVE) | `FB_Runner_JudgementText` | `…/SafeArea/Judgement` | Scale 1.4, 0.12초 | |
| 판정 글자 (PERFECT) | `FB_Runner_JudgementPerfect` | 같음 | 2.0 | `FB_Runner_JudgementText`의 Variant |

### 미스 / 헛입력

| 무엇 | 프리팹 | 대상 | 현재 값 | 메모 |
|---|---|---|---|---|
| 판정 글자 기울기 (MISS, OFF BEAT) | `FB_Runner_JudgementTilt` | `…/SafeArea/Judgement` | Rotation −12도, 0.5초 | 곡선이 "툭 → 반동 → 다시 툭 → 제자리" 모양이다. 회전 기준점은 코드가 글자 왼쪽 끝으로 자동으로 옮긴다 |
| 캐릭터 비틀거림 (헛입력) | `FB_Runner_Stumble` | `…/RunnerVisual_Feel/Sprite` | Rotation −9도, 0.12초 | |
| 캐릭터 넘어짐 (미스) | `FB_Runner_Fall` | `…/RunnerVisual_Feel` | Squash 0.8, 0.15초 | |

### HP 감소 (실전만)

| 무엇 | 위치 | 현재 값 | 메모 |
|---|---|---|---|
| HP바 튐 | 프리팹 `FB_Runner_HpLoss` → `…/SafeArea/Health` | Squash 1.1, 0.17초 | |
| HP 게이지 휘청임 | 프리팹 `FB_Runner_HpWobble` → `…/Health/HpBackground` | Rotation 7도, 0.35초 | |
| 늦게 따라오는 바 | `…/Health/HpBackground`의 **MMProgressBar** | `DecreasingDelay` 0.2초, `LerpDecreasingDelayedBarDuration` 0.3초 | `LerpForegroundBar`는 끔(HP는 즉시 갱신). 바 자체 튐(`BumpScaleOnChange`)도 끔. 성공으로 HP가 오르면 바로 맞춰짐 |
| 늦게 따라오는 바 색 | `…/HpBackground/HpDelayed`의 Image Color | `#FFF7E0`, 알파 0.9 | 위치·크기·스프라이트는 실행할 때 `HpFill`에서 복사됨 |

### 콤보

| 무엇 | 위치 | 현재 값 | 메모 |
|---|---|---|---|
| 콤보 증가 튐 | 프리팹 `FB_Runner_ComboPop` → `…/SafeArea/Combo` | Scale 1.25, 0.12초 | |
| 콤보 끊김 휘청임 | 프리팹 `FB_Runner_ComboBreakWobble` → `Combo` | Rotation 10도, 0.35초 | |
| 콤보 끊김 빨간색 | 프리팹 `FB_Runner_ComboBreakColor` → `Combo` | `DestinationColor` `#FF382E`, 0.45초 | Text 채널이라 움직임 줄이기 때도 유지 |
| 콤보 숫자 커지기 | `Afterecho_RunnerCircle`의 **RunnerView** → Combo text growth | `comboGrowthFullAt` 30, `comboGrowthMax` 0.8 | Feel이 아닌 코드 값이다. 콤보에 비례해 커진다: 15콤보 140%, 30콤보부터 최대 180%, 0이 되면 100%. 씬 값이라 Apply 없이 씬만 저장하면 된다 |

### 박자

| 무엇 | 위치 | 현재 값 | 메모 |
|---|---|---|---|
| 박자 링 맥동 | 프리팹 `FB_Runner_BeatPulse` → `…/Judge/BeatPulse` | Scale 1.12, 0.45초 | 곡선 앞 10%에서 최대가 됐다가 천천히 돌아옴. 박자는 `stage.json`의 `beats`와 판정과 같은 음악 시계로 맞춘다 |
| 박자 링 모양 | `…/Judge/BeatPulse`의 **RunnerRing** | 지름 300(RectTransform Width·Height), Thickness 2, Color `#FFF7E0` 알파 0.45 | 다가오는 노트 링(최대 지름 268)보다 크게 둘 것 |

### 질주 시작 (OVERDRIVE)

| 무엇 | 위치 | 현재 값 | 메모 |
|---|---|---|---|
| 캐릭터 확대 | 프리팹 `FB_Runner_BoostStart` → `…/RunnerVisual_Feel` | Squash 1.22, 0.18초 | |
| 화면 번쩍임 | 프리팹 `FB_Runner_BoostFlash` → `RunnerCanvas/BoostFlash` | `CurveRemapOne` 0.45(최대 투명도), 0.3초 | Flash 채널. 번쩍임 색은 `BoostFlash` Image의 Color에서 바꾸되, **알파는 0으로 둔다** |
| OVERDRIVE 글자 | `FB_Runner_JudgementText` | GOOD과 같은 값 | |

### 결과 화면 (클리어·실패)

| 무엇 | 위치 | 현재 값 | 메모 |
|---|---|---|---|
| 결과 창 등장 | 프리팹 `FB_Runner_ResultPop` → `…/SafeArea/MenuOverlay` | `RemapCurveZero` 0.85(시작 크기), `RemapCurveOne` 1, 0.3초 | 곡선이 1에서 끝나야 창이 원래 크기로 멈춘다 |

---

## 4. 연결 바꾸기 (Bindings)

`Afterecho_RunnerCircle`의 **RunnerFeedbackPlayer → Bindings**는 "어떤 순간(ID)에, 어떤 채널로, 어떤 플레이어를 재생할지"를 정한다.

- 한 ID에 여러 플레이어를 묶을 수 있다.
- **채널:**
  - `Motion`·`Flash`는 움직임 줄이기를 켜면 꺼진다.
  - `Audio`·`Text`는 계속 재생된다.
- 연결을 손으로 고쳤다면, 씬을 다시 생성할 때도 유지되도록 `RunnerFeedbackInstaller.cs`에도 반영해야 한다.

| ID | 언제 | 현재 연결 |
|---|---|---|
| `HitGood` | GOOD 판정 | Hit, TargetPop, DotPop |
| `HitPerfect` | PERFECT 판정 | HitPerfect, TargetPop, DotPop |
| `JudgementText` | GOOD 글자, OVERDRIVE 글자 | JudgementText |
| `JudgementPerfect` | PERFECT 글자 | JudgementPerfect |
| `JudgementMiss` | MISS, OFF BEAT 글자 | JudgementTilt |
| `HpLoss` | HP 감소 | HpLoss, HpWobble |
| `Fall` | 미스로 넘어짐 | Fall |
| `Stumble` | 헛입력 | Stumble |
| `BoostStart` | OVERDRIVE 시작 | BoostStart, BoostFlash(Flash 채널) |
| `ComboUp` | 콤보 증가 | ComboPop |
| `ComboBreak` | 콤보 끊김 | ComboBreakWobble, ComboBreakColor(Text 채널) |
| `Beat` | 박자, 준비 카운트 | BeatPulse |
| `ResultShow` | 결과 창 표시 | ResultPop |

---

## 5. 변경 기록

| 날짜 | 변경 | 커밋 |
|---|---|---|
| 2026-10-17 | 기존 효과 4개를 프리팹으로 이전 (Hit, HpLoss, BoostStart, Fall) | `12ad2f6` |
| 2026-10-17 | PERFECT 판정 링 1.25 (Variant) | `193bfb5` |
| 2026-10-17 | 판정 글자 튐 1.2 → 1.4 | `f206517`, `d2dbb39` |
| 2026-10-17 | HP바 늦게 따라오는 바 (MMProgressBar) | `9ae34d3` |
| 2026-10-17 | OVERDRIVE 화면 번쩍임 | `6056cce` |
| 2026-10-17 | 비틀거림 DOTween → Feel | `ba70271` |
| 2026-10-17 | 결과 창 등장 | `50673db` |
| 2026-10-17 | HP 좌우 흔들림 → 휘청임(회전), 고정 링·가운데 점 튐, 콤보 튐·끊김, 박자 링 | `7d7a240` |
| 2026-10-17 | 콤보 숫자 커지기, PERFECT 글자 1.7, MISS·OFF BEAT 글자 기울기 | `cbd513e` |
| 2026-10-17 | 콤보 숫자 50콤보 160% → 30콤보 180% (15콤보 140%), PERFECT 글자 1.7 → 2.0 | (이번 커밋) |
