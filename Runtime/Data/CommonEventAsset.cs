using System;
using System.Collections.Generic;
using CommonEventsEngine.Commands;
using CommonEventsEngine.Core;
using UnityEngine;

namespace CommonEventsEngine.Data
{
    /// <summary>
    /// Describes a structural block error reported by <see cref="CommonEventAsset.ValidateStructure"/>.
    /// </summary>
    public sealed class CommandValidationIssue
    {
        /// <summary>
        /// Zero-based index into <see cref="CommonEventAsset.commands"/> where the issue was detected.
        /// </summary>
        public int CommandIndex { get; set; }

        /// <summary>
        /// Human-readable description of the structural problem.
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// Reusable RPG Maker-style Common Event ScriptableObject containing trigger mode,
    /// activation conditions, and a flat <c>[SerializeReference]</c> command sequence with block indentation.
    /// </summary>
    [CreateAssetMenu(fileName = "CommonEvent", menuName = "CommonEventsEngine/Common Event Asset", order = 100)]
    public class CommonEventAsset : ScriptableObject
    {
        [Header("Identity & Trigger")]
        public string eventId = "evt_common_01";
        public string displayName = "New Common Event";
        [TextArea(2, 3)] public string description = string.Empty;
        public CommonEventTriggerMode triggerMode = CommonEventTriggerMode.Manual;

        [Header("Trigger Conditions (Used for Autorun / Parallel)")]
        public ConditionCombineMode conditionMode = ConditionCombineMode.AllTrueAnd;
        public List<EventCondition> triggerConditions = new List<EventCondition>();

        [Header("Autorun Behavior")]
        [Tooltip("When true, Autorun raises OnControlLockChanged(true) while executing.")]
        public bool lockPlayerControlOnAutorun = true;

        [Header("Command List")]
        [SerializeReference] public List<EventCommandBase> commands = new List<EventCommandBase>();

        /// <summary>
        /// Computes RPG Maker-style visual indentation depths for every index in <see cref="commands"/>.
        /// </summary>
        /// <returns>An array matching <see cref="commands"/> length where each entry is a non-negative nesting level.</returns>
        public int[] ComputeIndentDepths()
        {
            int count = commands != null ? commands.Count : 0;
            int[] depths = new int[count];
            int depth = 0;

            for (int i = 0; i < count; i++)
            {
                var cmd = commands[i];
                var role = cmd != null ? cmd.BlockRole : CommandBlockRole.Normal;

                if (role == CommandBlockRole.MidBlock || role == CommandBlockRole.CloseBlock)
                {
                    depth = Math.Max(0, depth - 1);
                }

                depths[i] = depth;

                if (role == CommandBlockRole.OpenBlock || role == CommandBlockRole.MidBlock)
                {
                    depth++;
                }
            }

            return depths;
        }

        /// <summary>
        /// Validates matching <see cref="IfCommand"/>/<see cref="ElseCommand"/>/<see cref="EndIfCommand"/>
        /// and <see cref="LoopCommand"/>/<see cref="BreakLoopCommand"/>/<see cref="EndLoopCommand"/> nesting, plus null command slots.
        /// </summary>
        /// <returns>List of all detected structural issues; empty when the command list is well-formed.</returns>
        public List<CommandValidationIssue> ValidateStructure()
        {
            var issues = new List<CommandValidationIssue>();
            if (commands == null) return issues;

            var blockStack = new Stack<(string kind, int index)>();

            for (int i = 0; i < commands.Count; i++)
            {
                var cmd = commands[i];
                if (cmd == null)
                {
                    issues.Add(new CommandValidationIssue { CommandIndex = i, Message = "Null command slot." });
                    continue;
                }

                if (cmd is IfCommand)
                {
                    blockStack.Push(("If", i));
                }
                else if (cmd is ElseCommand)
                {
                    if (blockStack.Count == 0 || blockStack.Peek().kind != "If")
                    {
                        issues.Add(new CommandValidationIssue { CommandIndex = i, Message = "Else without a matching If block." });
                    }
                }
                else if (cmd is EndIfCommand)
                {
                    if (blockStack.Count == 0 || blockStack.Peek().kind != "If")
                    {
                        issues.Add(new CommandValidationIssue { CommandIndex = i, Message = "EndIf without a matching If block." });
                    }
                    else
                    {
                        blockStack.Pop();
                    }
                }
                else if (cmd is LoopCommand)
                {
                    blockStack.Push(("Loop", i));
                }
                else if (cmd is BreakLoopCommand)
                {
                    bool insideLoop = false;
                    foreach (var entry in blockStack)
                    {
                        if (entry.kind == "Loop") { insideLoop = true; break; }
                    }
                    if (!insideLoop)
                    {
                        issues.Add(new CommandValidationIssue { CommandIndex = i, Message = "BreakLoop is outside any Loop block." });
                    }
                }
                else if (cmd is EndLoopCommand)
                {
                    if (blockStack.Count == 0 || blockStack.Peek().kind != "Loop")
                    {
                        issues.Add(new CommandValidationIssue { CommandIndex = i, Message = "EndLoop without a matching Loop block." });
                    }
                    else
                    {
                        blockStack.Pop();
                    }
                }
            }

            while (blockStack.Count > 0)
            {
                var unclosed = blockStack.Pop();
                issues.Add(new CommandValidationIssue
                {
                    CommandIndex = unclosed.index,
                    Message = $"Unclosed {unclosed.kind} block starting at line {unclosed.index + 1}."
                });
            }

            return issues;
        }

        /// <summary>
        /// Overwrites this asset's metadata and command list with a sample Potion Merchant encounter
        /// demonstrating dialogue, choices, nested conditionals, gold/inventory updates, and a screen flash.
        /// </summary>
        public void ApplySampleMerchantPreset()
        {
            eventId = "evt_potion_merchant";
            displayName = "Potion Merchant Encounter";
            description = "Sample RPG encounter using Dialogue, Choices, Gold check, Inventory grant, and Screen Flash.";
            triggerMode = CommonEventTriggerMode.Manual;
            triggerConditions.Clear();
            commands = new List<EventCommandBase>
            {
                new ShowMessageCommand { speakerName = "Alchemist Mira", message = "Welcome, traveler! Want a Health Potion for 50 Gold?" },
                new ShowChoicesCommand
                {
                    prompt = "Buy Health Potion (50G)?",
                    choices = new List<string> { "Buy Potion (-50G)", "No thanks" },
                    resultVariableKey = "LastChoice"
                },
                new IfCommand
                {
                    condition = new EventCondition
                    {
                        kind = ConditionKind.VariableCompare,
                        key = "LastChoice",
                        compareOperator = CompareOperator.Equal,
                        compareValue = EventValue.FromInt(0)
                    }
                },
                new IfCommand
                {
                    condition = new EventCondition
                    {
                        kind = ConditionKind.GoldAmount,
                        compareOperator = CompareOperator.GreaterOrEqual,
                        compareValue = EventValue.FromInt(50)
                    }
                },
                new ModifyGoldCommand { delta = -50 },
                new ModifyItemCommand { itemId = "potion_hp", delta = 1 },
                new FlashScreenCommand { flashColor = new Color(1f, 0.92f, 0.45f, 0.7f), durationSeconds = 0.25f },
                new ShowMessageCommand { speakerName = "Alchemist Mira", message = "Pleasure doing business! Added 1x Health Potion." },
                new ElseCommand(),
                new ShowMessageCommand { speakerName = "Alchemist Mira", message = "Looks like you are short on Gold." },
                new EndIfCommand(),
                new ElseCommand(),
                new ShowMessageCommand { speakerName = "Alchemist Mira", message = "Safe travels!" },
                new EndIfCommand()
            };
        }
    }
}
