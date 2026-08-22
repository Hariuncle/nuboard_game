using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace BlossomBreach
{
    /// <summary>Builds the entire game HUD at runtime, so the build scene stays disposable.</summary>
    [DisallowMultipleComponent]
    public sealed class GameHud : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(20, 35, 38, 255);
        private static readonly Color Cream = new Color32(255, 251, 222, 255);
        private static readonly Color Mint = new Color32(136, 255, 202, 255);
        private static readonly Color Rose = new Color32(255, 102, 133, 255);
        private static readonly Color Glass = new Color32(10, 29, 32, 205);

        private BlossomGame game;
        private Canvas canvas;
        private Font font;
        private Text scoreText;
        private Text comboText;
        private Text purityText;
        private Text timeText;
        private Text chapterText;
        private Text overdriveText;
        private Text objectiveText;
        private Text threatText;
        private Text resultText;
        private Text bossNameText;
        private Image purityFill;
        private Image bossFill;
        private Image screenFlash;
        private RectTransform bossPanel;
        private RectTransform reticle;
        private RectTransform resultPanel;
        private Button restartButton;
        private readonly List<Image> reticleMarks = new List<Image>(5);
        private float threatUntil;
        private float shotFeedbackUntil;
        private float flashAlpha;
        private float nextRefresh;
        private int previousOverdrive = -1;
        private float previousPurity = -1f;
        private bool reticleHit;
        private bool roundWasRunning;
        private bool subscribed;

        public Canvas Canvas => canvas;

        public void Configure(BlossomGame blossomGame)
        {
            Unsubscribe();
            game = blossomGame;
            Subscribe();
            Refresh();
        }

        private void Awake()
        {
            BuildHud();
        }

        private void OnEnable()
        {
            if (game == null)
            {
                game = FindAnyObjectByType<BlossomGame>();
            }

            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (game != null && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.08f;
                Refresh();
            }

            if (threatText != null)
            {
                float remaining = threatUntil - Time.unscaledTime;
                threatText.gameObject.SetActive(remaining > 0f);
                if (remaining > 0f)
                {
                    float pulse = 0.78f + Mathf.Sin(Time.unscaledTime * 12f) * 0.22f;
                    threatText.color = new Color(Rose.r, Rose.g, Rose.b, pulse);
                }
            }

            if (reticle != null)
            {
                bool feedbackActive = Time.unscaledTime < shotFeedbackUntil;
                float pulse = feedbackActive
                    ? 1.22f + Mathf.Sin(Time.unscaledTime * 18f) * 0.08f
                    : 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.055f;
                reticle.localScale = Vector3.one * pulse;
                Color feedbackColor = reticleHit ? Mint : Rose;
                for (int i = 0; i < reticleMarks.Count; i++)
                {
                    reticleMarks[i].color = feedbackActive ? feedbackColor : (i == reticleMarks.Count - 1 ? Rose : Cream);
                }
            }

            if (screenFlash != null)
            {
                flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, Time.unscaledDeltaTime * 2.8f);
                screenFlash.color = new Color(Mint.r, Mint.g, Mint.b, flashAlpha);
            }
        }

        public void SetReticleViewport(Vector2 viewport)
        {
            if (reticle == null)
            {
                return;
            }

            Vector2 clamped = new Vector2(Mathf.Clamp01(viewport.x), Mathf.Clamp01(viewport.y));
            reticle.anchorMin = clamped;
            reticle.anchorMax = clamped;
            reticle.anchoredPosition = Vector2.zero;
        }

        public void ShowThreat(string message, float seconds = 1.2f)
        {
            if (threatText == null)
            {
                return;
            }

            threatText.text = string.IsNullOrWhiteSpace(message) ? "FRIEND NEEDS HELP" : message.ToUpperInvariant();
            threatUntil = Time.unscaledTime + Mathf.Max(0.1f, seconds);
            threatText.gameObject.SetActive(true);
        }

        public void NotifyShot(bool hit)
        {
            reticleHit = hit;
            shotFeedbackUntil = Time.unscaledTime + 0.16f;
            if (hit)
            {
                flashAlpha = 0.16f;
            }
        }

        public void SetBoss(string displayName, float normalizedHealth)
        {
            if (bossPanel == null)
            {
                return;
            }

            bossPanel.gameObject.SetActive(true);
            bossNameText.text = string.IsNullOrWhiteSpace(displayName) ? "CORRUPTED BLOOM" : displayName.ToUpperInvariant();
            bossFill.fillAmount = Mathf.Clamp01(normalizedHealth);
            bossFill.rectTransform.anchorMax = new Vector2(bossFill.fillAmount, 1f);
            bossFill.rectTransform.offsetMax = Vector2.zero;
        }

        public void HideBoss()
        {
            if (bossPanel != null)
            {
                bossPanel.gameObject.SetActive(false);
            }
        }

        public void SetRestartVisible(bool visible)
        {
            if (restartButton != null)
            {
                restartButton.gameObject.SetActive(visible);
            }
        }

        private void Subscribe()
        {
            if (game == null || subscribed)
            {
                return;
            }

            game.StateChanged += Refresh;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (game != null && subscribed)
            {
                game.StateChanged -= Refresh;
            }

            subscribed = false;
        }

        private void Refresh()
        {
            if (game == null || scoreText == null)
            {
                return;
            }

            float purityPercent = game.Purity <= 1.001f ? game.Purity * 100f : game.Purity;
            scoreText.text = game.Score.ToString("000000");
            comboText.text = $"CHAIN  ×{Mathf.Max(1, game.Combo)}";
            purityText.text = $"CORE INTEGRITY  {Mathf.Clamp(purityPercent, 0f, 100f):0}%";
            purityFill.fillAmount = Mathf.Clamp01(purityPercent / 100f);
            purityFill.rectTransform.anchorMax = new Vector2(purityFill.fillAmount, 1f);
            purityFill.rectTransform.offsetMax = Vector2.zero;
            timeText.text = FormatTime(game.TimeRemaining);
            chapterText.text = $"WAVE {game.Chapter:00}  //  CLOVER MEADOW";
            objectiveText.text = game.Chapter >= 3
                ? "OBJECTIVE  //  RESTORE THE CORRUPTED BLOOM"
                : "OBJECTIVE  //  RETURN SPORES TO GENTLE PETS";
            overdriveText.text = game.OverdriveShots > 0
                ? $"HEARTBURST  ×{game.OverdriveShots}"
                : "HEARTBURST  CHARGING";
            overdriveText.color = game.OverdriveShots > 0 ? Cream : new Color(Mint.r, Mint.g, Mint.b, 0.62f);
            SetReticleViewport(game.AimViewport);
            if (game.BossActive)
            {
                SetBoss("CORRUPTED BLOOM", game.BossHealthNormalized);
            }
            else
            {
                HideBoss();
            }

            if (previousOverdrive >= 0 && game.OverdriveShots > previousOverdrive)
            {
                ShowThreat("HEARTBURST READY", 1.1f);
            }

            if (previousPurity >= 0f && purityPercent < previousPurity - 5f)
            {
                ShowThreat("MEADOW NEEDS HELP", 0.8f);
            }

            previousOverdrive = game.OverdriveShots;
            previousPurity = purityPercent;

            if (game.IsRunning)
            {
                roundWasRunning = true;
                if (resultPanel != null)
                {
                    resultPanel.gameObject.SetActive(false);
                }
            }
            else if (roundWasRunning && resultPanel != null)
            {
                string resultHeading = game.Purity > 0 ? "EARTH PROTECTED" : "MEADOW NEEDS HELP";
                resultText.text = $"{resultHeading}\n\nSCORE  {game.Score:000000}\nMAX CHAIN  ×{Mathf.Max(1, game.Combo)}\nCORE  {Mathf.Clamp(purityPercent, 0f, 100f):0}%";
                resultPanel.gameObject.SetActive(true);
            }
        }

        private static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }

        private void BuildHud()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();

            GameObject canvasObject = new GameObject("Game HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform safe = CreateRect("HUD Safe Area", canvas.transform);
            Stretch(safe, new Vector2(0.025f, 0.035f), new Vector2(0.975f, 0.965f));

            screenFlash = CreateImage("Hit Flash", canvas.transform, new Color(Mint.r, Mint.g, Mint.b, 0f));
            Stretch(screenFlash.rectTransform, Vector2.zero, Vector2.one);

            RectTransform topBar = CreatePanel("Top Bar", safe, Glass);
            Anchor(topBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -86f), new Vector2(0f, 86f));

            scoreText = CreateText("Score", topBar, 34, Cream, TextAnchor.MiddleLeft, FontStyle.Bold);
            Anchor(scoreText.rectTransform, new Vector2(0f, 0f), new Vector2(0.23f, 1f), new Vector2(24f, 0f), new Vector2(-8f, 0f));

            comboText = CreateText("Combo", topBar, 27, Mint, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(comboText.rectTransform, new Vector2(0.23f, 0f), new Vector2(0.43f, 1f));

            timeText = CreateText("Time", topBar, 38, Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(timeText.rectTransform, new Vector2(0.43f, 0f), new Vector2(0.57f, 1f));

            purityText = CreateText("Purity", topBar, 25, Mint, TextAnchor.UpperCenter, FontStyle.Bold);
            Anchor(purityText.rectTransform, new Vector2(0.57f, 0.12f), new Vector2(0.79f, 0.93f));

            RectTransform purityTrack = CreatePanel("Purity Track", topBar, new Color(1f, 1f, 1f, 0.12f));
            Anchor(purityTrack, new Vector2(0.59f, 0.18f), new Vector2(0.77f, 0.32f));
            purityFill = CreateImage("Purity Fill", purityTrack, Mint);
            Stretch(purityFill.rectTransform, Vector2.zero, Vector2.one);
            purityFill.type = Image.Type.Filled;
            purityFill.fillMethod = Image.FillMethod.Horizontal;

            overdriveText = CreateText("Overdrive", topBar, 23, Mint, TextAnchor.MiddleRight, FontStyle.Bold);
            Anchor(overdriveText.rectTransform, new Vector2(0.79f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-24f, 0f));

            chapterText = CreateText("Chapter", safe, 25, Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            chapterText.color = new Color(Cream.r, Cream.g, Cream.b, 0.82f);
            Anchor(chapterText.rectTransform, new Vector2(0.27f, 1f), new Vector2(0.73f, 1f), new Vector2(0f, -127f), new Vector2(0f, 42f));

            objectiveText = CreateText("Objective", safe, 17, Mint, TextAnchor.MiddleCenter, FontStyle.Bold);
            objectiveText.color = new Color(Mint.r, Mint.g, Mint.b, 0.82f);
            Anchor(objectiveText.rectTransform, new Vector2(0.25f, 1f), new Vector2(0.75f, 1f), new Vector2(0f, -153f), new Vector2(0f, 28f));

            bossPanel = CreatePanel("Boss Threat", safe, new Color(Glass.r, Glass.g, Glass.b, 0.9f));
            Anchor(bossPanel, new Vector2(0.28f, 1f), new Vector2(0.72f, 1f), new Vector2(0f, -182f), new Vector2(0f, 54f));
            bossNameText = CreateText("Boss Name", bossPanel, 20, Rose, TextAnchor.UpperCenter, FontStyle.Bold);
            Stretch(bossNameText.rectTransform, new Vector2(0.04f, 0.3f), new Vector2(0.96f, 0.95f));
            RectTransform bossTrack = CreatePanel("Boss Track", bossPanel, new Color(1f, 1f, 1f, 0.13f));
            Anchor(bossTrack, new Vector2(0.05f, 0.16f), new Vector2(0.95f, 0.34f));
            bossFill = CreateImage("Boss Fill", bossTrack, Rose);
            Stretch(bossFill.rectTransform, Vector2.zero, Vector2.one);
            bossFill.type = Image.Type.Filled;
            bossFill.fillMethod = Image.FillMethod.Horizontal;
            bossPanel.gameObject.SetActive(false);

            threatText = CreateText("Threat Flash", safe, 44, Rose, TextAnchor.MiddleCenter, FontStyle.Bold);
            Anchor(threatText.rectTransform, new Vector2(0.25f, 0.68f), new Vector2(0.75f, 0.82f));
            threatText.gameObject.SetActive(false);

            BuildRestartButton(safe);
            BuildResultPanel(canvas.transform);
            BuildReticle(canvas.transform);
        }

        private void BuildReticle(Transform parent)
        {
            reticle = CreateRect("Aim Reticle", parent);
            reticle.sizeDelta = new Vector2(72f, 72f);
            SetReticleViewport(new Vector2(0.5f, 0.5f));

            AddReticleBar("Top", new Vector2(0f, 22f), new Vector2(3f, 20f));
            AddReticleBar("Bottom", new Vector2(0f, -22f), new Vector2(3f, 20f));
            AddReticleBar("Left", new Vector2(-22f, 0f), new Vector2(20f, 3f));
            AddReticleBar("Right", new Vector2(22f, 0f), new Vector2(20f, 3f));
            AddReticleBar("Core", Vector2.zero, new Vector2(7f, 7f), Rose);
        }

        private void AddReticleBar(string name, Vector2 position, Vector2 size, Color? color = null)
        {
            Image image = CreateImage(name, reticle, color ?? Cream);
            reticleMarks.Add(image);
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = size;
        }

        private void BuildRestartButton(Transform parent)
        {
            RectTransform buttonRect = CreatePanel("Restart", parent, new Color(Glass.r, Glass.g, Glass.b, 0.94f));
            Anchor(buttonRect, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-116f, 48f), new Vector2(232f, 72f));
            Image background = buttonRect.GetComponent<Image>();
            background.raycastTarget = true;
            restartButton = buttonRect.gameObject.AddComponent<Button>();
            restartButton.targetGraphic = background;
            restartButton.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = Mint,
                pressedColor = Rose,
                selectedColor = Mint,
                disabledColor = new Color(1f, 1f, 1f, 0.35f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            restartButton.onClick.AddListener(() => game?.Restart());

            Text label = CreateText("Label", buttonRect, 20, Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(label.rectTransform, Vector2.zero, Vector2.one);
            label.text = "RESTART";
        }

        private void BuildResultPanel(Transform parent)
        {
            resultPanel = CreatePanel("Mission Result", parent, new Color(0.025f, 0.08f, 0.08f, 0.94f));
            Stretch(resultPanel, new Vector2(0.29f, 0.22f), new Vector2(0.71f, 0.78f));

            Text heading = CreateText("Heading", resultPanel, 34, Mint, TextAnchor.MiddleCenter, FontStyle.Bold);
            heading.text = "RESCUE REPORT";
            Anchor(heading.rectTransform, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.96f));

            resultText = CreateText("Result", resultPanel, 27, Cream, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(resultText.rectTransform, new Vector2(0.08f, 0.22f), new Vector2(0.92f, 0.78f));

            RectTransform buttonRect = CreatePanel("Restart Mission", resultPanel, Mint);
            Anchor(buttonRect, new Vector2(0.5f, 0.09f), new Vector2(0.5f, 0.09f), Vector2.zero, new Vector2(360f, 82f));
            Image background = buttonRect.GetComponent<Image>();
            background.raycastTarget = true;
            Button button = buttonRect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                roundWasRunning = false;
                resultPanel.gameObject.SetActive(false);
                game?.Restart();
            });
            Text label = CreateText("Label", buttonRect, 20, Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.text = "HELP AGAIN";
            Stretch(label.rectTransform, Vector2.zero, Vector2.one);
            resultPanel.gameObject.SetActive(false);
        }

        private void EnsureEventSystem()
        {
            InputSystemUIInputModule inputModule;
            if (EventSystem.current == null)
            {
                var eventSystemObject = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystemObject.transform.SetParent(transform, false);
                inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            }
            else
            {
                inputModule = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            }

            if (inputModule != null)
            {
                inputModule.move = null;
                inputModule.submit = null;
                inputModule.cancel = null;
                inputModule.scrollWheel = null;
                inputModule.middleClick = null;
                inputModule.rightClick = null;
                inputModule.trackedDevicePosition = null;
                inputModule.trackedDeviceOrientation = null;
            }
        }

        private RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            Image image = CreateImage(name, parent, color);
            return image.rectTransform;
        }

        private Image CreateImage(string name, Transform parent, Color color)
        {
            RectTransform rect = CreateRect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(string name, Transform parent, int size, Color color, TextAnchor alignment, FontStyle style)
        {
            RectTransform rect = CreateRect(name, parent);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Anchor(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position = default, Vector2 size = default)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
