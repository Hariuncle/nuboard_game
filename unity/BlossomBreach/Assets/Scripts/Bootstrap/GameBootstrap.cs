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
        [Header("Optional opening movie")]
        [SerializeField] private bool playIntro = true;
        [SerializeField] private string introFileName = "h3-meadow-intro.mp4";
        [SerializeField, Min(1f)] private float introPrepareTimeout = 8f;

        private bool skipIntro;
        private bool introFinished;
        private bool introFailed;
        private bool gateAccepted;

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
                "BLOSSOM BREACH",
                "A LIGHT-GUN GARDEN DEFENSE\n\nMOVE TO AIM  //  LEFT TRIGGER TO CLEANSE",
                "ENTER THE MEADOW");
            yield return ShowGate(
                "CHAPTER 01  //  CLOVER FIELD",
                "MISSION\nPurify the approaching nightmare spores.\nBuild your chain and protect the meadow's purity.",
                "BEGIN MISSION");
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
            Canvas introCanvas = introRoot.GetComponent<Canvas>();
            introCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            introCanvas.sortingOrder = 1000;
            CanvasScaler scaler = introRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RawImage screen = CreateFullscreenRawImage(introRoot.transform);
            RenderTexture target = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
            {
                name = "Meadow Intro Render Texture"
            };
            target.Create();
            screen.texture = target;

            VideoPlayer player = introRoot.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.isLooping = false;
            player.source = VideoSource.Url;
            player.url = videoPath;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = target;
            player.audioOutputMode = VideoAudioOutputMode.Direct;
            player.loopPointReached += _ => introFinished = true;
            player.errorReceived += (_, message) =>
            {
                introFailed = true;
                Debug.LogWarning($"Opening movie could not be played: {message}");
            };

            BuildSkipButton(introRoot.transform);
            RectTransform menuReticle = BuildMenuReticle(introRoot.transform);
            skipIntro = false;
            introFinished = false;
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
                player.Play();
                while (!skipIntro && !introFinished && !introFailed)
                {
                    UpdateMenuReticle(menuReticle);
                    yield return null;
                }
            }

            player.Stop();
            player.targetTexture = null;
            screen.texture = null;
            target.Release();
            Destroy(target);
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
            Canvas gateCanvas = gate.GetComponent<Canvas>();
            gateCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            gateCanvas.sortingOrder = 900;
            CanvasScaler scaler = gate.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

        private void BuildSkipButton(Transform parent)
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject buttonObject = new GameObject("Skip Intro", typeof(RectTransform), typeof(Image), typeof(Button));
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-42f, 42f);
            rect.sizeDelta = new Vector2(240f, 76f);
            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.03f, 0.10f, 0.11f, 0.88f);

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => skipIntro = true);

            GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Text label = labelObject.GetComponent<Text>();
            label.font = font;
            label.fontSize = 21;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color32(255, 251, 222, 255);
            label.raycastTarget = false;
            label.text = "SKIP  »";
        }

        private static RawImage CreateFullscreenRawImage(Transform parent)
        {
            GameObject screenObject = new GameObject("Video", typeof(RectTransform), typeof(RawImage));
            RectTransform rect = screenObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            RawImage image = screenObject.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
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
