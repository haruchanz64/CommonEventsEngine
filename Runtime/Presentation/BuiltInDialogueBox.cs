using System;
using System.Collections.Generic;
using CommonEventsEngine.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CommonEventsEngine.Presentation
{
    /// <summary>
    /// Turnkey uGUI implementation of IEventDialogueHandler.
    /// Automatically builds a styled RPG Maker-style dialogue and choice Canvas if no custom uGUI
    /// references are assigned in the Inspector, using built-in UnityEngine.UI (no TMP import required).
    /// </summary>
    public sealed class BuiltInDialogueBox : MonoBehaviour, IEventDialogueHandler
    {
        [Header("Optional Custom uGUI References (Auto-built if left empty)")]
        [SerializeField] private GameObject dialogueRoot;
        [SerializeField] private Text speakerText;
        [SerializeField] private Text bodyText;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Button continueButton;
        [SerializeField] private RectTransform choicesContainer;

        [Header("Presentation Settings")]
        [SerializeField, Min(0f)] private float charactersPerSecond = 60f;
        [SerializeField] private int canvasSortOrder = 200;

        private readonly List<Button> _spawnedChoiceButtons = new List<Button>();
        private Action _onMessageDone;
        private Action<int> _onChoiceDone;
        private string _fullMessage;
        private float _revealProgress;
        private bool _isTyping;
        private bool _awaitingMessageAdvance;

        private void Awake()
        {
            EnsureUiBuilt();
            HidePanel();
        }

        private void Update()
        {
            if (!_isTyping) return;

            if (charactersPerSecond <= 0f)
            {
                FinishTypewriter();
                return;
            }

            _revealProgress += Time.unscaledDeltaTime * charactersPerSecond;
            int visible = Mathf.Clamp(Mathf.FloorToInt(_revealProgress), 0, _fullMessage.Length);
            if (bodyText != null)
            {
                bodyText.text = _fullMessage.Substring(0, visible);
            }

            if (visible >= _fullMessage.Length)
            {
                FinishTypewriter();
            }
        }

        public void EnsureUiBuilt()
        {
            if (dialogueRoot != null)
            {
                if (continueButton != null)
                {
                    continueButton.onClick.RemoveListener(AdvanceOrCompleteMessage);
                    continueButton.onClick.AddListener(AdvanceOrCompleteMessage);
                }
                return;
            }

            Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("CommonEvents_DialogueCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = canvasSortOrder;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Bottom RPG dialogue panel
            var panelGo = new GameObject("DialoguePanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            var panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.08f, 0.04f);
            panelRect.anchorMax = new Vector2(0.92f, 0.30f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panelGo.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.16f, 0.94f);

            // Portrait slot
            var portGo = new GameObject("Portrait", typeof(RectTransform), typeof(Image));
            portGo.transform.SetParent(panelGo.transform, false);
            var portRect = portGo.GetComponent<RectTransform>();
            portRect.anchorMin = new Vector2(0.02f, 0.12f);
            portRect.anchorMax = new Vector2(0.15f, 0.88f);
            portRect.offsetMin = Vector2.zero;
            portRect.offsetMax = Vector2.zero;
            portraitImage = portGo.GetComponent<Image>();
            portraitImage.preserveAspect = true;
            portGo.SetActive(false);

            // Speaker name
            var spkGo = new GameObject("SpeakerText", typeof(RectTransform), typeof(Text));
            spkGo.transform.SetParent(panelGo.transform, false);
            var spkRect = spkGo.GetComponent<RectTransform>();
            spkRect.anchorMin = new Vector2(0.17f, 0.72f);
            spkRect.anchorMax = new Vector2(0.85f, 0.94f);
            spkRect.offsetMin = Vector2.zero;
            spkRect.offsetMax = Vector2.zero;
            speakerText = spkGo.GetComponent<Text>();
            speakerText.font = uiFont;
            speakerText.fontSize = 32;
            speakerText.fontStyle = FontStyle.Bold;
            speakerText.color = new Color(0.98f, 0.80f, 0.32f, 1f);

            // Body text
            var bodyGo = new GameObject("BodyText", typeof(RectTransform), typeof(Text));
            bodyGo.transform.SetParent(panelGo.transform, false);
            var bodyRect = bodyGo.GetComponent<RectTransform>();
            bodyRect.anchorMin = new Vector2(0.17f, 0.14f);
            bodyRect.anchorMax = new Vector2(0.84f, 0.70f);
            bodyRect.offsetMin = Vector2.zero;
            bodyRect.offsetMax = Vector2.zero;
            bodyText = bodyGo.GetComponent<Text>();
            bodyText.font = uiFont;
            bodyText.fontSize = 28;
            bodyText.color = Color.white;

            // Continue button
            var btnGo = new GameObject("ContinueButton", typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(panelGo.transform, false);
            var btnRect = btnGo.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.86f, 0.14f);
            btnRect.anchorMax = new Vector2(0.98f, 0.42f);
            btnRect.offsetMin = Vector2.zero;
            btnRect.offsetMax = Vector2.zero;
            btnGo.GetComponent<Image>().color = new Color(0.20f, 0.45f, 0.85f, 1f);
            continueButton = btnGo.GetComponent<Button>();
            continueButton.onClick.AddListener(AdvanceOrCompleteMessage);

            var btnLblGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            btnLblGo.transform.SetParent(btnGo.transform, false);
            var btnLblRect = btnLblGo.GetComponent<RectTransform>();
            btnLblRect.anchorMin = Vector2.zero;
            btnLblRect.anchorMax = Vector2.one;
            btnLblRect.offsetMin = Vector2.zero;
            btnLblRect.offsetMax = Vector2.zero;
            var btnLbl = btnLblGo.GetComponent<Text>();
            btnLbl.font = uiFont;
            btnLbl.fontSize = 24;
            btnLbl.alignment = TextAnchor.MiddleCenter;
            btnLbl.color = Color.white;
            btnLbl.text = "Next >";

            // Choices container (above dialogue panel)
            var choiceGo = new GameObject("ChoicesContainer", typeof(RectTransform), typeof(VerticalLayoutGroup));
            choiceGo.transform.SetParent(canvasGo.transform, false);
            choicesContainer = choiceGo.GetComponent<RectTransform>();
            choicesContainer.anchorMin = new Vector2(0.58f, 0.33f);
            choicesContainer.anchorMax = new Vector2(0.92f, 0.76f);
            choicesContainer.offsetMin = Vector2.zero;
            choicesContainer.offsetMax = Vector2.zero;
            var vlg = choiceGo.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 10f;
            vlg.childAlignment = TextAnchor.LowerRight;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            dialogueRoot = canvasGo;
        }

        public void ShowMessage(string speakerName, string message, Sprite portrait, Action onCompleted)
        {
            EnsureUiBuilt();
            ClearChoiceButtons();

            dialogueRoot.SetActive(true);
            if (continueButton != null) continueButton.gameObject.SetActive(true);
            if (speakerText != null) speakerText.text = speakerName ?? string.Empty;

            if (portraitImage != null)
            {
                portraitImage.sprite = portrait;
                portraitImage.gameObject.SetActive(portrait != null);
            }

            _fullMessage = message ?? string.Empty;
            _onMessageDone = onCompleted;
            _awaitingMessageAdvance = true;
            _revealProgress = 0f;

            if (charactersPerSecond <= 0f || !Application.isPlaying)
            {
                if (bodyText != null) bodyText.text = _fullMessage;
                _isTyping = false;
            }
            else
            {
                if (bodyText != null) bodyText.text = string.Empty;
                _isTyping = true;
            }
        }

        public void ShowChoices(string prompt, IReadOnlyList<string> choices, int cancelChoiceIndex, Action<int> onChoiceSelected)
        {
            EnsureUiBuilt();
            ClearChoiceButtons();

            dialogueRoot.SetActive(true);
            if (continueButton != null) continueButton.gameObject.SetActive(false);
            if (speakerText != null) speakerText.text = "Choice";
            if (bodyText != null) bodyText.text = prompt ?? string.Empty;
            if (portraitImage != null) portraitImage.gameObject.SetActive(false);

            _onChoiceDone = onChoiceSelected;
            _awaitingMessageAdvance = false;
            _isTyping = false;

            Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < choices.Count; i++)
            {
                int capturedIndex = i;
                var optGo = new GameObject($"Choice_{i}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                optGo.transform.SetParent(choicesContainer, false);
                optGo.GetComponent<Image>().color = new Color(0.14f, 0.20f, 0.32f, 0.96f);
                var le = optGo.GetComponent<LayoutElement>();
                le.preferredHeight = 56f;

                var btn = optGo.GetComponent<Button>();
                btn.onClick.AddListener(() => SelectChoice(capturedIndex));

                var txtGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
                txtGo.transform.SetParent(optGo.transform, false);
                var tr = txtGo.GetComponent<RectTransform>();
                tr.anchorMin = Vector2.zero;
                tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(18f, 4f);
                tr.offsetMax = new Vector2(-18f, -4f);
                var txt = txtGo.GetComponent<Text>();
                txt.font = uiFont;
                txt.fontSize = 26;
                txt.alignment = TextAnchor.MiddleLeft;
                txt.color = Color.white;
                txt.text = choices[i];

                _spawnedChoiceButtons.Add(btn);
            }
        }

        public void AdvanceOrCompleteMessage()
        {
            if (!_awaitingMessageAdvance) return;

            if (_isTyping)
            {
                FinishTypewriter();
                return;
            }

            _awaitingMessageAdvance = false;
            var cb = _onMessageDone;
            _onMessageDone = null;
            HidePanel();
            cb?.Invoke();
        }

        public void SelectChoice(int choiceIndex)
        {
            var cb = _onChoiceDone;
            _onChoiceDone = null;
            ClearChoiceButtons();
            HidePanel();
            cb?.Invoke(choiceIndex);
        }

        public void CancelActiveDialogue()
        {
            _isTyping = false;
            _awaitingMessageAdvance = false;
            _onMessageDone = null;
            _onChoiceDone = null;
            ClearChoiceButtons();
            HidePanel();
        }

        private void FinishTypewriter()
        {
            _isTyping = false;
            if (bodyText != null) bodyText.text = _fullMessage;
        }

        private void ClearChoiceButtons()
        {
            for (int i = 0; i < _spawnedChoiceButtons.Count; i++)
            {
                if (_spawnedChoiceButtons[i] != null)
                {
                    if (Application.isPlaying) Destroy(_spawnedChoiceButtons[i].gameObject);
                    else DestroyImmediate(_spawnedChoiceButtons[i].gameObject);
                }
            }
            _spawnedChoiceButtons.Clear();
        }

        private void HidePanel()
        {
            if (dialogueRoot != null) dialogueRoot.SetActive(false);
        }
    }
}
