using System;
using System.Collections.Generic;
using System.Reflection;
using CommonEventsEngine.Commands;
using CommonEventsEngine.Components;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using CommonEventsEngine.Engine;
using CommonEventsEngine.State;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CommonEventsEngine.Editor.UI
{
    public sealed class CommonEventsWorkbenchWindow : EditorWindow
    {
        private sealed class SandboxPresentationStub : IEventDialogueHandler, IScreenTransitionHandler, IEventAudioHandler
        {
            public bool HasPendingMessage { get; private set; }
            public string PendingSpeaker { get; private set; }
            public string PendingMessage { get; private set; }
            private Action _messageDone;

            public bool HasPendingChoices { get; private set; }
            public string PendingChoicePrompt { get; private set; }
            public IReadOnlyList<string> PendingChoices { get; private set; }
            private Action<int> _choiceDone;

            public string LastVisualNote { get; private set; } = "No screen/audio effect played yet.";

            public void ShowMessage(string speakerName, string message, Sprite portrait, Action onCompleted)
            {
                HasPendingMessage = true;
                PendingSpeaker = speakerName;
                PendingMessage = message;
                _messageDone = onCompleted;
            }

            public void ConfirmMessage()
            {
                if (!HasPendingMessage) return;
                HasPendingMessage = false;
                var cb = _messageDone;
                _messageDone = null;
                cb?.Invoke();
            }

            public void ShowChoices(string prompt, IReadOnlyList<string> choices, int cancelChoiceIndex, Action<int> onChoiceSelected)
            {
                HasPendingChoices = true;
                PendingChoicePrompt = prompt;
                PendingChoices = choices;
                _choiceDone = onChoiceSelected;
            }

            public void PickChoice(int index)
            {
                if (!HasPendingChoices) return;
                HasPendingChoices = false;
                var cb = _choiceDone;
                _choiceDone = null;
                cb?.Invoke(index);
            }

            public void CancelActiveDialogue()
            {
                HasPendingMessage = false;
                HasPendingChoices = false;
                _messageDone = null;
                _choiceDone = null;
            }

            public void StartFade(Color targetColor, float targetAlpha, float durationSeconds, Action onCompleted)
            {
                LastVisualNote = $"Fade -> alpha {targetAlpha:F2} ({durationSeconds:F2}s)";
                onCompleted?.Invoke();
            }

            public void StartFlash(Color flashColor, float durationSeconds, Action onCompleted)
            {
                LastVisualNote = $"Flash {ColorUtility.ToHtmlStringRGBA(flashColor)} ({durationSeconds:F2}s)";
                onCompleted?.Invoke();
            }

            public void StartCameraShake(float intensity, float durationSeconds, Action onCompleted)
            {
                LastVisualNote = $"Camera Shake intensity={intensity:F2} ({durationSeconds:F2}s)";
                onCompleted?.Invoke();
            }

            public void CancelTransitions() { }

            public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f) =>
                LastVisualNote = $"Play SFX '{(clip != null ? clip.name : "None")}'";

            public void PlayBgm(AudioClip clip, float volume = 0.8f, bool loop = true) =>
                LastVisualNote = $"Play BGM '{(clip != null ? clip.name : "None")}'";

            public void StopBgm() => LastVisualNote = "Stop BGM";
        }

        private enum Tab { Editor, WorldState, Debugger, Signals }

        private Tab _activeTab = Tab.Editor;
        private CommonEventAsset _selectedAsset;
        private CommonEventAsset _sandboxFallbackAsset;
        private int _selectedCommandIndex = -1;

        private CommonEventRunner _sandboxRunner;
        private SandboxPresentationStub _sandboxPresentation;
        private ActiveEventProcess _sandboxProcess;

        private string _newSwitchKey = "HasDungeonKey";
        private string _newVarKey = "QuestStage";
        private int _newVarIntValue = 1;
        private string _newItemKey = "potion_hp";
        private int _newItemQty = 1;
        private string _signalNameInput = "cutscene_continue";
        private string _signalPayloadInput = "ok";

        private VisualElement _body;
        private readonly List<Button> _tabButtons = new List<Button>();

        [MenuItem("Tools/HaruChanZ64/Gameplay/CommonEventsEngine/Event Workbench", false, 132)]
        public static void OpenWindow()
        {
            var win = GetWindow<CommonEventsWorkbenchWindow>("CommonEvents Workbench");
            win.minSize = new Vector2(400, 360);
            win.Show();
        }

        public static void OpenWithAsset(CommonEventAsset asset)
        {
            var win = GetWindow<CommonEventsWorkbenchWindow>("CommonEvents Workbench");
            win.minSize = new Vector2(400, 360);
            win._selectedAsset = asset;
            win._selectedCommandIndex = -1;
            win.RebuildBody();
            win.Show();
        }

        private CommonEventAsset ActiveEvent
        {
            get
            {
                if (_selectedAsset != null) return _selectedAsset;
                if (_sandboxFallbackAsset == null)
                {
                    _sandboxFallbackAsset = CreateInstance<CommonEventAsset>();
                    _sandboxFallbackAsset.ApplySampleMerchantPreset();
                }
                return _sandboxFallbackAsset;
            }
        }

        private CommonEventRunner ActiveRunner
        {
            get
            {
                if (Application.isPlaying && CommonEventDirector.ActiveInstance != null)
                {
                    return CommonEventDirector.ActiveInstance.Runner;
                }
                EnsureSandboxRunner();
                return _sandboxRunner;
            }
        }

        private void EnsureSandboxRunner()
        {
            if (_sandboxRunner != null) return;
            var store = new WorldStateStore();
            store.SetGold(100);
            _sandboxPresentation = new SandboxPresentationStub();
            _sandboxRunner = new CommonEventRunner(store)
            {
                DialogueHandler = _sandboxPresentation,
                ScreenHandler = _sandboxPresentation,
                AudioHandler = _sandboxPresentation
            };
        }

        private void OnDisable()
        {
            if (_sandboxFallbackAsset != null)
            {
                DestroyImmediate(_sandboxFallbackAsset);
                _sandboxFallbackAsset = null;
            }
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("cee-root");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Packages/com.haruchanz64.common-events-engine/Editor/UI/CommonEventsWorkbenchStyles.uss");
            if (uss != null) root.styleSheets.Add(uss);

            // Header
            var header = new VisualElement();
            header.AddToClassList("cee-header");

            var titleBox = new VisualElement();
            titleBox.style.flexGrow = 1;
            titleBox.style.flexShrink = 1;
            titleBox.style.minWidth = 180;
            titleBox.style.marginRight = 8;
            var title = new Label("CommonEventsEngine — Event Workbench");
            title.AddToClassList("cee-title");
            var sub = new Label("RPG Maker-Style Indented Command Editor | Switches, Variables & Inventory | Live Step Debugger");
            sub.AddToClassList("cee-subtitle");
            titleBox.Add(title);
            titleBox.Add(sub);

            var assetField = new ObjectField("Event Asset") { objectType = typeof(CommonEventAsset), value = _selectedAsset };
            assetField.style.flexGrow = 1;
            assetField.style.minWidth = 200;
            assetField.style.maxWidth = 340;
            assetField.RegisterValueChangedCallback(evt =>
            {
                _selectedAsset = evt.newValue as CommonEventAsset;
                _selectedCommandIndex = -1;
                RebuildBody();
            });

            header.Add(titleBox);
            header.Add(assetField);
            root.Add(header);

            // Tabs
            var tabs = new VisualElement();
            tabs.AddToClassList("cee-tabs");
            _tabButtons.Clear();
            AddTab(tabs, "1. Event Command Editor", Tab.Editor);
            AddTab(tabs, "2. Switches, Vars & Inventory", Tab.WorldState);
            AddTab(tabs, "3. Live Step Debugger", Tab.Debugger);
            AddTab(tabs, "4. Signals & Bindings", Tab.Signals);
            root.Add(tabs);

            var scroll = new ScrollView();
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            _body = new VisualElement();
            scroll.Add(_body);
            root.Add(scroll);

            UpdateTabStyles();
            RebuildBody();
        }

        private void AddTab(VisualElement parent, string label, Tab tab)
        {
            var btn = new Button(() =>
            {
                _activeTab = tab;
                UpdateTabStyles();
                RebuildBody();
            }) { text = label };
            btn.AddToClassList("cee-tab");
            btn.userData = tab;
            _tabButtons.Add(btn);
            parent.Add(btn);
        }

        private void UpdateTabStyles()
        {
            for (int i = 0; i < _tabButtons.Count; i++)
            {
                _tabButtons[i].EnableInClassList("cee-tab--active", (Tab)_tabButtons[i].userData == _activeTab);
            }
        }

        private void RebuildBody()
        {
            if (_body == null) return;
            _body.Clear();
            EnsureSandboxRunner();

            switch (_activeTab)
            {
                case Tab.Editor: BuildEditorTab(); break;
                case Tab.WorldState: BuildWorldStateTab(); break;
                case Tab.Debugger: BuildDebuggerTab(); break;
                case Tab.Signals: BuildSignalsTab(); break;
            }
        }

        // ==================== TAB 1: EVENT COMMAND EDITOR ====================
        private void BuildEditorTab()
        {
            var evt = ActiveEvent;
            bool isTempSandbox = _selectedAsset == null;

            var topCard = MakeCard(isTempSandbox
                ? $"Sandbox Preview: {evt.displayName} (Assign a CommonEventAsset above to persist edits)"
                : $"Editing Asset: {evt.displayName}");

            var actionsRow = new VisualElement();
            actionsRow.AddToClassList("cee-row");
            actionsRow.Add(MakeBtn("+ Add Command", "cee-btn--primary", ShowAddCommandMenu));
            actionsRow.Add(MakeBtn("Seed Merchant Preset", "cee-btn--accent", () =>
            {
                Undo.RecordObject(evt, "Seed Merchant Preset");
                evt.ApplySampleMerchantPreset();
                EditorUtility.SetDirty(evt);
                _selectedCommandIndex = -1;
                RebuildBody();
            }));
            actionsRow.Add(MakeBtn("Run in Step Debugger >", null, () =>
            {
                _activeTab = Tab.Debugger;
                UpdateTabStyles();
                StartSandboxEvent();
                RebuildBody();
            }));
            topCard.Add(actionsRow);

            // Trigger mode inline controls
            var trigRow = new VisualElement();
            trigRow.AddToClassList("cee-row");
            var trigField = new EnumField("Trigger Mode", evt.triggerMode);
            trigField.style.flexGrow = 1;
            trigField.style.minWidth = 200;
            trigField.style.maxWidth = 340;
            trigField.RegisterValueChangedCallback(e =>
            {
                Undo.RecordObject(evt, "Change Trigger Mode");
                evt.triggerMode = (CommonEventTriggerMode)e.newValue;
                EditorUtility.SetDirty(evt);
            });
            trigRow.Add(trigField);
            topCard.Add(trigRow);

            // Structure Validation Linter
            var issues = evt.ValidateStructure();
            if (issues.Count > 0)
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    var warn = new Label($"[Validation Warning] Line {issues[i].CommandIndex + 1}: {issues[i].Message}");
                    warn.AddToClassList("cee-warn");
                    topCard.Add(warn);
                }
            }
            _body.Add(topCard);

            // Indented RPG Maker Command List
            var listCard = MakeCard($"Command Sequence ({evt.commands.Count} commands)");
            int[] depths = evt.ComputeIndentDepths();

            for (int i = 0; i < evt.commands.Count; i++)
            {
                int idx = i;
                var cmd = evt.commands[i];
                var row = new VisualElement();
                row.AddToClassList("cee-cmd-row");
                if (idx == _selectedCommandIndex) row.AddToClassList("cee-cmd-row--selected");
                if (cmd != null && cmd.BlockRole != CommandBlockRole.Normal) row.AddToClassList("cee-cmd-row--flow");

                var numLbl = new Label($"{idx + 1:00}");
                numLbl.AddToClassList("cee-cmd-line-num");
                row.Add(numLbl);

                string indent = new string(' ', depths[idx] * 4);
                string prefix = cmd != null && cmd.BlockRole == CommandBlockRole.Normal ? "◆ " : "▸ ";
                var textBtn = new Button(() =>
                {
                    _selectedCommandIndex = idx;
                    RebuildBody();
                })
                {
                    text = $"{indent}{prefix}{(cmd != null ? cmd.GetDisplaySummary() : "(Null Command)")}"
                };
                textBtn.style.flexGrow = 1;
                textBtn.style.flexShrink = 1;
                textBtn.style.minWidth = 0;
                textBtn.style.overflow = Overflow.Hidden;
                textBtn.style.textOverflow = TextOverflow.Ellipsis;
                textBtn.style.whiteSpace = WhiteSpace.NoWrap;
                textBtn.style.unityTextAlign = TextAnchor.MiddleLeft;
                textBtn.style.backgroundColor = Color.clear;
                textBtn.style.borderBottomWidth = 0;
                textBtn.style.borderTopWidth = 0;
                textBtn.style.borderLeftWidth = 0;
                textBtn.style.borderRightWidth = 0;
                row.Add(textBtn);

                row.Add(MakeBtn("↑", null, () => MoveCommand(idx, -1)));
                row.Add(MakeBtn("↓", null, () => MoveCommand(idx, 1)));
                row.Add(MakeBtn("X", "cee-btn--danger", () => DeleteCommand(idx)));

                listCard.Add(row);
            }
            _body.Add(listCard);

            // Inline Property Editor for selected command
            if (_selectedCommandIndex >= 0 && _selectedCommandIndex < evt.commands.Count)
            {
                var propCard = MakeCard($"Selected Command Properties (Line {_selectedCommandIndex + 1})");
                var so = new SerializedObject(evt);
                var commandsProp = so.FindProperty("commands");
                if (commandsProp != null && _selectedCommandIndex < commandsProp.arraySize)
                {
                    var elemProp = commandsProp.GetArrayElementAtIndex(_selectedCommandIndex);
                    var propField = new PropertyField(elemProp, evt.commands[_selectedCommandIndex].GetType().Name);
                    propField.Bind(so);
                    propField.RegisterValueChangeCallback(_ => EditorUtility.SetDirty(evt));
                    propCard.Add(propField);
                }
                _body.Add(propCard);
            }
        }

        private void ShowAddCommandMenu()
        {
            var menu = new GenericMenu();
            var types = TypeCache.GetTypesDerivedFrom<EventCommandBase>();
            var entries = new List<(string menuPath, Type type)>();

            foreach (var t in types)
            {
                if (t.IsAbstract || t.IsGenericType) continue;
                var attr = t.GetCustomAttribute<EventCommandAttribute>();
                string path = attr != null ? attr.MenuPath : $"Custom/{t.Name}";
                entries.Add((path, t));
            }

            entries.Sort((a, b) => string.Compare(a.menuPath, b.menuPath, StringComparison.Ordinal));

            foreach (var entry in entries)
            {
                Type captured = entry.type;
                menu.AddItem(new GUIContent(entry.menuPath), false, () =>
                {
                    var evt = ActiveEvent;
                    Undo.RecordObject(evt, $"Add {captured.Name}");
                    var instance = (EventCommandBase)Activator.CreateInstance(captured);
                    int insertAt = _selectedCommandIndex >= 0 && _selectedCommandIndex < evt.commands.Count
                        ? _selectedCommandIndex + 1
                        : evt.commands.Count;
                    evt.commands.Insert(insertAt, instance);
                    _selectedCommandIndex = insertAt;
                    EditorUtility.SetDirty(evt);
                    RebuildBody();
                });
            }

            menu.ShowAsContext();
        }

        private void MoveCommand(int idx, int delta)
        {
            var evt = ActiveEvent;
            int target = idx + delta;
            if (target < 0 || target >= evt.commands.Count) return;
            Undo.RecordObject(evt, "Reorder Command");
            var temp = evt.commands[idx];
            evt.commands[idx] = evt.commands[target];
            evt.commands[target] = temp;
            _selectedCommandIndex = target;
            EditorUtility.SetDirty(evt);
            RebuildBody();
        }

        private void DeleteCommand(int idx)
        {
            var evt = ActiveEvent;
            if (idx < 0 || idx >= evt.commands.Count) return;
            Undo.RecordObject(evt, "Delete Command");
            evt.commands.RemoveAt(idx);
            _selectedCommandIndex = Math.Min(_selectedCommandIndex, evt.commands.Count - 1);
            EditorUtility.SetDirty(evt);
            RebuildBody();
        }

        // ==================== TAB 2: WORLD STATE & INVENTORY ====================
        private void BuildWorldStateTab()
        {
            var runner = ActiveRunner;
            var store = runner.WorldState;
            string modeLabel = Application.isPlaying && CommonEventDirector.ActiveInstance != null
                ? "LIVE PLAYMODE (CommonEventDirector.ActiveInstance)"
                : "EDITMODE SANDBOX RUNNER";

            var headerCard = MakeCard($"World State Inspector — {modeLabel}");
            var row = new VisualElement();
            row.AddToClassList("cee-row");
            row.Add(new Label($"Gold: {store.Gold}G   |   Switches: {store.Switches.Count}   |   Variables: {store.Variables.Count}   |   Items: {store.GetAllItems().Count}"));
            headerCard.Add(row);

            var utilRow = new VisualElement();
            utilRow.AddToClassList("cee-row");
            utilRow.Add(MakeBtn("+50 Gold", "cee-btn--primary", () => { store.ModifyGold(50); RebuildBody(); }));
            utilRow.Add(MakeBtn("-50 Gold", null, () => { store.ModifyGold(-50); RebuildBody(); }));
            utilRow.Add(MakeBtn("Copy Save JSON", "cee-btn--accent", () =>
            {
                EditorGUIUtility.systemCopyBuffer = store.ExportJson(true);
                Debug.Log("[CommonEventsEngine] WorldState JSON snapshot copied to clipboard.");
            }));
            utilRow.Add(MakeBtn("Clear State", "cee-btn--danger", () => { store.Clear(); RebuildBody(); }));
            headerCard.Add(utilRow);
            _body.Add(headerCard);

            // Switches Card
            var swCard = MakeCard("Global Switches");
            var addSwRow = new VisualElement();
            addSwRow.AddToClassList("cee-row");
            var swKeyField = new TextField("Switch Key") { value = _newSwitchKey };
            swKeyField.style.flexGrow = 1;
            swKeyField.style.minWidth = 180;
            swKeyField.RegisterValueChangedCallback(e => _newSwitchKey = e.newValue);
            addSwRow.Add(swKeyField);
            addSwRow.Add(MakeBtn("Set ON", "cee-btn--primary", () => { store.SetSwitch(_newSwitchKey, true); RebuildBody(); }));
            addSwRow.Add(MakeBtn("Set OFF", null, () => { store.SetSwitch(_newSwitchKey, false); RebuildBody(); }));
            swCard.Add(addSwRow);

            foreach (var kvp in store.Switches)
            {
                string key = kvp.Key;
                bool val = kvp.Value;
                var r = new VisualElement();
                r.AddToClassList("cee-row");
                r.Add(new Label($"• {key}: {(val ? "ON" : "OFF")}") { style = { flexGrow = 1, minWidth = 160, whiteSpace = WhiteSpace.Normal } });
                r.Add(MakeBtn("Toggle", null, () => { store.ToggleSwitch(key); RebuildBody(); }));
                swCard.Add(r);
            }
            _body.Add(swCard);

            // Variables Card
            var varCard = MakeCard("Global Variables");
            var addVarRow = new VisualElement();
            addVarRow.AddToClassList("cee-row");
            var vkField = new TextField("Key") { value = _newVarKey };
            vkField.style.flexGrow = 1;
            vkField.style.minWidth = 150;
            vkField.RegisterValueChangedCallback(e => _newVarKey = e.newValue);
            var vvField = new IntegerField("Int Value") { value = _newVarIntValue };
            vvField.style.flexGrow = 0.6f;
            vvField.style.minWidth = 130;
            vvField.RegisterValueChangedCallback(e => _newVarIntValue = e.newValue);
            addVarRow.Add(vkField);
            addVarRow.Add(vvField);
            addVarRow.Add(MakeBtn("Set Variable", "cee-btn--accent", () => { store.SetVariable(_newVarKey, EventValue.FromInt(_newVarIntValue)); RebuildBody(); }));
            varCard.Add(addVarRow);

            foreach (var kvp in store.Variables)
            {
                varCard.Add(new Label($"• {kvp.Key} ({kvp.Value.type}) = {kvp.Value}") { style = { whiteSpace = WhiteSpace.Normal } });
            }
            _body.Add(varCard);

            // Inventory Card
            var invCard = MakeCard("Inventory Items");
            var invRow = new VisualElement();
            invRow.AddToClassList("cee-row");
            var ikField = new TextField("Item ID") { value = _newItemKey };
            ikField.style.flexGrow = 1;
            ikField.style.minWidth = 150;
            ikField.RegisterValueChangedCallback(e => _newItemKey = e.newValue);
            var iqField = new IntegerField("Delta") { value = _newItemQty };
            iqField.style.flexGrow = 0.5f;
            iqField.style.minWidth = 120;
            iqField.RegisterValueChangedCallback(e => _newItemQty = e.newValue);
            invRow.Add(ikField);
            invRow.Add(iqField);
            invRow.Add(MakeBtn("Modify Item", "cee-btn--primary", () => { store.ModifyItem(_newItemKey, _newItemQty); RebuildBody(); }));
            invCard.Add(invRow);

            foreach (var kvp in store.GetAllItems())
            {
                invCard.Add(new Label($"• {kvp.Key} x{kvp.Value}"));
            }
            _body.Add(invCard);
        }

        // ==================== TAB 3: LIVE STEP DEBUGGER ====================
        private void BuildDebuggerTab()
        {
            var evt = ActiveEvent;
            var runner = ActiveRunner;

            var ctrlCard = MakeCard($"Live Step Debugger — {evt.displayName}");
            var btnRow = new VisualElement();
            btnRow.AddToClassList("cee-row");
            btnRow.Add(MakeBtn("Start Event", "cee-btn--primary", () => { StartSandboxEvent(); RebuildBody(); }));
            btnRow.Add(MakeBtn("Step 1 Command", "cee-btn--accent", () =>
            {
                if (_sandboxProcess == null || _sandboxProcess.IsFinished) StartSandboxEvent();
                runner.StepSingleCommand(_sandboxProcess, 0.1f);
                RebuildBody();
            }));
            btnRow.Add(MakeBtn("Tick +0.25s", null, () => { runner.Tick(0.25f); RebuildBody(); }));
            btnRow.Add(MakeBtn("Reset Runner", "cee-btn--danger", () =>
            {
                runner.CancelAllProcesses();
                _sandboxPresentation?.CancelActiveDialogue();
                _sandboxProcess = null;
                RebuildBody();
            }));
            ctrlCard.Add(btnRow);

            var status = _sandboxProcess != null && !_sandboxProcess.IsFinished
                ? $"RUNNING — Stack Depth: {_sandboxProcess.CallStack.Count} | Current Line: {_sandboxProcess.CurrentFrame.InstructionPointer + 1} | Control Locked: {runner.IsControlLocked}"
                : $"IDLE — Active Processes: {runner.ActiveProcesses.Count} | Gold: {runner.WorldState.Gold}G";
            ctrlCard.Add(new Label(status));

            if (_sandboxPresentation != null)
            {
                var fxLbl = new Label($"Screen/Audio FX: {_sandboxPresentation.LastVisualNote}");
                fxLbl.AddToClassList("cee-muted");
                ctrlCard.Add(fxLbl);

                if (_sandboxPresentation.HasPendingMessage)
                {
                    var dlg = new VisualElement();
                    dlg.AddToClassList("cee-dialogue-box");
                    dlg.Add(new Label($"[{_sandboxPresentation.PendingSpeaker}]") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                    var msg = new Label(_sandboxPresentation.PendingMessage);
                    msg.AddToClassList("cee-mono");
                    dlg.Add(msg);
                    dlg.Add(MakeBtn("Next >", "cee-btn--primary", () =>
                    {
                        _sandboxPresentation.ConfirmMessage();
                        runner.Tick(0.016f);
                        RebuildBody();
                    }));
                    ctrlCard.Add(dlg);
                }
                else if (_sandboxPresentation.HasPendingChoices)
                {
                    var dlg = new VisualElement();
                    dlg.AddToClassList("cee-dialogue-box");
                    dlg.Add(new Label(_sandboxPresentation.PendingChoicePrompt) { style = { unityFontStyleAndWeight = FontStyle.Bold } });
                    for (int c = 0; c < _sandboxPresentation.PendingChoices.Count; c++)
                    {
                        int choiceIdx = c;
                        dlg.Add(MakeBtn($"{c + 1}. {_sandboxPresentation.PendingChoices[c]}", "cee-btn--accent", () =>
                        {
                            _sandboxPresentation.PickChoice(choiceIdx);
                            runner.Tick(0.016f);
                            RebuildBody();
                        }));
                    }
                    ctrlCard.Add(dlg);
                }
            }
            _body.Add(ctrlCard);

            // Live Command Pointer View
            var traceCard = MakeCard("Execution Pointer");
            int activeIp = _sandboxProcess != null && !_sandboxProcess.IsFinished && _sandboxProcess.CurrentFrame.EventAsset == evt
                ? _sandboxProcess.CurrentFrame.InstructionPointer
                : -1;
            int[] depths = evt.ComputeIndentDepths();

            for (int i = 0; i < evt.commands.Count; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("cee-cmd-row");
                if (i == activeIp) row.AddToClassList("cee-cmd-row--active");
                string marker = i == activeIp ? "▶ " : "  ";
                string indent = new string(' ', depths[i] * 4);
                row.Add(new Label($"{marker}{i + 1:00}  {indent}{(evt.commands[i] != null ? evt.commands[i].GetDisplaySummary() : "(Null)")}"));
                traceCard.Add(row);
            }
            _body.Add(traceCard);

            // Runner Logs
            if (runner.Logs.Count > 0)
            {
                var logCard = MakeCard($"Execution Logs ({runner.Logs.Count})");
                for (int i = Math.Max(0, runner.Logs.Count - 12); i < runner.Logs.Count; i++)
                {
                    var l = new Label(runner.Logs[i]);
                    l.AddToClassList("cee-mono");
                    logCard.Add(l);
                }
                _body.Add(logCard);
            }
        }

        private void StartSandboxEvent()
        {
            var runner = ActiveRunner;
            runner.CancelAllProcesses();
            _sandboxPresentation?.CancelActiveDialogue();
            _sandboxProcess = runner.StartEvent(ActiveEvent, ownerKey: "WorkbenchSandbox");
        }

        // ==================== TAB 4: SIGNALS & BINDINGS ====================
        private void BuildSignalsTab()
        {
            var runner = ActiveRunner;

            var emitCard = MakeCard("Emit Signal");
            var row = new VisualElement();
            row.AddToClassList("cee-row");
            var sigField = new TextField("Signal Name") { value = _signalNameInput };
            sigField.style.flexGrow = 1;
            sigField.style.minWidth = 170;
            sigField.RegisterValueChangedCallback(e => _signalNameInput = e.newValue);
            var payField = new TextField("Payload") { value = _signalPayloadInput };
            payField.style.flexGrow = 1;
            payField.style.minWidth = 150;
            payField.RegisterValueChangedCallback(e => _signalPayloadInput = e.newValue);
            row.Add(sigField);
            row.Add(payField);
            row.Add(MakeBtn("Emit & Tick", "cee-btn--primary", () =>
            {
                runner.EmitSignal(_signalNameInput, _signalPayloadInput);
                runner.Tick(0.016f);
                RebuildBody();
            }));
            emitCard.Add(row);
            _body.Add(emitCard);

            var histCard = MakeCard($"Signal History ({runner.SignalHistory.Count})");
            for (int i = runner.SignalHistory.Count - 1; i >= Math.Max(0, runner.SignalHistory.Count - 20); i--)
            {
                var s = runner.SignalHistory[i];
                histCard.Add(new Label($"#{s.Sequence}  '{s.SignalName}'  payload=\"{s.Payload}\""));
            }
            _body.Add(histCard);

            var bindCard = MakeCard($"Active Scene Bindings ({runner.SceneBindings.Bindings.Count})");
            foreach (var kvp in runner.SceneBindings.Bindings)
            {
                bindCard.Add(new Label($"• {kvp.Key} -> {(kvp.Value != null ? kvp.Value.name : "(Destroyed)")}"));
            }
            _body.Add(bindCard);
        }

        private static VisualElement MakeCard(string title)
        {
            var card = new VisualElement();
            card.AddToClassList("cee-card");
            var lbl = new Label(title);
            lbl.AddToClassList("cee-card-title");
            card.Add(lbl);
            return card;
        }

        private static Button MakeBtn(string text, string variant, Action onClick)
        {
            var btn = new Button(onClick) { text = text };
            btn.AddToClassList("cee-btn");
            if (!string.IsNullOrEmpty(variant)) btn.AddToClassList(variant);
            return btn;
        }
    }
}
