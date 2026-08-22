using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace BlossomBreach
{
    /// <summary>
    /// Treats a BLE HID light-gun as an ordinary mouse. Pointer movement aims in
    /// viewport space and the primary button fires. Touch drag aims and a short,
    /// stationary tap fires. No non-pointer gameplay input is registered.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GunMouseInput : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float tapTravelPixels = 28f;
        [SerializeField, Min(0.05f)] private float tapDurationSeconds = 0.35f;

        private readonly List<RaycastResult> uiHits = new List<RaycastResult>(8);
        private BlossomGame game;
        private GameHud hud;
        private Vector2 touchStartPosition;
        private float touchStartTime;
        private bool touchStartedOverUi;

        public void Configure(BlossomGame blossomGame, GameHud gameHud)
        {
            game = blossomGame;
            hud = gameHud;
        }

        private void Awake()
        {
            if (game == null)
            {
                game = FindAnyObjectByType<BlossomGame>();
            }

            if (hud == null)
            {
                hud = FindAnyObjectByType<GameHud>();
            }
        }

        private void OnEnable()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Confined;
        }

        private void OnDisable()
        {
            if (Cursor.lockState == CursorLockMode.Confined)
            {
                Cursor.lockState = CursorLockMode.None;
            }
            Cursor.visible = true;
        }

        private void Update()
        {
            if (game == null)
            {
                game = FindAnyObjectByType<BlossomGame>();
                if (game == null)
                {
                    return;
                }
            }

            if (UpdateTouch())
            {
                return;
            }

            UpdateMouse();
        }

        private bool UpdateTouch()
        {
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return false;
            }

            TouchControl touch = touchscreen.primaryTouch;
            bool touchedThisFrame = touch.press.wasPressedThisFrame;
            bool releasedThisFrame = touch.press.wasReleasedThisFrame;
            if (!touch.press.isPressed && !releasedThisFrame)
            {
                return false;
            }

            Vector2 screenPosition = touch.position.ReadValue();
            SetAim(screenPosition);

            if (touchedThisFrame)
            {
                touchStartPosition = screenPosition;
                touchStartTime = Time.unscaledTime;
                touchStartedOverUi = IsPointerOverUi(screenPosition);
            }

            if (releasedThisFrame)
            {
                float travel = Vector2.Distance(touchStartPosition, screenPosition);
                float duration = Time.unscaledTime - touchStartTime;
                if (!touchStartedOverUi && travel <= tapTravelPixels && duration <= tapDurationSeconds)
                {
                    Fire();
                }
            }

            return true;
        }

        private void UpdateMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            SetAim(screenPosition);

            if (mouse.leftButton.wasPressedThisFrame && !IsPointerOverUi(screenPosition))
            {
                Fire();
            }
        }

        private void Fire()
        {
            int scoreBeforeShot = game.Score;
            game.Fire();
            hud?.NotifyShot(game.Score > scoreBeforeShot);
        }

        private void SetAim(Vector2 screenPosition)
        {
            Vector2 viewport = ScreenToViewport(screenPosition, Screen.width, Screen.height);
            game.SetAim(viewport);
            hud?.SetReticleViewport(viewport);
        }

        private bool IsPointerOverUi(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            uiHits.Clear();
            var eventData = new PointerEventData(eventSystem) { position = screenPosition };
            eventSystem.RaycastAll(eventData, uiHits);
            return uiHits.Count > 0;
        }

        public static Vector2 ScreenToViewport(Vector2 screenPosition, float width, float height)
        {
            float safeWidth = Mathf.Max(1f, width);
            float safeHeight = Mathf.Max(1f, height);
            return new Vector2(
                Mathf.Clamp01(screenPosition.x / safeWidth),
                Mathf.Clamp01(screenPosition.y / safeHeight));
        }
    }
}
