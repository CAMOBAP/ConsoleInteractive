﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Wcwidth;

namespace ConsoleInteractive {
    public static class ConsoleSuggestion {
        public const int MaxSuggestionCount = 6, MaxSuggestionLength = 30;

        private static bool InUse = false;

        private static bool AlreadyTriggerTab = false, LastKeyIsTab = false, Hide = false, HideAndClear = false;

        private static Tuple<int, int> TargetTextRange = new(1, 1), LastTextRange = new(1, 1);

        private static int StartIndex = 0, PopupWidth = 0;

        private static int ChoosenIndex = 0, ViewTop = 0, ViewBottom = 0;

        private static Suggestion[] Suggestions = Array.Empty<Suggestion>();


        internal static void BeforeWriteLine(string message, int linesAdded) {
            if (InUse) DrawHelper.ClearSuggestionPopup(linesAdded);
            DrawHelper.AddMessage(message);
        }

        internal static void AfterWriteLine() {
            if (InUse) DrawHelper.DrawSuggestionPopup();
        }

        internal static void OnInputUpdate() {
            LastKeyIsTab = false;
        }

        internal static bool HandleEscape() {
            lock (InternalContext.WriteLock) {
                if (InUse) {
                    Hide = true;
                    ClearSuggestions();
                    return true;
                } else {
                    return false;
                }
            }
        }

        internal static bool HandleTab() {
            lock (InternalContext.WriteLock) {
                if (Hide) {
                    Hide = false;
                    if (!HideAndClear) {
                        InUse = true;
                        DrawHelper.DrawSuggestionPopup();
                    }
                    HideAndClear = false;
                    return true;
                } else if (InUse) {
                    if (AlreadyTriggerTab) {
                        if (LastKeyIsTab)
                            HandleDownArrow();
                        ConsoleBuffer.Replace(LastTextRange.Item1, LastTextRange.Item2, Suggestions[ChoosenIndex].Text);
                    } else {
                        ConsoleBuffer.Replace(TargetTextRange.Item1, TargetTextRange.Item2, Suggestions[ChoosenIndex].Text);
                    }
                    LastTextRange = new(TargetTextRange.Item1, ConsoleBuffer.BufferPosition);
                    AlreadyTriggerTab = true;
                    LastKeyIsTab = true;
                    if (Suggestions[ChoosenIndex].Text == "/")
                        ConsoleReader.CheckInputBufferUpdate();
                    DrawHelper.RedrawOnTab();
                    return true;
                } else {
                    return false;
                }
            }
        }

        internal static bool HandleUpArrow() {
            lock (InternalContext.WriteLock) {
                if (InUse) {
                    LastKeyIsTab = false;
                    if (ChoosenIndex == 0) {
                        ChoosenIndex = Suggestions.Length - 1;
                        ViewTop = Math.Max(0, Suggestions.Length - MaxSuggestionCount);
                        ViewBottom = Suggestions.Length;
                        DrawHelper.DrawSuggestionPopup(refreshMsgBuf: false);
                    } else {
                        --ChoosenIndex;
                        if (ChoosenIndex == ViewTop && ViewTop != 0) {
                            --ViewTop;
                            --ViewBottom;
                            DrawHelper.DrawSuggestionPopup(refreshMsgBuf: false);
                        } else {
                            DrawHelper.RedrawOnArrowKey(offset: 1);
                        }
                    }
                    return true;
                } else {
                    return false;
                }
            }
        }

        internal static bool HandleDownArrow() {
            lock (InternalContext.WriteLock) {
                if (InUse) {
                    LastKeyIsTab = false;
                    if (ChoosenIndex == Suggestions.Length - 1) {
                        ChoosenIndex = 0;
                        ViewTop = 0;
                        ViewBottom = Math.Min(Suggestions.Length, MaxSuggestionCount);
                        DrawHelper.DrawSuggestionPopup(refreshMsgBuf: false);
                    } else {
                        ++ChoosenIndex;
                        if (ChoosenIndex == ViewBottom - 1 && ViewBottom != Suggestions.Length) {
                            ++ViewTop;
                            ++ViewBottom;
                            DrawHelper.DrawSuggestionPopup(refreshMsgBuf: false);
                        } else {
                            DrawHelper.RedrawOnArrowKey(offset: -1);
                        }
                    }
                    return true;
                } else {
                    return false;
                }
            }
        }

        internal static void HandleEnter() {
            ClearSuggestions();
        }

        private static bool CheckIfNeedClear(Suggestion[] Suggestions, Tuple<int, int> range, int maxLength) {
            if (Suggestions.Length < MaxSuggestionCount && ConsoleSuggestion.Suggestions.Length >= MaxSuggestionCount)
                return true;
            if (ConsoleSuggestion.Suggestions.Length < MaxSuggestionCount && ConsoleSuggestion.Suggestions.Length > Suggestions.Length)
                return true;
            if (StartIndex < (range.Item1 - 1))
                return true;
            if (StartIndex + PopupWidth > (range.Item1 - 1 + maxLength + 2))
                return true;
            return false;
        }

        public static void UpdateSuggestions(Suggestion[] Suggestions, Tuple<int, int> range) {
            int maxLength = 0;
            foreach (Suggestion sug in Suggestions)
                maxLength = Math.Max(maxLength,
                    Math.Min(MaxSuggestionLength, sug.ShortText.Length +
                        (sug.TooltipWidth == 0 ? 0 : (sug.TooltipWidth + 1))));

            if (Suggestions.Length == 0 || maxLength == 0) {
                ClearSuggestions();
                return;
            }

            lock (InternalContext.WriteLock) {
                if (InUse && CheckIfNeedClear(Suggestions, range, maxLength))
                    DrawHelper.ClearSuggestionPopup();

                ConsoleSuggestion.Suggestions = Suggestions;

                TargetTextRange = range;
                StartIndex = range.Item1 - 1;

                PopupWidth = maxLength + 2;

                ChoosenIndex = 0;

                ViewTop = 0;
                ViewBottom = Math.Min(Suggestions.Length, MaxSuggestionCount);

                if (!Hide) {
                    InUse = true;
                    DrawHelper.DrawSuggestionPopup();
                }

                AlreadyTriggerTab = false;
                LastKeyIsTab = false;
            }
        }

        public static void ClearSuggestions() {
            lock (InternalContext.WriteLock) {
                if (InUse) {
                    InUse = false;
                    DrawHelper.ClearSuggestionPopup();
                } else if (Hide) {
                    HideAndClear = true;
                }
            }
        }

        private static string? StringToColorCode(string? input, bool background) {
            if (string.IsNullOrWhiteSpace(input))
                return null;
            if (input.Length < 6 || input.Length > 7)
                return null;
            if (input.Length == 6 && input[0] == '#')
                return null;
            if (input.Length == 7 && input[0] != '#')
                return null;
            try {
                int rgb = Convert.ToInt32(input.Length == 7 ? input[1..] : input, 16);

                int r = (rgb & 0xff0000) >> 16;
                int g = (rgb & 0xff00) >> 8;
                int b = (rgb & 0xff);

                return string.Format("\u001b[{0};2;{1};{2};{3}m", background ? 48 : 38, r, g, b);
            } catch {
                return null;
            }
        }

        public static void SetColors(string? Normal = null,
                                     string? NormalBg = null,
                                     string? Highlight = null,
                                     string? HighlightBg = null,
                                     string? Tooltip = null,
                                     string? TooltipHighlight = null,
                                     string? Arrow = null) {
            Normal = StringToColorCode(Normal, false);
            if (!string.IsNullOrEmpty(Normal))
                DrawHelper.NormalColorCode = Normal;

            NormalBg = StringToColorCode(NormalBg, true);
            if (!string.IsNullOrEmpty(NormalBg))
                DrawHelper.NormalBgColorCode = NormalBg;

            Highlight = StringToColorCode(Highlight, false);
            if (!string.IsNullOrEmpty(Highlight))
                DrawHelper.HighlightColorCode = Highlight;

            HighlightBg = StringToColorCode(HighlightBg, true);
            if (!string.IsNullOrEmpty(HighlightBg))
                DrawHelper.HighlightBgColorCode = HighlightBg;

            Tooltip = StringToColorCode(Tooltip, false);
            if (!string.IsNullOrEmpty(Tooltip))
                DrawHelper.TooltipColorCode = Tooltip;

            TooltipHighlight = StringToColorCode(TooltipHighlight, false);
            if (!string.IsNullOrEmpty(TooltipHighlight))
                DrawHelper.HighlightTooltipColorCode = TooltipHighlight;

            Arrow = StringToColorCode(Arrow, false);
            if (!string.IsNullOrEmpty(Arrow))
                DrawHelper.ArrowColorCode = Arrow;
        }

        public class Suggestion {
            public string Text, Tooltip;
            public string ShortText;

            public int TooltipWidth;

            private Tuple<int, string>? _cache = null;

            public Suggestion(string text, string? tooltip = null) {
                Text = text;
                if (Text.Length <= MaxSuggestionLength) {
                    ShortText = Text;
                } else {
                    int half = MaxSuggestionLength / 2;
                    ShortText = Text[..(half - 1)] + (MaxSuggestionLength % 2 == 0 ? ".." : "...") + Text[(Text.Length - half + 1)..];
                }

                Tooltip = tooltip ?? string.Empty;
                TooltipWidth = 0;
                foreach (char c in Tooltip)
                    TooltipWidth += Math.Max(0, UnicodeCalculator.GetWidth(c));
            }

            internal string GetShortTooltip(int widthLimit) {
                widthLimit -= 2;
                if (widthLimit <= 0) return string.Empty;

                if (_cache != null && _cache.Item1 == widthLimit)
                    return _cache.Item2;

                for (int i = Tooltip.Length - 1, limit = widthLimit; i > 0 && limit > 0; --i) {
                    char c = Tooltip[i];
                    int width = Math.Max(0, UnicodeCalculator.GetWidth(c));
                    if (limit - width < 0)
                        return (_cache = new(widthLimit, new string('.', 2 + limit) + Tooltip[(i + 1)..])).Item2;
                    limit -= width;
                    if (limit == 0)
                        return (_cache = new(widthLimit, ".." + Tooltip[i..])).Item2;
                }

                return (_cache = new(widthLimit, "..")).Item2;
            }
        }

        private static class DrawHelper {
            internal static string NormalColorCode = "\u001b[38;2;248;250;252m";          // Slate   5%  (#f8fafc)
            internal static string NormalBgColorCode = "\u001b[48;2;100;116;139m";        // Slate  50%  (#64748b)

            internal static string HighlightColorCode = "\u001b[38;2;51;65;85m";          // Slate  70%  (#334155)
            internal static string HighlightBgColorCode = "\u001b[48;2;253;224;71m";      // Yellow 30%  (#fde047)

            internal static string TooltipColorCode = "\u001b[38;2;125;211;252m";         // Sky    30%  (#7dd3fc)
            internal static string HighlightTooltipColorCode = "\u001b[38;2;59;130;246m"; // Blue   50%  (#3b82f6)

            internal static string ArrowColorCode = "\u001b[38;2;209;213;219m";           // Gray   30%  (#d1d5db)

            internal const string ResetColorCode = "\u001b[0m";

            private static int LastDrawStartPos = -1;
            private static readonly BgMessageBuffer[] BgBuffer = new BgMessageBuffer[MaxSuggestionCount];

            internal static void DrawSuggestionPopup(bool refreshMsgBuf = true) {
                BgMessageBuffer[] messageBuffers = Array.Empty<BgMessageBuffer>();
                int curBufIdx = -1, nextMessageIdx = 0;
                InternalContext.SetCursorVisible(false);
                int top = InternalContext.CurrentCursorTopPos, left = InternalContext.CurrentCursorLeftPos;
                LastDrawStartPos = GetDrawStartPos();
                for (int i = ViewBottom - 1; i >= ViewTop; --i) {
                    if (refreshMsgBuf) {
                        if (curBufIdx < 0) {
                            messageBuffers = GetBgMessageBuffer(RecentMessageHandler.GetRecentMessage(nextMessageIdx), LastDrawStartPos, PopupWidth);
                            curBufIdx = messageBuffers.Length - 1;
                            ++nextMessageIdx;
                        }
                        BgBuffer[i - ViewTop] = messageBuffers[curBufIdx];
                        --curBufIdx;
                    }
                    DrawSingleSuggestionPopup(i, BgBuffer[i - ViewTop], cursorTop: top - (ViewBottom - i));
                }
                Console.SetCursorPosition(left, top);
                InternalContext.SetCursorVisible(true);
            }

            internal static void ClearSuggestionPopup(int linesAdded = 0) {
                int DisplaySuggestionsCnt = Math.Min(MaxSuggestionCount, Suggestions.Length); // Todo: head
                int drawStartPos = GetDrawStartPos();
                InternalContext.SetCursorVisible(false);
                int top = InternalContext.CurrentCursorTopPos, left = InternalContext.CurrentCursorLeftPos;
                for (int i = 0; i < DisplaySuggestionsCnt; ++i) {
                    if (linesAdded > 0 && i >= linesAdded && drawStartPos == LastDrawStartPos) break;
                    ClearSingleSuggestionPopup(BgBuffer[i], cursorTop: top - (DisplaySuggestionsCnt - i));
                }
                Console.SetCursorPosition(left, top);
                InternalContext.SetCursorVisible(true);
            }

            internal static void RedrawOnArrowKey(int offset) {
                InternalContext.SetCursorVisible(false);
                int top = InternalContext.CurrentCursorTopPos, left = InternalContext.CurrentCursorLeftPos;

                int cursorTop = top - (ViewBottom - ChoosenIndex);
                DrawSingleSuggestionPopup(ChoosenIndex, BgBuffer[ChoosenIndex - ViewTop], cursorTop);
                DrawSingleSuggestionPopup(ChoosenIndex + offset, BgBuffer[ChoosenIndex - ViewTop + offset], cursorTop + offset);

                Console.SetCursorPosition(left, top);
                InternalContext.SetCursorVisible(true);
            }

            internal static void RedrawOnTab() {
                if (GetDrawStartPos() != LastDrawStartPos) {
                    ClearSuggestionPopup();
                    DrawSuggestionPopup(refreshMsgBuf: true);
                }
            }

            internal static void AddMessage(string message) {
                RecentMessageHandler.AddMessage(message);
            }

            private static int GetDrawStartPos() {
                return Math.Max(0, Math.Min(InternalContext.CursorLeftPosLimit - PopupWidth, StartIndex + ConsoleBuffer.PrefixTotalLength - ConsoleBuffer.BufferOutputAnchor));
            }

            private static void DrawSingleSuggestionPopup(int index, BgMessageBuffer buf, int cursorTop) {
                if (cursorTop < 0) return;

                StringBuilder sb = new(PopupWidth + 4 * NormalColorCode.Length);

                sb.Append(ResetColorCode);
                if (buf.StartSpace)
                    sb.Append(' ');

                sb.Append(NormalBgColorCode);

                sb.Append(ArrowColorCode);
                if (index == ViewTop && ViewTop != 0)
                    sb.Append('↑');
                else if (index + 1 == ViewBottom && ViewBottom != Suggestions.Length)
                    sb.Append('↓');
                else if (index == ChoosenIndex)
                    sb.Append('>');
                else
                    sb.Append(' ');

                if (index == ChoosenIndex) {
                    if (HighlightColorCode != ArrowColorCode)
                        sb.Append(HighlightColorCode);
                    if (HighlightBgColorCode != NormalBgColorCode)
                        sb.Append(HighlightBgColorCode);
                } else {
                    sb.Append(NormalColorCode);
                }

                sb.Append(Suggestions[index].ShortText);

                int lastSpace = PopupWidth - 2 - Suggestions[index].ShortText.Length;
                if (Suggestions[index].TooltipWidth > 0 && lastSpace > 1) {
                    --lastSpace;
                    sb.Append(' ');

                    if (index == ChoosenIndex) {
                        if (HighlightColorCode != HighlightTooltipColorCode)
                            sb.Append(HighlightTooltipColorCode);
                    } else {
                        if (NormalColorCode != TooltipColorCode)
                            sb.Append(TooltipColorCode);
                    }

                    if (lastSpace >= Suggestions[index].TooltipWidth) {
                        sb.Append(' ', lastSpace - Suggestions[index].TooltipWidth);
                        sb.Append(Suggestions[index].Tooltip);
                    } else {
                        sb.Append(Suggestions[index].GetShortTooltip(lastSpace));
                    }
                } else if (lastSpace > 0) {
                    sb.Append(new string(' ', lastSpace));
                }

                if (index == ChoosenIndex && HighlightBgColorCode != NormalBgColorCode)
                    sb.Append(NormalBgColorCode);

                sb.Append(ArrowColorCode);
                if (index == ViewTop && ViewTop != 0)
                    sb.Append('↑');
                else if (index + 1 == ViewBottom && ViewBottom != Suggestions.Length)
                    sb.Append('↓');
                else if (index == ChoosenIndex)
                    sb.Append('<');
                else
                    sb.Append(' ');

                sb.Append(ResetColorCode);
                if (buf.EndSpace)
                    sb.Append(' ');

                Console.SetCursorPosition(buf.CursorStart, cursorTop);
                Console.Write(sb.ToString());
            }

            private static void ClearSingleSuggestionPopup(BgMessageBuffer buf, int cursorTop) {
                if (cursorTop < 0) return;

                StringBuilder sb = new(PopupWidth + ResetColorCode.Length);

                sb.Append(buf.Text);
                sb.Append(ResetColorCode);
                sb.Append(' ', buf.AfterTextSpace);

                Console.SetCursorPosition(buf.CursorStart, cursorTop);
                Console.Write(sb.ToString());
            }

            private static BgMessageBuffer[] GetBgMessageBuffer(RecentMessageHandler.RecentMessage? msg, int start, int length) {
                if (msg == null || msg.Message.Length == 0)
                    return new BgMessageBuffer[1] { new BgMessageBuffer(start, start + length, length) };

                int charIndex = 0;
                char[] chars = msg.Message.ToCharArray();
                List<BgMessageBuffer> buffers = new();

                while (charIndex < chars.Length) {
                    int cursorPos = 0;
                    int charStart, charEnd;

                    string Text = string.Empty;
                    bool StartSpace = false, EndSpace = false;
                    int CursorStart, CutsorEnd, AfterTextSpace = 0;

                    while (cursorPos < start) {
                        if (charIndex < chars.Length) {
                            int width = Math.Max(0, UnicodeCalculator.GetWidth(chars[charIndex]));
                            if (cursorPos + width > start) {
                                StartSpace = true;
                                break;
                            }
                            cursorPos += width;
                            ++charIndex;
                        } else {
                            cursorPos = start;
                            break;
                        }
                    }
                    charStart = charIndex;
                    CursorStart = cursorPos;

                    int end = start + length;
                    while (cursorPos < end) {
                        if (charIndex < chars.Length) {
                            cursorPos += Math.Max(0, UnicodeCalculator.GetWidth(chars[charIndex]));

                            if (cursorPos > end)
                                EndSpace = true;

                            ++charIndex;
                        } else {
                            int last = end - cursorPos;
                            cursorPos += last;
                            AfterTextSpace += last;
                            break;
                        }
                    }
                    charEnd = charIndex;
                    CutsorEnd = cursorPos;

                    if (charStart < charEnd) {
                        StringBuilder sb = new();

                        int bgIdx = GetLowerBoundColorCode(msg.ColorCodeBG, charStart);
                        while (bgIdx < msg.ColorCodeBG.Length && msg.ColorCodeBG[bgIdx].Item1 < charStart)
                            sb.Append(msg.ColorCodeBG[bgIdx++].Item2);

                        int fgIdx = GetLowerBoundColorCode(msg.ColorCodeFG, charStart);
                        while (fgIdx < msg.ColorCodeFG.Length && msg.ColorCodeFG[fgIdx].Item1 < charStart)
                            sb.Append(msg.ColorCodeFG[fgIdx++].Item2);

                        for (int i = charStart; i < charEnd; ++i) {
                            while (bgIdx < msg.ColorCodeBG.Length && msg.ColorCodeBG[bgIdx].Item1 == i)
                                sb.Append(msg.ColorCodeBG[bgIdx++].Item2);
                            while (fgIdx < msg.ColorCodeFG.Length && msg.ColorCodeFG[fgIdx].Item1 == i)
                                sb.Append(msg.ColorCodeFG[fgIdx++].Item2);
                            sb.Append(chars[i]);
                        }
                        Text = sb.ToString();
                    }

                    buffers.Add(new BgMessageBuffer(CursorStart, CutsorEnd, AfterTextSpace, Text, StartSpace, EndSpace));

                    while (cursorPos < InternalContext.CursorLeftPosLimit) {
                        if (charIndex < chars.Length) {
                            int width = Math.Max(0, UnicodeCalculator.GetWidth(chars[charIndex]));
                            if (cursorPos + width >= InternalContext.CursorLeftPosLimit)
                                break;
                            cursorPos += width;
                            ++charIndex;
                        } else {
                            break;
                        }
                    }
                }

                return buffers.ToArray();
            }

            private static int GetLowerBoundColorCode(Tuple<int, string>[] ColorCodes, int charStart) {
                int left = 0, right = ColorCodes.Length - 1;
                while (left < right) {
                    int mid = (left + right + 1) / 2;
                    if (ColorCodes[mid].Item1 <= charStart)
                        left = mid;
                    else
                        right = mid - 1;
                }
                return left;
            }

            private static class RecentMessageHandler {
                private static int Count = 0, Index = 0;

                private static readonly RecentMessage[] Messages = new RecentMessage[MaxSuggestionCount];

                internal static void AddMessage(string message) {
                    string[] lines = message.Split('\n');
                    int startIdx = Math.Max(0, lines.Length - MaxSuggestionCount);
                    for (int i = startIdx; i < lines.Length; i++) {
                        Index = Count < MaxSuggestionCount ? Count++ : (Index + 1) % MaxSuggestionCount;
                        Messages[Index] = new(lines[i]);
                    }
                }

                internal static RecentMessage? GetRecentMessage(int index) {
                    if (index >= Count)
                        return null;
                    RecentMessage message = Messages[(MaxSuggestionCount + Index - index) % MaxSuggestionCount];
                    message.ParseColorCode();
                    return message;
                }

                internal class RecentMessage {
                    private readonly static Regex ContralCharRegex = new(@"[\u0000-\u001F]", RegexOptions.Compiled);
                    private readonly static Regex EscapeCodeRegex = new(@"\u001B\[[\d;]+m", RegexOptions.Compiled);

                    private readonly static Regex Fg3bitColorCode = new(@"^\u001B\[(?:3|9)[01234567]m$", RegexOptions.Compiled);
                    private readonly static Regex Bg3bitColorCode = new(@"^\u001B\[(?:4|10)[01234567]m$", RegexOptions.Compiled);
                    private readonly static Regex Fg8bitColorCode = new(@"^\u001B\[38;5;(?:1\d{2}|2[0-4]\d|[1-9]?\d|25[0-5])m$", RegexOptions.Compiled);
                    private readonly static Regex Bg8bitColorCode = new(@"^\u001B\[48;5;(?:1\d{2}|2[0-4]\d|[1-9]?\d|25[0-5])m$", RegexOptions.Compiled);
                    private readonly static Regex Fg24bitColorCode = new(@"^\u001B\[38;2(?:;(?:1\d{2}|2[0-4]\d|[1-9]?\d|25[0-5])){3}m$", RegexOptions.Compiled);
                    private readonly static Regex Bg24bitColorCode = new(@"^\u001B\[48;2(?:;(?:1\d{2}|2[0-4]\d|[1-9]?\d|25[0-5])){3}m$", RegexOptions.Compiled);

                    bool Parsed = false;

                    public Tuple<int, string>[] ColorCodeFG = Array.Empty<Tuple<int, string>>();

                    public Tuple<int, string>[] ColorCodeBG = Array.Empty<Tuple<int, string>>();

                    public string Message = string.Empty, RawMessage = string.Empty;

                    public RecentMessage(string message) {
                        RawMessage = message;
                    }

                    public void ParseColorCode() {
                        if (Parsed)
                            return;

                        List<Tuple<int, string>> fgColor = new(), bgColor = new();
                        MatchCollection matchs = EscapeCodeRegex.Matches(RawMessage);
                        int colorLen = 0;
                        foreach (Match match in matchs) {
                            int index = match.Index - colorLen;
                            string code = match.Groups[0].Value;
                            if (code == ResetColorCode)
                                bgColor.Add(new(index, code));
                            else if (IsForcegroundColorCode(code))
                                fgColor.Add(new(index, code));
                            else if (IsBackgroundColorCode(code))
                                bgColor.Add(new(index, code));
                            colorLen += match.Length;
                        }

                        Message = ContralCharRegex.Replace(EscapeCodeRegex.Replace(RawMessage, string.Empty), string.Empty);
                        ColorCodeFG = fgColor.ToArray();
                        ColorCodeBG = bgColor.ToArray();

                        Parsed = true;
                    }

                    private static bool IsForcegroundColorCode(string code) {
                        if (Fg3bitColorCode.IsMatch(code))
                            return true;
                        if (Fg8bitColorCode.IsMatch(code))
                            return true;
                        if (Fg24bitColorCode.IsMatch(code))
                            return true;
                        return false;
                    }

                    private static bool IsBackgroundColorCode(string code) {
                        if (Bg3bitColorCode.IsMatch(code))
                            return true;
                        if (Bg8bitColorCode.IsMatch(code))
                            return true;
                        if (Bg24bitColorCode.IsMatch(code))
                            return true;
                        return false;
                    }
                }
            }

            private record BgMessageBuffer {
                public BgMessageBuffer(int cursorStart, int cutsorEnd, int afterTextSpace) {
                    CursorStart = cursorStart;
                    CutsorEnd = cutsorEnd;
                    StartSpace = false;
                    EndSpace = false;
                    Text = string.Empty;
                    AfterTextSpace = afterTextSpace;
                }

                public BgMessageBuffer(int cursorStart, int cutsorEnd, int afterTextSpace, string text, bool startSpace, bool endSpace) {
                    CursorStart = cursorStart;
                    CutsorEnd = cutsorEnd;
                    StartSpace = startSpace;
                    EndSpace = endSpace;
                    Text = text;
                    AfterTextSpace = afterTextSpace;
                }

                public int CursorStart { get; init; }
                public int CutsorEnd { get; init; }

                public bool StartSpace { get; init; }
                public bool EndSpace { get; init; }

                public string Text { get; init; }

                public int AfterTextSpace { get; init; }
            }
        }
    }
}
