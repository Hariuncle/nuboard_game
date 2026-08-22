using UnityEngine;
using UnityEngine.UI;

namespace BlossomBreach
{
    /// <summary>
    /// Asset-free light-gun feedback: reticle bloom, restrained screen/camera impulse,
    /// and short synthesized tones. It never reads input, so the HID gun remains an
    /// ordinary pointer plus primary button.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GunConsoleFeedback : MonoBehaviour
    {
        private static readonly Color Cream = new Color32(255, 248, 208, 255);
        private static readonly Color Mint = new Color32(111, 255, 192, 255);
        private static readonly Color Rose = new Color32(255, 91, 125, 255);
        private static readonly Color Amber = new Color32(255, 174, 55, 255);
        private static readonly Color Violet = new Color32(192, 113, 255, 255);
        private static readonly Color Cyan = new Color32(84, 226, 255, 255);

        [Header("Impulse")]
        [SerializeField, Range(0f, 1f)] private float cameraImpulse = 0.48f;
        [SerializeField, Range(0f, 1f)] private float screenFlash = 0.65f;

        [Header("Audio")]
        [SerializeField, Range(0f, 1f)] private float shotVolume = 0.34f;
        [SerializeField, Range(0f, 1f)] private float impactVolume = 0.26f;

        private readonly Image[] _bloomMarks = new Image[4];
        private BlossomGame _game;
        private Camera _camera;
        private AudioSource _shotSource;
        private AudioSource _impactSource;
        private RectTransform _bloomRoot;
        private CanvasGroup _bloomGroup;
        private Image _impactDot;
        private Image _screenFlashImage;
        private Vector3 _cameraBasePosition;
        private Quaternion _cameraBaseRotation;
        private Color _feedbackColor = Cream;
        private float _bloomAlpha;
        private float _bloomScale = 1f;
        private float _impactAlpha;
        private float _flashAlpha;
        private float _recoil;
        private float _sideImpulse;
        private int _shotParity;
        private bool _subscribed;

        private AudioClip _shotClip;
        private AudioClip _powerClip;
        private AudioClip _hitClip;
        private AudioClip _criticalClip;
        private AudioClip _blockedClip;
        private AudioClip _missClip;
        private AudioClip _defeatClip;

        public void Configure(BlossomGame game, Camera gameplayCamera)
        {
            Unsubscribe();
            ResetCamera();
            _game = game;
            _camera = gameplayCamera != null ? gameplayCamera : Camera.main;
            CaptureCameraBase();
            Subscribe();
        }

        private void Awake()
        {
            BuildOverlay();
            BuildAudio();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetCamera();
        }

        private void Update()
        {
            float delta = Time.unscaledDeltaTime;
            UpdateOverlay(delta);
            UpdateCamera(delta);
        }

        private void OnShotResolved(BlossomGame.ShotSignal signal)
        {
            Color color;
            float strength;
            AudioClip impactClip;

            switch (signal.Outcome)
            {
                case BlossomGame.ShotOutcome.Miss:
                    color = Rose;
                    strength = 0.54f;
                    impactClip = _missClip;
                    break;
                case BlossomGame.ShotOutcome.Blocked:
                    color = Amber;
                    strength = 0.72f;
                    impactClip = _blockedClip;
                    break;
                case BlossomGame.ShotOutcome.BossCoreBlocked:
                    color = Violet;
                    strength = 0.88f;
                    impactClip = _blockedClip;
                    break;
                case BlossomGame.ShotOutcome.ShieldBreak:
                    color = Amber;
                    strength = 1.12f;
                    impactClip = _criticalClip;
                    break;
                case BlossomGame.ShotOutcome.Critical:
                    color = signal.HasEnemy && signal.EnemyKind == EnemyKind.Boss ? Rose : Mint;
                    strength = 1.02f;
                    impactClip = _criticalClip;
                    break;
                case BlossomGame.ShotOutcome.Defeat:
                    color = signal.HasEnemy && signal.EnemyKind == EnemyKind.Boss ? Violet : Rose;
                    strength = 1.20f;
                    impactClip = _defeatClip;
                    break;
                default:
                    color = signal.HasEnemy && signal.EnemyKind == EnemyKind.Armored ? Amber : Cream;
                    strength = 0.78f;
                    impactClip = _hitClip;
                    break;
            }

            if (signal.PowerShot)
            {
                color = Color.Lerp(color, Cyan, 0.72f);
                strength += 0.32f;
            }

            _feedbackColor = color;
            _bloomAlpha = Mathf.Max(_bloomAlpha, Mathf.Clamp01(0.62f + strength * 0.24f));
            _bloomScale = Mathf.Max(_bloomScale, 1.18f + strength * 0.48f);
            _impactAlpha = Mathf.Max(_impactAlpha, Mathf.Clamp01(0.48f + strength * 0.28f));
            _flashAlpha = Mathf.Max(_flashAlpha, screenFlash * (0.018f + strength * 0.035f));
            _recoil = Mathf.Min(1.8f, _recoil + cameraImpulse * (0.42f + strength * 0.30f));
            _shotParity++;
            _sideImpulse += (_shotParity & 1) == 0 ? -0.18f * strength : 0.18f * strength;

            if (_shotSource != null)
            {
                _shotSource.pitch = signal.PowerShot ? 0.86f : 1f;
                _shotSource.PlayOneShot(signal.PowerShot ? _powerClip : _shotClip, shotVolume);
            }

            if (_impactSource != null && impactClip != null)
            {
                _impactSource.pitch = signal.Outcome == BlossomGame.ShotOutcome.BossCoreBlocked ? 0.78f : 1f;
                _impactSource.PlayOneShot(impactClip, impactVolume);
            }
        }

        private void UpdateOverlay(float delta)
        {
            if (_bloomRoot == null)
            {
                return;
            }

            Vector2 aim = _game != null ? _game.AimViewport : new Vector2(0.5f, 0.5f);
            _bloomRoot.anchorMin = aim;
            _bloomRoot.anchorMax = aim;
            _bloomRoot.anchoredPosition = Vector2.zero;

            _bloomAlpha = Mathf.MoveTowards(_bloomAlpha, 0f, delta * 5.8f);
            _impactAlpha = Mathf.MoveTowards(_impactAlpha, 0f, delta * 7.5f);
            _flashAlpha = Mathf.MoveTowards(_flashAlpha, 0f, delta * 2.8f);
            _bloomScale = Mathf.MoveTowards(_bloomScale, 1f, delta * 5.2f);

            _bloomRoot.localScale = Vector3.one * _bloomScale;
            _bloomGroup.alpha = _bloomAlpha;
            _impactDot.color = WithAlpha(_feedbackColor, _impactAlpha);
            _screenFlashImage.color = WithAlpha(_feedbackColor, _flashAlpha);

            for (int i = 0; i < _bloomMarks.Length; i++)
            {
                _bloomMarks[i].color = WithAlpha(_feedbackColor, 0.92f);
            }
        }

        private void UpdateCamera(float delta)
        {
            if (_camera == null)
            {
                return;
            }

            _recoil = Mathf.MoveTowards(_recoil, 0f, delta * 7.8f);
            _sideImpulse = Mathf.MoveTowards(_sideImpulse, 0f, delta * 5.6f);
            Transform cameraTransform = _camera.transform;
            cameraTransform.localPosition = _cameraBasePosition +
                new Vector3(_sideImpulse * 0.012f, -_recoil * 0.018f, -_recoil * 0.045f);
            cameraTransform.localRotation = _cameraBaseRotation *
                Quaternion.Euler(-_recoil * 1.15f, _sideImpulse * 0.65f, _sideImpulse * 0.35f);
        }

        private void BuildOverlay()
        {
            GameObject canvasObject = new GameObject(
                "Gun Feedback Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 80;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _screenFlashImage = CreateImage("Impact Flash", canvas.transform, Color.clear);
            Stretch(_screenFlashImage.rectTransform, Vector2.zero, Vector2.one);

            GameObject bloomObject = new GameObject("Reticle Bloom", typeof(RectTransform), typeof(CanvasGroup));
            _bloomRoot = bloomObject.GetComponent<RectTransform>();
            _bloomRoot.SetParent(canvas.transform, false);
            _bloomRoot.anchorMin = _bloomRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _bloomRoot.sizeDelta = new Vector2(96f, 96f);
            _bloomGroup = bloomObject.GetComponent<CanvasGroup>();
            _bloomGroup.alpha = 0f;
            _bloomGroup.blocksRaycasts = false;
            _bloomGroup.interactable = false;

            _bloomMarks[0] = CreateMark("Bloom Top", new Vector2(0f, 31f), new Vector2(4f, 23f));
            _bloomMarks[1] = CreateMark("Bloom Bottom", new Vector2(0f, -31f), new Vector2(4f, 23f));
            _bloomMarks[2] = CreateMark("Bloom Left", new Vector2(-31f, 0f), new Vector2(23f, 4f));
            _bloomMarks[3] = CreateMark("Bloom Right", new Vector2(31f, 0f), new Vector2(23f, 4f));

            _impactDot = CreateImage("Impact Dot", _bloomRoot, Color.clear);
            RectTransform dotRect = _impactDot.rectTransform;
            dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.5f, 0.5f);
            dotRect.anchoredPosition = Vector2.zero;
            dotRect.sizeDelta = new Vector2(13f, 13f);
        }

        private Image CreateMark(string name, Vector2 position, Vector2 size)
        {
            Image mark = CreateImage(name, _bloomRoot, Cream);
            mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            mark.rectTransform.anchoredPosition = position;
            mark.rectTransform.sizeDelta = size;
            return mark;
        }

        private void BuildAudio()
        {
            _shotSource = gameObject.AddComponent<AudioSource>();
            _shotSource.playOnAwake = false;
            _shotSource.spatialBlend = 0f;
            _shotSource.dopplerLevel = 0f;

            _impactSource = gameObject.AddComponent<AudioSource>();
            _impactSource.playOnAwake = false;
            _impactSource.spatialBlend = 0f;
            _impactSource.dopplerLevel = 0f;

            _shotClip = CreateTone("Gun Pulse", 92f, 58f, 0.065f, 0.62f, 0.26f);
            _powerClip = CreateTone("Overdrive Pulse", 72f, 42f, 0.105f, 0.78f, 0.34f);
            _hitClip = CreateTone("Clean Hit", 430f, 330f, 0.050f, 0.36f, 0.02f);
            _criticalClip = CreateTone("Critical Hit", 620f, 980f, 0.085f, 0.42f, 0.01f);
            _blockedClip = CreateTone("Armor Block", 245f, 180f, 0.070f, 0.38f, 0.18f);
            _missClip = CreateTone("Dry Miss", 150f, 105f, 0.030f, 0.20f, 0.08f);
            _defeatClip = CreateTone("Enemy Cleanse", 380f, 760f, 0.120f, 0.44f, 0.02f);
        }

        private static AudioClip CreateTone(
            string name,
            float startFrequency,
            float endFrequency,
            float duration,
            float gain,
            float noiseAmount)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.Max(64, Mathf.CeilToInt(duration * sampleRate));
            float[] samples = new float[sampleCount];
            float phase = 0f;
            uint noiseState = 0x92D68CA2u;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float frequency = Mathf.Lerp(startFrequency, endFrequency, t);
                phase += Mathf.PI * 2f * frequency / sampleRate;
                noiseState = noiseState * 1664525u + 1013904223u;
                float noise = ((noiseState >> 9) / 8388607f) * 2f - 1f;
                float envelope = (1f - t) * (1f - t);
                samples[i] = (Mathf.Sin(phase) * (1f - noiseAmount) + noise * noiseAmount) *
                    envelope * gain;
            }

            AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void Subscribe()
        {
            if (_game == null || _subscribed)
            {
                return;
            }

            _game.ShotResolved += OnShotResolved;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_game != null && _subscribed)
            {
                _game.ShotResolved -= OnShotResolved;
            }

            _subscribed = false;
        }

        private void CaptureCameraBase()
        {
            if (_camera == null)
            {
                return;
            }

            _cameraBasePosition = _camera.transform.localPosition;
            _cameraBaseRotation = _camera.transform.localRotation;
        }

        private void ResetCamera()
        {
            if (_camera == null)
            {
                return;
            }

            _camera.transform.localPosition = _cameraBasePosition;
            _camera.transform.localRotation = _cameraBaseRotation;
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

        private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }
    }
}
