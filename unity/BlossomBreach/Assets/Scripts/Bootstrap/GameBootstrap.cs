using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.Video;

namespace BlossomBreach
{
    /// <summary>Creates the playable scene from an intentionally empty build scene.</summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class GameBootstrap : MonoBehaviour
    {
        private const float IntroSkipGraceSeconds = 1.5f;

        [Header("Optional opening movie")]
        [SerializeField] private bool playIntro = true;
        [SerializeField] private string introFileName = "h3-meadow-intro.mp4";
        [SerializeField, Min(1f)] private float introPrepareTimeout = 8f;

        private bool skipIntro;
        private bool introFailed;
        private bool gateAccepted;
        private static Font uiFont;
        private static Sprite targetCircleSprite;

        private IEnumerator Start()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 1;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;

            if (playIntro)
            {
                yield return PlayIntroIfAvailable();
            }

            PrepareWorld();
            yield return ShowGate(
                "블로섬 브리치",
                "건슈팅 정원 방어전\n\n조준기로 겨누고  //  방아쇠로 정화하세요",
                "초원으로 진입");
            yield return ShowGate(
                "제1장  //  클로버 들판",
                "임무\n다가오는 악몽 포자를 정화하세요.\n연속 정화를 이어가며 초원의 순수도를 지키세요.",
                "임무 시작");
            CreateGameplay();
        }

        private static void PrepareWorld()
        {
            Camera camera = EnsureCamera();
            EnsureLighting();
            MeadowEnvironment.Build();
            camera.transform.LookAt(new Vector3(0f, 2.6f, 18f));
        }

        private IEnumerator PlayIntroIfAvailable()
        {
            string videoPath = Path.Combine(Application.streamingAssetsPath, introFileName);
            if (!File.Exists(videoPath))
            {
                yield break;
            }

            EnsureEventSystem();
            GameObject introRoot = new GameObject("Meadow Intro", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Stretch(introRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            Canvas introCanvas = introRoot.GetComponent<Canvas>();
            introCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            introCanvas.sortingOrder = 1000;
            CanvasScaler scaler = introRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RawImage screen = CreateFullscreenRawImage(introRoot.transform);

            VideoPlayer player = introRoot.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.isLooping = true;
            player.source = VideoSource.Url;
            player.url = videoPath;
            player.renderMode = VideoRenderMode.APIOnly;
            player.sendFrameReadyEvents = true;
            player.audioOutputMode = VideoAudioOutputMode.AudioSource;
            player.controlledAudioTrackCount = 1;

            AudioSource introAudio = introRoot.AddComponent<AudioSource>();
            introAudio.playOnAwake = false;
            introAudio.loop = false;
            introAudio.spatialBlend = 0f;
            introAudio.volume = 1f;

            player.errorReceived += (_, message) =>
            {
                introFailed = true;
                Debug.LogWarning($"인트로 영상을 재생하지 못했습니다. 게임을 바로 시작합니다: {message}");
            };

            long frameReadyCount = 0;
            long lastFrameIndex = -1;
            bool firstFrameReceivedLogged = false;
            bool firstFramePresentedLogged = false;
            bool blitFailureLogged = false;
            RenderTexture displayTarget = null;
            VideoPlayer.FrameReadyEventHandler frameReadyHandler = (source, frameIndex) =>
            {
                frameReadyCount++;
                lastFrameIndex = frameIndex;
                if (!firstFrameReceivedLogged)
                {
                    firstFrameReceivedLogged = true;
                    Debug.Log(
                        $"인트로 첫 프레임 수신: {frameIndex}, " +
                        $"영상 {source.width}x{source.height}, source={DescribeTexture(source.texture)}.");
                }
            };
            player.frameReady += frameReadyHandler;

            RectTransform skipTarget = BuildIntroSkipPrompt(introRoot.transform);
            RectTransform menuReticle = BuildMenuReticle(introRoot.transform);
            screen.rectTransform.SetAsFirstSibling();
            skipTarget.SetAsLastSibling();
            Canvas.ForceUpdateCanvases();
            Debug.Log(
                $"인트로 UI 계층: video={screen.rectTransform.GetSiblingIndex()}, " +
                $"reticle={menuReticle.GetSiblingIndex()}, skip={skipTarget.GetSiblingIndex()}, " +
                $"canvas={introRoot.GetComponent<RectTransform>().rect.size}, screen={screen.rectTransform.rect.size}.");
            skipIntro = false;
            introFailed = false;
            player.Prepare();

            float timeoutAt = Time.realtimeSinceStartup + introPrepareTimeout;
            while (!skipIntro && !introFailed && !player.isPrepared && Time.realtimeSinceStartup < timeoutAt)
            {
                UpdateMenuReticle(menuReticle);
                yield return null;
            }

            if (!skipIntro && !introFailed && player.isPrepared)
            {
                Debug.Log("인트로 영상 준비 완료.");
                if (player.audioTrackCount > 0)
                {
                    player.EnableAudioTrack(0, true);
                    player.SetTargetAudioSource(0, introAudio);
                }

                int videoWidth = player.width > 0 ? Mathf.Clamp((int)player.width, 16, 4096) : 1920;
                int videoHeight = player.height > 0 ? Mathf.Clamp((int)player.height, 16, 4096) : 1080;
                displayTarget = new RenderTexture(
                    videoWidth,
                    videoHeight,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.Default)
                {
                    name = "인트로 표시 버퍼",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                if (!displayTarget.Create())
                {
                    Debug.LogWarning("인트로 표시 버퍼를 만들지 못했습니다. 영상 텍스처를 직접 표시합니다.");
                    Destroy(displayTarget);
                    displayTarget = null;
                }
                else
                {
                    screen.texture = displayTarget;
                }

                player.Play();
                Debug.Log(
                    $"인트로 영상 재생 시작: player={player.width}x{player.height}, " +
                    $"display={DescribeTexture(displayTarget)}, screen={DescribeTexture(screen.texture)}.");
                float playbackStartedAt = Time.realtimeSinceStartup;
                var skipGuard = new IntroSkipGuard(IntroSkipGraceSeconds);
                while (!skipIntro && !introFailed)
                {
                    UpdateMenuReticle(menuReticle);
                    if (player.frame >= 0 && player.texture != null)
                    {
                        bool presented = TryPresentIntroFrame(
                            player.texture,
                            screen,
                            displayTarget,
                            ref blitFailureLogged,
                            out string presentationPath);
                        if (presented && !firstFramePresentedLogged)
                        {
                            firstFramePresentedLogged = true;
                            lastFrameIndex = player.frame;
                            Debug.Log(
                                $"인트로 첫 프레임 표시 확인: frame={player.frame}, path={presentationPath}, " +
                                $"source={DescribeTexture(player.texture)}, screen={DescribeTexture(screen.texture)}, " +
                                $"alpha={screen.canvasRenderer.GetAlpha():0.00}, uv={screen.uvRect}.");
                        }
                    }

                    bool triggerPressed = TryGetIntroSkipPress(out Vector2 pressPosition);
                    bool hasPointerPosition = triggerPressed || TryGetIntroPointerPosition(out pressPosition);
                    bool pointerInsideTarget = hasPointerPosition &&
                                               RectTransformUtility.RectangleContainsScreenPoint(
                                                   skipTarget,
                                                   pressPosition,
                                                   null);
                    if (skipGuard.TryAccept(
                            Time.realtimeSinceStartup - playbackStartedAt,
                            IsIntroSkipHeld(),
                            triggerPressed,
                            pointerInsideTarget))
                    {
                        skipIntro = true;
                        Debug.Log(
                            $"인트로 건너뛰기: 표적 밖을 조준한 뒤 우측 하단 버튼 입력. " +
                            $"frameReady {frameReadyCount}회, 마지막 프레임 {lastFrameIndex}.");
                    }
                    yield return null;
                }
            }
            else if (!skipIntro && !introFailed)
            {
                introFailed = true;
                Debug.LogWarning("인트로 영상 준비 시간이 초과되어 게임을 바로 시작합니다.");
            }

            player.Stop();
            introAudio.Stop();
            player.frameReady -= frameReadyHandler;
            screen.texture = Texture2D.blackTexture;
            if (displayTarget != null)
            {
                displayTarget.Release();
                Destroy(displayTarget);
            }
            Debug.Log(
                $"인트로 종료: frameReady {frameReadyCount}회, 마지막 프레임 {lastFrameIndex}, " +
                $"표시 성공={firstFramePresentedLogged}.");
            Destroy(introRoot);
        }

        private void CreateGameplay()
        {
            BlossomGame game = FindAnyObjectByType<BlossomGame>();
            if (game == null)
            {
                game = new GameObject("Blossom Game").AddComponent<BlossomGame>();
            }

            GameHud hud = FindAnyObjectByType<GameHud>();
            if (hud == null)
            {
                hud = new GameObject("Game HUD Controller").AddComponent<GameHud>();
            }
            hud.Configure(game);

            GunMouseInput input = FindAnyObjectByType<GunMouseInput>();
            if (input == null)
            {
                input = new GameObject("Gun Mouse Input").AddComponent<GunMouseInput>();
            }
            input.Configure(game, hud);

            GunConsoleFeedback feedback = FindAnyObjectByType<GunConsoleFeedback>();
            if (feedback == null)
            {
                feedback = new GameObject("Gun Console Feedback").AddComponent<GunConsoleFeedback>();
            }
            feedback.Configure(game, Camera.main);
        }

        private IEnumerator ShowGate(string heading, string body, string buttonLabel)
        {
            EnsureEventSystem();
            gateAccepted = false;

            GameObject gate = new GameObject("Flow Gate", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Stretch(gate.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            Canvas gateCanvas = gate.GetComponent<Canvas>();
            gateCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gateCanvas.sortingOrder = 900;
            CanvasScaler scaler = gate.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Font font = GetUiFont();
            Image veil = CreateImage("Veil", gate.transform, new Color(0.015f, 0.055f, 0.055f, 0.78f));
            Stretch(veil.rectTransform, Vector2.zero, Vector2.one);

            Image card = CreateImage("Card", gate.transform, new Color(0.035f, 0.13f, 0.12f, 0.94f));
            Stretch(card.rectTransform, new Vector2(0.22f, 0.22f), new Vector2(0.78f, 0.78f));

            Text title = CreateText("Heading", card.transform, font, 51, new Color32(255, 251, 222, 255), FontStyle.Bold);
            Stretch(title.rectTransform, new Vector2(0.07f, 0.67f), new Vector2(0.93f, 0.91f));
            title.text = heading;

            Text copy = CreateText("Copy", card.transform, font, 24, new Color32(136, 255, 202, 255), FontStyle.Normal);
            Stretch(copy.rectTransform, new Vector2(0.09f, 0.28f), new Vector2(0.91f, 0.67f));
            copy.text = body;

            GameObject buttonObject = new GameObject("Continue", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(card.transform, false);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.5f, 0.15f);
            buttonRect.sizeDelta = new Vector2(340f, 70f);
            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color32(136, 255, 202, 255);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(() => gateAccepted = true);
            Text buttonText = CreateText("Label", buttonRect, font, 21, new Color32(20, 35, 38, 255), FontStyle.Bold);
            Stretch(buttonText.rectTransform, Vector2.zero, Vector2.one);
            buttonText.text = buttonLabel;

            RectTransform menuReticle = BuildMenuReticle(gate.transform);

            while (!gateAccepted)
            {
                UpdateMenuReticle(menuReticle);
                yield return null;
            }

            Destroy(gate);
            yield return null;
        }

        private static Camera EnsureCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cameraObject.tag = "MainCamera";
                camera = cameraObject.GetComponent<Camera>();
            }

            camera.transform.SetPositionAndRotation(new Vector3(0f, 4.8f, -11.5f), Quaternion.Euler(8f, 0f, 0f));
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 0.08f;
            camera.farClipPlane = 180f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.allowHDR = false;
            camera.allowMSAA = true;

            UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }
            cameraData.antialiasing = AntialiasingMode.None;
            cameraData.renderPostProcessing = false;
            return camera;
        }

        private static void EnsureLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.72f, 0.77f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.48f, 0.39f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.18f, 0.13f);
            RenderSettings.reflectionIntensity = 0.65f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.68f, 0.82f, 0.76f);
            RenderSettings.fogDensity = 0.0065f;

            Light sun = CreateLight("Meadow Sun", LightType.Directional, new Color(1f, 0.91f, 0.70f), 1.45f);
            sun.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.72f;

            Light fill = CreateLight("Sky Fill", LightType.Directional, new Color(0.50f, 0.72f, 1f), 0.38f);
            fill.transform.rotation = Quaternion.Euler(128f, 35f, 175f);
            fill.shadows = LightShadows.None;
        }

        private static Light CreateLight(string name, LightType type, Color color, float intensity)
        {
            GameObject lightObject = new GameObject(name, typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            return light;
        }

        private RectTransform BuildIntroSkipPrompt(Transform parent)
        {
            Font font = GetUiFont();
            GameObject buttonObject = new GameObject("인트로 건너뛰기", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-42f, 42f);
            rect.sizeDelta = new Vector2(520f, 190f);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.03f, 0.10f, 0.11f, 0.88f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            Sprite circle = GetTargetCircleSprite();
            Image outerRing = CreateImage("표적 외곽", rect, new Color32(255, 102, 133, 255));
            outerRing.sprite = circle;
            outerRing.preserveAspect = true;
            outerRing.rectTransform.anchorMin = outerRing.rectTransform.anchorMax = new Vector2(0.17f, 0.5f);
            outerRing.rectTransform.anchoredPosition = Vector2.zero;
            outerRing.rectTransform.sizeDelta = new Vector2(124f, 124f);

            Image innerRing = CreateImage("표적 내부", outerRing.transform, new Color32(10, 29, 32, 255));
            innerRing.sprite = circle;
            innerRing.preserveAspect = true;
            innerRing.rectTransform.anchorMin = innerRing.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            innerRing.rectTransform.anchoredPosition = Vector2.zero;
            innerRing.rectTransform.sizeDelta = new Vector2(82f, 82f);

            Image centerDot = CreateImage("표적 중심", innerRing.transform, new Color32(136, 255, 202, 255));
            centerDot.sprite = circle;
            centerDot.preserveAspect = true;
            centerDot.rectTransform.anchorMin = centerDot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            centerDot.rectTransform.anchoredPosition = Vector2.zero;
            centerDot.rectTransform.sizeDelta = new Vector2(28f, 28f);

            Text targetLabel = CreateText("표적 안내", rect, font, 29, new Color32(255, 251, 222, 255), FontStyle.Bold);
            Stretch(targetLabel.rectTransform, new Vector2(0.32f, 0.46f), new Vector2(0.98f, 0.91f));
            targetLabel.text = "여기를 쏘세요";

            Text actionLabel = CreateText("동작 안내", rect, font, 18, new Color32(136, 255, 202, 255), FontStyle.Bold);
            Stretch(actionLabel.rectTransform, new Vector2(0.32f, 0.12f), new Vector2(0.98f, 0.50f));
            actionLabel.text = "인트로 건너뛰기  /  게임 시작";
            return rect;
        }

        private static bool TryGetIntroSkipPress(out Vector2 screenPosition)
        {
            screenPosition = default;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null &&
                Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            return false;
        }

        private static bool TryGetIntroPointerPosition(out Vector2 screenPosition)
        {
            if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            screenPosition = default;
            return false;
        }

        private static bool IsIntroSkipHeld()
        {
            bool mouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
            bool touchHeld = Touchscreen.current != null &&
                             Touchscreen.current.primaryTouch.press.isPressed;
            return mouseHeld || touchHeld;
        }

        private static bool TryPresentIntroFrame(
            Texture source,
            RawImage screen,
            RenderTexture displayTarget,
            ref bool blitFailureLogged,
            out string presentationPath)
        {
            if (displayTarget != null && displayTarget.IsCreated())
            {
                try
                {
                    Graphics.Blit(source, displayTarget);
                    screen.texture = displayTarget;
                    screen.color = Color.white;
                    screen.canvasRenderer.SetAlpha(1f);
                    presentationPath = "Graphics.Blit";
                    return true;
                }
                catch (System.Exception exception)
                {
                    if (!blitFailureLogged)
                    {
                        blitFailureLogged = true;
                        Debug.LogWarning($"인트로 프레임 복사 실패, 직접 표시로 전환합니다: {exception.Message}");
                    }
                }
            }

            screen.texture = source;
            screen.color = Color.white;
            screen.canvasRenderer.SetAlpha(1f);
            presentationPath = "직접 텍스처";
            return screen.texture != null;
        }

        private static string DescribeTexture(Texture texture)
        {
            return texture == null
                ? "null"
                : $"{texture.name}({texture.width}x{texture.height})";
        }

        private static Font GetUiFont()
        {
            if (uiFont == null)
            {
                uiFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Malgun Gothic", "맑은 고딕", "Arial" },
                    32);
                if (uiFont == null)
                {
                    uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
            }

            return uiFont;
        }

        private static Sprite GetTargetCircleSprite()
        {
            if (targetCircleSprite != null)
            {
                return targetCircleSprite;
            }

            const int size = 64;
            float center = (size - 1) * 0.5f;
            float radius = center - 0.5f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(radius + 1f - distance);
                    pixels[y * size + x] = new Color32(
                        255,
                        255,
                        255,
                        (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "인트로 원형 표적 텍스처",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            targetCircleSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                size);
            targetCircleSprite.name = "인트로 원형 표적";
            targetCircleSprite.hideFlags = HideFlags.HideAndDontSave;
            return targetCircleSprite;
        }

        private static RawImage CreateFullscreenRawImage(Transform parent)
        {
            GameObject screenObject = new GameObject("Video", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            RectTransform rect = screenObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            RawImage image = screenObject.GetComponent<RawImage>();
            image.color = Color.white;
            image.texture = Texture2D.blackTexture;
            image.material = null;
            image.uvRect = new Rect(0f, 0f, 1f, 1f);
            image.raycastTarget = false;
            image.canvasRenderer.SetAlpha(1f);
            image.rectTransform.SetAsFirstSibling();
            AspectRatioFitter fitter = screenObject.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 16f / 9f;
            return image;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static Text CreateText(string name, Transform parent, Font font, int size, Color color, FontStyle style)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform BuildMenuReticle(Transform parent)
        {
            GameObject root = new GameObject("Menu Aim Reticle", typeof(RectTransform));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(78f, 78f);
            root.transform.SetAsLastSibling();

            AddMenuReticleMark(rect, new Vector2(0f, 24f), new Vector2(4f, 22f));
            AddMenuReticleMark(rect, new Vector2(0f, -24f), new Vector2(4f, 22f));
            AddMenuReticleMark(rect, new Vector2(-24f, 0f), new Vector2(22f, 4f));
            AddMenuReticleMark(rect, new Vector2(24f, 0f), new Vector2(22f, 4f));
            AddMenuReticleMark(rect, Vector2.zero, new Vector2(8f, 8f), new Color32(255, 102, 133, 255));
            return rect;
        }

        private static void AddMenuReticleMark(RectTransform parent, Vector2 position, Vector2 size, Color? color = null)
        {
            Image mark = CreateImage("Mark", parent, color ?? new Color32(255, 251, 222, 255));
            mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            mark.rectTransform.anchoredPosition = position;
            mark.rectTransform.sizeDelta = size;
        }

        private static void UpdateMenuReticle(RectTransform reticle)
        {
            if (reticle == null)
            {
                return;
            }

            Vector2 screenPosition = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                screenPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            }
            else if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();
            }

            Vector2 viewport = GunMouseInput.ScreenToViewport(screenPosition, Screen.width, Screen.height);
            reticle.anchorMin = viewport;
            reticle.anchorMax = viewport;
            reticle.anchoredPosition = Vector2.zero;
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 7f) * 0.055f;
            reticle.localScale = Vector3.one * pulse;
        }

        private static void EnsureEventSystem()
        {
            InputSystemUIInputModule inputModule;
            if (EventSystem.current == null)
            {
                var eventSystemObject = new GameObject("Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
                inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            }
            else
            {
                inputModule = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            }

            if (inputModule != null)
            {
                ConfigurePointerOnly(inputModule);
            }
        }

        private static void ConfigurePointerOnly(InputSystemUIInputModule inputModule)
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
}
