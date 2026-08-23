using System;
using System.Collections;
using CommonEventsEngine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CommonEventsEngine.Presentation
{
    /// <summary>
    /// Turnkey uGUI + Camera implementation of IScreenTransitionHandler providing
    /// screen fades, color flashes, and camera shake out of the box.
    /// </summary>
    public sealed class BuiltInScreenFader : MonoBehaviour, IScreenTransitionHandler
    {
        [Header("Optional Custom References (Auto-built if left empty)")]
        [SerializeField] private Image overlayImage;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private int canvasSortOrder = 300;

        private Coroutine _fadeRoutine;
        private Coroutine _flashRoutine;
        private Coroutine _shakeRoutine;
        private Vector3 _originalCamLocalPos;
        private bool _hasSavedCamPos;

        private void Awake()
        {
            EnsureOverlayBuilt();
        }

        public void EnsureOverlayBuilt()
        {
            if (overlayImage != null) return;

            var canvasGo = new GameObject("CommonEvents_ScreenFadeCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortOrder;

            var imgGo = new GameObject("FadeOverlay", typeof(RectTransform), typeof(Image));
            imgGo.transform.SetParent(canvasGo.transform, false);
            var rect = imgGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            overlayImage = imgGo.GetComponent<Image>();
            overlayImage.color = new Color(0f, 0f, 0f, 0f);
            overlayImage.raycastTarget = false;
        }

        public void StartFade(Color targetColor, float targetAlpha, float durationSeconds, Action onCompleted)
        {
            EnsureOverlayBuilt();
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

            if (!Application.isPlaying || durationSeconds <= 0f)
            {
                overlayImage.color = new Color(targetColor.r, targetColor.g, targetColor.b, targetAlpha);
                onCompleted?.Invoke();
                return;
            }

            _fadeRoutine = StartCoroutine(FadeRoutine(targetColor, targetAlpha, durationSeconds, onCompleted));
        }

        public void StartFlash(Color flashColor, float durationSeconds, Action onCompleted)
        {
            EnsureOverlayBuilt();
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);

            if (!Application.isPlaying || durationSeconds <= 0f)
            {
                overlayImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
                onCompleted?.Invoke();
                return;
            }

            _flashRoutine = StartCoroutine(FlashRoutine(flashColor, durationSeconds, onCompleted));
        }

        public void StartCameraShake(float intensity, float durationSeconds, Action onCompleted)
        {
            if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (!Application.isPlaying || cam == null || durationSeconds <= 0f)
            {
                onCompleted?.Invoke();
                return;
            }

            _shakeRoutine = StartCoroutine(ShakeRoutine(cam, intensity, durationSeconds, onCompleted));
        }

        public void CancelTransitions()
        {
            if (_fadeRoutine != null) { StopCoroutine(_fadeRoutine); _fadeRoutine = null; }
            if (_flashRoutine != null) { StopCoroutine(_flashRoutine); _flashRoutine = null; }
            if (_shakeRoutine != null) { StopCoroutine(_shakeRoutine); _shakeRoutine = null; }

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (_hasSavedCamPos && cam != null)
            {
                cam.transform.localPosition = _originalCamLocalPos;
                _hasSavedCamPos = false;
            }
        }

        private IEnumerator FadeRoutine(Color targetColor, float targetAlpha, float duration, Action onCompleted)
        {
            Color start = overlayImage.color;
            Color end = new Color(targetColor.r, targetColor.g, targetColor.b, targetAlpha);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                overlayImage.color = Color.Lerp(start, end, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            overlayImage.color = end;
            _fadeRoutine = null;
            onCompleted?.Invoke();
        }

        private IEnumerator FlashRoutine(Color flashColor, float duration, Action onCompleted)
        {
            float peakAlpha = flashColor.a > 0f ? flashColor.a : 0.8f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                overlayImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, Mathf.Lerp(peakAlpha, 0f, t));
                yield return null;
            }
            overlayImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, 0f);
            _flashRoutine = null;
            onCompleted?.Invoke();
        }

        private IEnumerator ShakeRoutine(Camera cam, float intensity, float duration, Action onCompleted)
        {
            if (!_hasSavedCamPos)
            {
                _originalCamLocalPos = cam.transform.localPosition;
                _hasSavedCamPos = true;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float damper = 1f - Mathf.Clamp01(elapsed / duration);
                Vector3 offset = UnityEngine.Random.insideUnitSphere * (intensity * damper);
                offset.z = 0f;
                cam.transform.localPosition = _originalCamLocalPos + offset;
                yield return null;
            }

            cam.transform.localPosition = _originalCamLocalPos;
            _hasSavedCamPos = false;
            _shakeRoutine = null;
            onCompleted?.Invoke();
        }
    }
}
