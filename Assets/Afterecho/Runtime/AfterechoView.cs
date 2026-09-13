using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using DG.Tweening;
using MoreMountains.Feedbacks;

namespace Afterecho
{
    public sealed class AfterechoView : MonoBehaviour
    {
        public Camera gameCamera;
        public Transform playerRoot, playerVisual, enemyRoot, enemyVisual, doorRoot, doorLeft, doorRight;
        public SpriteRenderer playerSprite, enemySprite;
        public SpriteRenderer[] floorPool;
        public SpriteRenderer[] edgePool;
        public SpriteRenderer footHalo, enemyHalo;
        public MMF_Player stepFeedback, attackFeedback, damageFeedback;
        public AfterechoImpactFeedback impactFeedback;
        public AfterechoMovementFeedback movementFeedback;
        public AudioSource sfx;
        public AudioClip stepSound, hitSound, doorSound, warningSound, damageSound;
        public RectTransform safeArea, overlayPanel, judgment, rail;
        public CanvasGroup overlay;
        public GameObject portrait, primaryButton, secondaryButton, menuExtras;
        public UnityEngine.UI.Button primary, secondary, pauseButton;
        public UnityEngine.UI.Button[] difficulties;
        public UnityEngine.UI.Slider volumeSlider, syncSlider;
        public UnityEngine.UI.Toggle motionToggle;
        public TMP_Text title, subtitle, body, primaryText, secondaryText, sectionText, progressText, healthText, comboText, scoreText, statusText, resultText, syncText, difficultyText, enemyText, countText;
        public UnityEngine.UI.Image progressFill, enemyFill, hitMark;
        public RectTransform[] noteLeft, noteRight;
        public UnityEngine.UI.Image[] noteLeftImages, noteRightImages;
        AfterechoGame game;
        readonly List<Vector3> route = new List<Vector3>();
        readonly HashSet<Vector2Int> corridor = new HashSet<Vector2Int>();
        Tween moveTween, attackTween, hitTween, panelTween;
        int shownStep = -1, lastTileX = int.MinValue, lastTileY = int.MinValue;
        float reactionUntil;
        string reaction = "", shownEnemy = "";
        bool doorOpened;
        Vector3 doorFrom;
        Vector3 cameraVelocity;
        static readonly Color Teal = new Color(.55f,.88f,.82f), Bone = new Color(.89f,.90f,.82f), Red = new Color(.94f,.34f,.30f);
        public void Initialize(AfterechoGame owner)
        {
            game = owner; BuildRoute();
            primary.onClick.AddListener(PrimaryAction); secondary.onClick.AddListener(SecondaryAction);
            pauseButton.onClick.AddListener(game.PauseGame);
            string[] names = { "easy","normal","hard" };
            for (int i = 0; i < difficulties.Length; i++) { string p = names[i]; difficulties[i].onClick.AddListener(() => game.SelectDifficulty(p)); }
            volumeSlider.SetValueWithoutNotify(game.volume); volumeSlider.onValueChanged.AddListener(game.SetVolume);
            syncSlider.SetValueWithoutNotify(game.syncMs); syncSlider.onValueChanged.AddListener(game.SetSync);
            motionToggle.onValueChanged.AddListener(value => game.reducedMotion = value);
            stepFeedback.Initialization(); attackFeedback.Initialization(); damageFeedback.Initialization();
            if (impactFeedback == null) impactFeedback = GetComponent<AfterechoImpactFeedback>();
            if (impactFeedback != null) impactFeedback.Initialize(game, this);
            if (movementFeedback == null) movementFeedback = GetComponent<AfterechoMovementFeedback>();
            if (movementFeedback == null) movementFeedback = gameObject.AddComponent<AfterechoMovementFeedback>();
            movementFeedback.Initialize(game,this);
        }
        void PrimaryAction()
        {
            if (game.Phase == GamePhase.Menu || game.Phase == GamePhase.Result) game.StartGame();
            else if (game.Phase == GamePhase.Ready) game.BeginMain();
            else if (game.Phase == GamePhase.Paused) game.ResumeGame();
        }
        void SecondaryAction() { game.Menu(); }
        void BuildRoute()
        {
            route.Clear(); corridor.Clear();
            if (game == null || game.Stage == null || game.Stage.route == null) return;
            var raw = game.Stage.route;
            // Repeat the original automatic corridor as needed for dense charts. Each hit is a whole tile.
            for (int repeat = 0; repeat < 8; repeat++)
            {
                for (int p = 0; p < raw.Length - 1; p++)
                {
                    Vector2 a = new Vector2(raw[p].x + 64 * repeat,raw[p].y - repeat);
                    Vector2 b = new Vector2(raw[p+1].x + 64 * repeat,raw[p+1].y - repeat);
                    int length = Mathf.RoundToInt(Vector2.Distance(a,b));
                    for (int n = 0; n < length; n++) route.Add(Vector2.Lerp(a,b,n/(float)length));
                }
            }
            foreach (var p in route)
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) corridor.Add(new Vector2Int(Mathf.RoundToInt(p.x)+dx,Mathf.RoundToInt(p.y)+dy));
        }
        Vector3 StepPoint(int step) => route[Mathf.Clamp(step,0,route.Count-1)];
        public void ResetWorld()
        {
            StopEffects(); shownStep = 0; doorOpened = false; shownEnemy = "";
            if (route.Count == 0) return;
            playerRoot.position = StepPoint(0); playerVisual.localPosition = Vector3.zero;
            playerVisual.localScale = Vector3.one; enemyVisual.localScale = Vector3.one;
            gameCamera.transform.position = playerRoot.position + new Vector3(0,2.0f,-10);
            doorRoot.gameObject.SetActive(false); enemyRoot.gameObject.SetActive(false); enemyHalo.gameObject.SetActive(false);
            doorLeft.localPosition = new Vector3(-.5f,0,0); doorRight.localPosition = new Vector3(.5f,0,0);
            reaction = ""; reactionUntil = 0; lastTileX = int.MinValue; UpdateTiles();
        }
        public void StopEffects()
        {
            moveTween?.Kill(); attackTween?.Kill(); hitTween?.Kill();
            DOTween.Kill("AfterechoWorld");
            if (stepFeedback != null) { stepFeedback.StopFeedbacks(); stepFeedback.RestoreInitialValues(); }
            if (attackFeedback != null) { attackFeedback.StopFeedbacks(); attackFeedback.RestoreInitialValues(); }
            if (damageFeedback != null) { damageFeedback.StopFeedbacks(); damageFeedback.RestoreInitialValues(); }
            if (impactFeedback != null) impactFeedback.StopAllFeedbacks();
            if (movementFeedback != null) movementFeedback.StopFeedback();
            if (sfx != null) sfx.Stop();
            if (!doorOpened && shownStep >= 0 && route.Count > 0) playerRoot.position = StepPoint(shownStep);
            if (playerVisual != null) playerVisual.localPosition = Vector3.zero;
            if (judgment != null) judgment.localScale = Vector3.one;
            if (enemySprite != null) enemySprite.color = Color.white;
        }
        public void React(RunEvent e)
        {
            bool motion = game != null ? !game.reducedMotion : true;
            if (impactFeedback != null)
            {
                impactFeedback.HandleRunEvent(e);
            }

            if (e.kind == "step" || e.kind == "attack")
            {
                double window = (game != null && game.Run != null && e.index >= 0 && e.index < game.Run.Chart.notes.Length)
                    ? game.Run.WindowAt(e.index)
                    : 0.150;
                double maxPerfectMargin = Math.Min(0.035, 0.5 * window);
                bool isPerfect = Math.Abs(e.error) <= maxPerfectMargin;

                reaction = isPerfect ? "PERFECT" : e.error < 0 ? "EARLY · GOOD" : "LATE · GOOD";
                reactionUntil = Time.unscaledTime + .28f;
                hitTween?.Kill(); judgment.localScale = Vector3.one;
                if (motion) hitTween = judgment.DOPunchScale(Vector3.one * (isPerfect ? .16f : .10f), .10f, 1, .15f).SetUpdate(true).SetLink(judgment.gameObject);
                hitMark.color = Teal;
                if (e.kind == "step")
                {
                    Vector3 from = playerRoot.position;
                    if (!e.practice)
                    {
                        shownStep = (game != null && game.Run != null) ? game.Run.Steps : (shownStep + 1);
                        moveTween?.Kill();
                        if (playerRoot != null && route.Count > 0)
                        {
                            // Logical step commits immediately. Animation always catches up within the shortest note gap.
                            moveTween = playerRoot.DOMove(StepPoint(shownStep),motion ? .095f : .045f).SetEase(Ease.OutCubic).SetUpdate(true).SetId("AfterechoWorld").SetLink(playerRoot.gameObject);
                        }
                    }
                    stepFeedback.StopFeedbacks(); stepFeedback.RestoreInitialValues(); stepFeedback.ResetAllCooldowns();
                    if (movementFeedback != null) movementFeedback.PlayStep(from, e.practice ? from : StepPoint(shownStep),e.practice);
                    else if (motion) stepFeedback.PlayFeedbacks();
                    if (impactFeedback == null) sfx.PlayOneShot(stepSound,.14f);
                }
                else
                {
                    if (movementFeedback != null) movementFeedback.StopBodyFeedback();
                    attackFeedback.StopFeedbacks(); attackFeedback.RestoreInitialValues(); attackFeedback.ResetAllCooldowns();
                    if (motion) attackFeedback.PlayFeedbacks();
                    if (impactFeedback == null)
                    {
                        sfx.PlayOneShot(hitSound,.22f);
                        attackTween?.Kill(); enemySprite.color = Teal;
                        attackTween = enemySprite.DOColor(Color.white,.12f).SetUpdate(true).SetId("AfterechoWorld").SetLink(enemySprite.gameObject);
                    }
                }
            }
            if (e.kind == "miss" || e.kind == "extra")
            { reaction = e.kind == "miss" ? "MISS" : "EXTRA"; reactionUntil = Time.unscaledTime + .24f; hitMark.color = new Color(.68f,.48f,.42f); }
            if (e.kind == "warning") sfx.PlayOneShot(warningSound,.22f);
            if (e.kind == "kill") { reaction = "BREAK"; reactionUntil = Time.unscaledTime + .4f; }
            if (e.kind == "damage")
            {
                reaction = game.invincible ? "GUARD · TEST" : "DAMAGE −1"; reactionUntil = Time.unscaledTime + .6f;
                damageFeedback.StopFeedbacks(); damageFeedback.RestoreInitialValues();
                if (motion) damageFeedback.PlayFeedbacks();
                sfx.PlayOneShot(damageSound,.27f);
            }
            if (e.kind == "door") OpenDoor();
        }
        void OpenDoor()
        {
            doorOpened = true; doorFrom = playerRoot.position; doorRoot.gameObject.SetActive(true); doorRoot.position = doorFrom + new Vector3(0,3,0);
            sfx.PlayOneShot(doorSound,.4f); moveTween?.Kill();
        }

        void UpdateTiles()
        {
            int cx = Mathf.RoundToInt(playerRoot.position.x), cy = Mathf.RoundToInt(playerRoot.position.y);
            if (cx == lastTileX && cy == lastTileY) return;
            lastTileX = cx; lastTileY = cy;
            for (int i = 0; i < floorPool.Length; i++)
            {
                int dx = i % 19 - 9, dy = i / 19 - 7;
                var cell = new Vector2Int(cx + dx,cy + dy);
                bool path = corridor.Contains(cell) || doorOpened;
                var tile = floorPool[i]; tile.transform.position = new Vector3(cell.x,cell.y,0);
                tile.gameObject.SetActive(path);
                edgePool[i].transform.position = new Vector3(cell.x,cell.y,0);
                bool edge = !path && (corridor.Contains(cell+Vector2Int.up)||corridor.Contains(cell+Vector2Int.down)||corridor.Contains(cell+Vector2Int.left)||corridor.Contains(cell+Vector2Int.right));
                edgePool[i].gameObject.SetActive(edge);
            }
        }
        public void Render(double time,GamePhase phase)
        {
            if (game.Run == null) return;
            Rect safe = Screen.safeArea;
            safeArea.anchorMin = new Vector2(safe.x / Screen.width,safe.y / Screen.height);
            safeArea.anchorMax = new Vector2(safe.xMax / Screen.width,safe.yMax / Screen.height);
            portrait.SetActive(Screen.height > Screen.width);
            var run = game.Run;
            int beat = Array.BinarySearch(game.Stage.beats,time); if (beat < 0) beat = Math.Max(0,~beat-1);
            beat = Math.Min(beat,game.Stage.beats.Length-1);
            double elapsed = Math.Max(0,time-game.Stage.beats[beat]);
            float pulse = (float)Math.Exp(-elapsed * 12);
            for (int i = 0; i < floorPool.Length; i++)
            {
                float distance = Vector3.Distance(floorPool[i].transform.position,playerRoot.position);
                float light = Mathf.Lerp(.04f,.78f,Mathf.Clamp01(1-distance/8f));
                if (game.reducedMotion) pulse = 0;
                floorPool[i].color = new Color(light*.72f,light*(.9f+pulse*.06f),light,1);
            }
            if (doorOpened)
            {
                float elapsedDoor = (float)Math.Max(0,time-game.Stage.doorTime);
                float opening = Mathf.SmoothStep(0,1,elapsedDoor/.8f);
                doorLeft.localPosition = new Vector3(Mathf.Lerp(-.5f,-1.25f,opening),0,0);
                doorRight.localPosition = new Vector3(Mathf.Lerp(.5f,1.25f,opening),0,0);
                playerRoot.position = Vector3.Lerp(doorFrom,doorRoot.position+new Vector3(0,1.3f,0),Mathf.SmoothStep(0,1,(elapsedDoor-.35f)/1.8f));
            }
            UpdateTiles();
            if (phase == GamePhase.Playing || phase == GamePhase.Preparing)
            {
                Vector3 target = playerRoot.position + new Vector3(0,2,-10);
                gameCamera.transform.position = Vector3.SmoothDamp(gameCamera.transform.position,target,ref cameraVelocity,.085f,100,Time.unscaledDeltaTime);
            }
            footHalo.transform.position = playerRoot.position + new Vector3(0,-.35f,0);
            footHalo.color = new Color(.3f,.75f,.66f,.17f+pulse*.05f);
            StageSection section = game.Stage.sections[0];
            foreach (var s in game.Stage.sections) if (beat >= s.fromBeat) section = s;
            sectionText.text = $"01   /   {section.name}   <color=#668080>{section.code}</color>";
            float progress = game.IsTutorial ? 0 : (float)Math.Clamp(time/game.Stage.doorTime,0,1);
            progressFill.fillAmount = progress; progressText.text = $"{progress*100:00}%";
            healthText.text = new string('●',run.Health) + "<color=#384242>" + new string('○',run.Damage) + "</color>";
            comboText.text = $"<size=36>{run.Combo:00}</size>  <color=#809996>COMBO</color>";
            scoreText.text = $"{run.Score:000000}    ×{run.Multiplier}";
            difficultyText.text = game.autoPlay ? "AUTO PLAY · TEST" : game.invincible ? "INVINCIBLE · TEST" : game.difficulty == "easy" ? "입문" : game.difficulty == "normal" ? "보통" : "도전";
            syncText.text = $"싱크  {game.syncMs:+0;-0;0} ms";
            countText.gameObject.SetActive(phase == GamePhase.Preparing);
            if (game.IsTutorial && phase == GamePhase.Playing && time < game.Stage.beats[game.Stage.listenBeats])
                statusText.text = $"먼저 음악을 들으세요   {Math.Max(1,4-beat)}";
            else if (game.IsTutorial && phase == GamePhase.Playing && time < game.Stage.MainTime) statusText.text = "안전한 연습 · 실수해도 괜찮아요";
            else if (Time.unscaledTime < reactionUntil) statusText.text = reaction;
            else statusText.text = phase == GamePhase.Playing ? "" : "ONE TAP · ONE ACTION";
            var enemy = run.VisibleEnemy;
            if (enemy != null && phase != GamePhase.Menu && phase != GamePhase.Result)
            {
                if (shownEnemy != enemy.data.id || enemy.state == EnemyState.Warning) { shownEnemy = enemy.data.id; enemyRoot.position = StepPoint(shownStep+3); }
                enemyRoot.gameObject.SetActive(true); enemyHalo.gameObject.SetActive(true);
                enemyHalo.transform.position = enemyRoot.position + new Vector3(0,-.55f,0);
                enemyHalo.color = new Color(.8f,.12f,.08f,.16f+pulse*.09f);
                if (attackTween == null || !attackTween.IsActive()) enemySprite.color = enemy.state == EnemyState.Warning ? new Color(.38f,.45f,.45f,.75f) : Color.white;
                enemyText.text = enemy.state == EnemyState.Warning ? $"기척 접근  ·  {Math.Ceiling((enemy.data.start-time)/game.Stage.beat):0}박" : $"{enemy.remaining} / {enemy.data.hp}    ·    {Math.Max(0,enemy.data.end-time):0.0}s";
                enemyFill.fillAmount = enemy.remaining/(float)enemy.data.hp;
                Vector2 local;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(safeArea,gameCamera.WorldToScreenPoint(enemyRoot.position+Vector3.up*1.65f),null,out local);
                local.y+=safeArea.rect.height*.5f;
                local.x=Mathf.Clamp(local.x,-safeArea.rect.width*.5f+220,safeArea.rect.width*.5f-220);
                local.y=Mathf.Clamp(local.y,265,safeArea.rect.height-135);
                enemyText.rectTransform.anchoredPosition=local;
                enemyFill.rectTransform.anchoredPosition=local+new Vector2(0,-23);
            }
            else { enemyRoot.gameObject.SetActive(false); enemyHalo.gameObject.SetActive(false); enemyText.text = ""; enemyFill.fillAmount = 0; }
            RenderNotes(time,phase);
            pauseButton.gameObject.SetActive(phase == GamePhase.Playing || phase == GamePhase.Preparing);
        }
        void RenderNotes(double time,GamePhase phase)
        {
            int used = 0; bool active = phase == GamePhase.Playing || phase == GamePhase.Preparing;
            if (active)
            {
                var run = game.Run; float width = rail.rect.width * .48f;
                for (int i = Math.Max(0,run.NextIndex); i < run.Chart.notes.Length && used < noteLeft.Length; i++)
                {
                    var n = run.Chart.notes[i]; double delta = n.time-time;
                    if (delta > 1.6) break;
                    if (run.Decisions[i] != NoteState.Pending || delta < -run.WindowAt(i)) continue;
                    float x = width * (float)(delta/1.6);
                    noteLeft[used].gameObject.SetActive(true); noteRight[used].gameObject.SetActive(true);
                    noteLeft[used].anchoredPosition = new Vector2(-x,0); noteRight[used].anchoredPosition = new Vector2(x,0);
                    Color c = n.role == "attack" ? new Color(.96f,.53f,.36f) : Bone;
                    c.a = Mathf.Clamp01((1.6f-(float)delta)*3);
                    noteLeftImages[used].color = c; noteRightImages[used].color = c;
                    used++;
                }
                if (used == 0 && time >= game.Stage.MainTime && time < game.Stage.doorTime && Time.unscaledTime >= reactionUntil) statusText.text = "REST";
            }
            for (int i = used; i < noteLeft.Length; i++) { noteLeft[i].gameObject.SetActive(false); noteRight[i].gameObject.SetActive(false); }
        }
        public void ShowCount(int value) { countText.text = value == 0 ? "LISTEN" : value.ToString(); }
        void ShowOverlay(string heading,string sub,string text,string button,string other,bool extras)
        {
            title.text = heading; subtitle.text = sub; body.text = text; primaryText.text = button; secondaryText.text = other;
            secondaryButton.SetActive(other.Length > 0); menuExtras.SetActive(extras);
            overlay.gameObject.SetActive(true); overlay.blocksRaycasts = true; overlay.interactable = true;
            primary.Select();
            panelTween?.Kill(); overlay.alpha = 0;
            panelTween = overlay.DOFade(1,.2f).SetUpdate(true).SetLink(overlay.gameObject);
        }
        public void HideOverlay() { panelTween?.Kill(); overlay.gameObject.SetActive(false); }
        public void RefreshMenu() { if (game.Phase == GamePhase.Menu) ShowMenu(); }
        public void ShowMenu()
        {
            string d = game.difficulty == "easy" ? "입문" : game.difficulty == "normal" ? "보통" : "도전";
            ShowOverlay("잔향", "AFTERECHO   /   THE BLACK CORRIDOR", "박자가 모이면, 한 걸음.\n앞에 적이 있다면, 그 한 번이 공격이 됩니다.\n\n양쪽에서 오는 한 쌍의 표시는 한 번만 누릅니다.\n화면 탭 · SPACE · F · J", $"{d} 시작하기", "", true);
            for (int i = 0; i < difficulties.Length; i++) difficulties[i].GetComponent<UnityEngine.UI.Image>().color = i == (game.difficulty == "easy" ? 0 : game.difficulty == "normal" ? 1 : 2) ? new Color(.22f,.4f,.38f) : new Color(.09f,.14f,.15f);
        }
        public void ShowReady() => ShowOverlay("준비됐나요?", "PRACTICE COMPLETE", "노트를 놓치면 콤보만 끊깁니다.\n예고된 적을 시간 안에 처치하지 못하면 체력이 줄어듭니다.\n\n4박 준비 후, 노래가 처음부터 시작됩니다.\n연습 기록은 초기화됩니다.", "곡 처음부터 실전 시작", "처음으로", false);
        public void ShowAudioProblem(string message) => ShowOverlay("음악을 다시 연결할게요", "AUDIO PAUSED", message, "4박 후 계속", "처음으로", false);
        public void ShowPause() => ShowOverlay("잠시, 숨을 고르세요", "PAUSED", "음악과 노트, 적의 시간이 멈췄습니다.\n계속하기를 누르면 4박 후 이어집니다.\n\n음량과 싱크는 아래에서 조절할 수 있습니다.", "4박 후 계속", "처음으로", false);
        public void ShowResult(bool won)
        {
            var r = game.Run;
            ShowOverlay(won ? "문 너머의 잔향" : "어둠에 멈춘 걸음", won ? "STAGE 01 · CLEAR" : "STAGE 01 · RETRY", $"{(won ? "철문 너머에도 어두운 방이 이어집니다." : "다시 리듬을 찾을 수 있습니다.")}\n\n점수  {r.Score:N0}    최고 콤보  {r.Best}\n노트 미스  {r.Misses}    여분 입력  {r.Extras}\n처치  {r.Kills}/6    받은 피해  {r.Damage}", "다시 시작", "처음으로", false);
        }
    }
}
