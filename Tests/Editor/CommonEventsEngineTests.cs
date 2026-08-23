using System;
using System.Collections.Generic;
using CommonEventsEngine.Commands;
using CommonEventsEngine.Core;
using CommonEventsEngine.Data;
using CommonEventsEngine.Engine;
using CommonEventsEngine.State;
using NUnit.Framework;
using UnityEngine;

namespace CommonEventsEngine.Tests
{
    public sealed class CommonEventsEngineTests
    {
        private sealed class ScriptedDialogueAndScreen : IEventDialogueHandler, IScreenTransitionHandler
        {
            public readonly List<string> ShownMessages = new List<string>();
            public int NextChoiceIndex { get; set; } = 0;
            public int FlashCount { get; private set; }
            public Action PendingMessageCallback { get; private set; }
            public Action<int> PendingChoiceCallback { get; private set; }
            public bool AutoComplete { get; set; } = true;

            public void ShowMessage(string speakerName, string message, Sprite portrait, Action onCompleted)
            {
                ShownMessages.Add($"{speakerName}: {message}");
                if (AutoComplete) onCompleted?.Invoke();
                else PendingMessageCallback = onCompleted;
            }

            public void ShowChoices(string prompt, IReadOnlyList<string> choices, int cancelChoiceIndex, Action<int> onChoiceSelected)
            {
                if (AutoComplete) onChoiceSelected?.Invoke(NextChoiceIndex);
                else PendingChoiceCallback = onChoiceSelected;
            }

            public void CancelActiveDialogue()
            {
                PendingMessageCallback = null;
                PendingChoiceCallback = null;
            }

            public void StartFade(Color targetColor, float targetAlpha, float durationSeconds, Action onCompleted) => onCompleted?.Invoke();
            public void StartFlash(Color flashColor, float durationSeconds, Action onCompleted) { FlashCount++; onCompleted?.Invoke(); }
            public void StartCameraShake(float intensity, float durationSeconds, Action onCompleted) => onCompleted?.Invoke();
            public void CancelTransitions() { }
        }

        [Test]
        public void SampleMerchantPreset_BuysPotionWhenPlayerHasGold_AndDeclinesWhenPoor()
        {
            var evt = ScriptableObject.CreateInstance<CommonEventAsset>();
            try
            {
                evt.ApplySampleMerchantPreset();
                Assert.AreEqual(0, evt.ValidateStructure().Count);

                var stub = new ScriptedDialogueAndScreen { NextChoiceIndex = 0, AutoComplete = true };
                var runner = new CommonEventRunner { DialogueHandler = stub, ScreenHandler = stub };
                runner.WorldState.SetGold(100);

                runner.StartEvent(evt);
                runner.Tick(0.016f);

                Assert.AreEqual(50, runner.WorldState.Gold);
                Assert.AreEqual(1, runner.WorldState.GetItemCount("potion_hp"));
                Assert.AreEqual(1, stub.FlashCount);

                // Spend remaining gold so player is too poor (30G < 50G)
                runner.WorldState.SetGold(30);
                stub.ShownMessages.Clear();
                runner.StartEvent(evt);
                runner.Tick(0.016f);

                Assert.AreEqual(30, runner.WorldState.Gold);
                Assert.AreEqual(1, runner.WorldState.GetItemCount("potion_hp"));
                Assert.IsTrue(stub.ShownMessages[stub.ShownMessages.Count - 1].Contains("short on Gold"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(evt);
            }
        }

        [Test]
        public void ValidateStructure_CatchesUnclosedIfAndStrayElse()
        {
            var evt = ScriptableObject.CreateInstance<CommonEventAsset>();
            try
            {
                evt.commands = new List<EventCommandBase>
                {
                    new ElseCommand(),
                    new IfCommand(),
                    new BreakLoopCommand()
                };

                var issues = evt.ValidateStructure();
                Assert.AreEqual(3, issues.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(evt);
            }
        }

        [Test]
        public void LoopAndBreakLoop_AccumulatesVariableAccurately()
        {
            var evt = ScriptableObject.CreateInstance<CommonEventAsset>();
            try
            {
                evt.commands = new List<EventCommandBase>
                {
                    new SetVariableCommand { variableKey = "Counter", operation = VariableOperation.Set, operand = EventValue.FromInt(0) },
                    new LoopCommand { maxIterations = 10 },
                    new SetVariableCommand { variableKey = "Counter", operation = VariableOperation.Add, operand = EventValue.FromInt(1) },
                    new IfCommand
                    {
                        condition = new EventCondition
                        {
                            kind = ConditionKind.VariableCompare,
                            key = "Counter",
                            compareOperator = CompareOperator.GreaterOrEqual,
                            compareValue = EventValue.FromInt(4)
                        }
                    },
                    new BreakLoopCommand(),
                    new EndIfCommand(),
                    new EndLoopCommand()
                };

                var runner = new CommonEventRunner();
                runner.StartEvent(evt);
                runner.Tick(0.016f);

                Assert.AreEqual(4, runner.WorldState.GetVariable("Counter").AsInt());
                Assert.AreEqual(0, runner.ActiveProcesses.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(evt);
            }
        }

        [Test]
        public void WaitSecondsAndWaitForSignal_ResumeDeterministically()
        {
            var evt = ScriptableObject.CreateInstance<CommonEventAsset>();
            try
            {
                evt.commands = new List<EventCommandBase>
                {
                    new WaitSecondsCommand { durationSeconds = 1.0f },
                    new SetSwitchCommand { switchKey = "AfterWait", operation = SwitchOperation.SetOn },
                    new WaitForSignalCommand { signalName = "boss_dead", storePayloadInVariable = "BossName" },
                    new SetSwitchCommand { switchKey = "AfterSignal", operation = SwitchOperation.SetOn }
                };

                var runner = new CommonEventRunner();
                runner.StartEvent(evt);

                runner.Tick(0.5f);
                Assert.IsFalse(runner.WorldState.GetSwitch("AfterWait"));

                runner.Tick(0.6f);
                Assert.IsTrue(runner.WorldState.GetSwitch("AfterWait"));
                Assert.IsFalse(runner.WorldState.GetSwitch("AfterSignal"));

                runner.EmitSignal("boss_dead", "CrimsonDragon");
                runner.Tick(0.016f);

                Assert.IsTrue(runner.WorldState.GetSwitch("AfterSignal"));
                Assert.AreEqual("CrimsonDragon", runner.WorldState.GetVariable("BossName").AsString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(evt);
            }
        }

        [Test]
        public void AutorunLocksControl_AndParallelRunsWhileSwitchIsOn()
        {
            var autorunEvt = ScriptableObject.CreateInstance<CommonEventAsset>();
            var parallelEvt = ScriptableObject.CreateInstance<CommonEventAsset>();
            try
            {
                autorunEvt.eventId = "auto_cutscene";
                autorunEvt.triggerMode = CommonEventTriggerMode.Autorun;
                autorunEvt.lockPlayerControlOnAutorun = true;
                autorunEvt.triggerConditions.Add(new EventCondition { kind = ConditionKind.GlobalSwitch, key = "StartCutscene", expectedBool = true });
                autorunEvt.commands = new List<EventCommandBase>
                {
                    new WaitSecondsCommand { durationSeconds = 0.5f },
                    new SetSwitchCommand { switchKey = "StartCutscene", operation = SwitchOperation.SetOff },
                    new SetSwitchCommand { switchKey = "CutsceneDone", operation = SwitchOperation.SetOn }
                };

                parallelEvt.eventId = "para_regen";
                parallelEvt.triggerMode = CommonEventTriggerMode.Parallel;
                parallelEvt.triggerConditions.Add(new EventCondition { kind = ConditionKind.GlobalSwitch, key = "RegenActive", expectedBool = true });
                parallelEvt.commands = new List<EventCommandBase>
                {
                    new SetVariableCommand { variableKey = "Ticks", operation = VariableOperation.Add, operand = EventValue.FromInt(1) },
                    new WaitSecondsCommand { durationSeconds = 0.2f }
                };

                var runner = new CommonEventRunner();
                runner.RegisterEvent(autorunEvt);
                runner.RegisterEvent(parallelEvt);

                var lockHistory = new List<bool>();
                runner.OnControlLockChanged += locked => lockHistory.Add(locked);

                runner.WorldState.SetSwitch("StartCutscene", true);
                runner.WorldState.SetSwitch("RegenActive", true);

                runner.Tick(0.1f);
                Assert.IsTrue(runner.IsControlLocked);
                Assert.AreEqual(1, runner.WorldState.GetVariable("Ticks").AsInt());

                runner.Tick(0.5f);
                Assert.IsFalse(runner.IsControlLocked);
                Assert.IsTrue(runner.WorldState.GetSwitch("CutsceneDone"));
                CollectionAssert.AreEqual(new[] { true, false }, lockHistory);

                // Parallel event stops cleanly when switch turns off
                runner.WorldState.SetSwitch("RegenActive", false);
                runner.Tick(0.1f);
                Assert.AreEqual(0, runner.ActiveProcesses.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(autorunEvt);
                UnityEngine.Object.DestroyImmediate(parallelEvt);
            }
        }

        [Test]
        public void CallCommonEvent_PassesLocalArguments_AndEnforcesRecursionGuard()
        {
            var caller = ScriptableObject.CreateInstance<CommonEventAsset>();
            var callee = ScriptableObject.CreateInstance<CommonEventAsset>();
            var selfRec = ScriptableObject.CreateInstance<CommonEventAsset>();
            try
            {
                callee.displayName = "Callee";
                callee.commands = new List<EventCommandBase>
                {
                    new CopyVariableCommand { sourceKey = "BonusGold", sourceIsLocal = true, destinationKey = "ReceivedBonus", destinationIsLocal = false }
                };

                caller.commands = new List<EventCommandBase>
                {
                    new CallCommonEventCommand
                    {
                        targetEvent = callee,
                        arguments = new List<CallArgumentEntry>
                        {
                            new CallArgumentEntry { key = "BonusGold", value = EventValue.FromInt(250) }
                        }
                    }
                };

                var runner = new CommonEventRunner { MaxCallDepth = 4 };
                runner.StartEvent(caller);
                runner.Tick(0.016f);

                Assert.AreEqual(250, runner.WorldState.GetVariable("ReceivedBonus").AsInt());

                // Infinite recursion guard test
                selfRec.displayName = "Recursive";
                selfRec.commands = new List<EventCommandBase>
                {
                    new CallCommonEventCommand { targetEvent = selfRec }
                };
                runner.StartEvent(selfRec);
                runner.Tick(0.016f);
                Assert.IsTrue(runner.Logs.Count > 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(caller);
                UnityEngine.Object.DestroyImmediate(callee);
                UnityEngine.Object.DestroyImmediate(selfRec);
            }
        }

        [Test]
        public void SelfSwitches_SceneBindings_AndWorldStateJsonSnapshot_WorkCleanly()
        {
            var evt = ScriptableObject.CreateInstance<CommonEventAsset>();
            var playerGo = new GameObject("TestPlayer");
            var markerGo = new GameObject("TownMarker");
            markerGo.transform.position = new Vector3(12f, 3f, -5f);
            var doorGo = new GameObject("BossDoor");

            try
            {
                evt.commands = new List<EventCommandBase>
                {
                    new SetSelfSwitchCommand { slot = SelfSwitchSlot.A, value = true },
                    new SetObjectActiveCommand { bindingKey = "BossDoor", active = false },
                    new TeleportObjectCommand { targetBindingKey = "Player", destinationMarkerKey = "TownMarker" },
                    new ModifyGoldCommand { delta = 777 },
                    new ModifyItemCommand { itemId = "key_silver", delta = 2 }
                };

                var runner = new CommonEventRunner();
                runner.SceneBindings.Register("Player", playerGo);
                runner.SceneBindings.Register("TownMarker", markerGo);
                runner.SceneBindings.Register("BossDoor", doorGo);

                runner.StartEvent(evt, ownerKey: "Chest_01");
                runner.Tick(0.016f);

                Assert.IsTrue(runner.WorldState.GetSelfSwitch("Chest_01", SelfSwitchSlot.A));
                Assert.IsFalse(runner.WorldState.GetSelfSwitch("Chest_02", SelfSwitchSlot.A));
                Assert.IsFalse(doorGo.activeSelf);
                Assert.AreEqual(markerGo.transform.position, playerGo.transform.position);

                string json = runner.WorldState.ExportJson();
                var restored = new WorldStateStore();
                restored.ImportJson(json);

                Assert.IsTrue(restored.GetSelfSwitch("Chest_01", SelfSwitchSlot.A));
                Assert.AreEqual(777, restored.Gold);
                Assert.AreEqual(2, restored.GetItemCount("key_silver"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(evt);
                UnityEngine.Object.DestroyImmediate(playerGo);
                UnityEngine.Object.DestroyImmediate(markerGo);
                UnityEngine.Object.DestroyImmediate(doorGo);
            }
        }

        [Test]
        public void StepSingleCommand_AdvancesOneLineAtATime()
        {
            var evt = ScriptableObject.CreateInstance<CommonEventAsset>();
            try
            {
                evt.commands = new List<EventCommandBase>
                {
                    new SetSwitchCommand { switchKey = "Step1", operation = SwitchOperation.SetOn },
                    new SetSwitchCommand { switchKey = "Step2", operation = SwitchOperation.SetOn }
                };

                var runner = new CommonEventRunner();
                var proc = runner.StartEvent(evt);

                Assert.IsTrue(runner.StepSingleCommand(proc));
                Assert.IsTrue(runner.WorldState.GetSwitch("Step1"));
                Assert.IsFalse(runner.WorldState.GetSwitch("Step2"));

                Assert.IsFalse(runner.StepSingleCommand(proc));
                Assert.IsTrue(runner.WorldState.GetSwitch("Step2"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(evt);
            }
        }
    }
}
