using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Madlands.Client
{
    /// <summary>
    /// The chat box: a log of recent lines that fade after a while, and an input line opened with
    /// Enter (Esc cancels). Lines starting with '.' are GM commands; the server decides.
    /// </summary>
    public sealed class ChatPanel
    {
        private const int MaxLines = 60;
        private const int VisibleLines = 16;
        private const float FadeAfterSeconds = 30f;

        private readonly VisualElement _log;
        private readonly TextField _input;
        private readonly Action<string> _send;
        private readonly List<(Label Label, float Time)> _lines = new List<(Label, float)>();
        private int _closedFrame = -1;

        public ChatPanel(VisualElement log, TextField input, Action<string> send)
        {
            _log = log;
            _input = input;
            _send = send;
            _input.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            _input.RegisterCallback<FocusOutEvent>(_ => Close());
        }

        public bool IsOpen { get; private set; }

        /// <summary>True on the frame the input was closed, so the Enter that sent the line does not reopen it.</summary>
        public bool ClosedThisFrame => _closedFrame == Time.frameCount;

        public void Open(string prefill = "")
        {
            if (IsOpen) return;
            IsOpen = true;
            InputGate.Typing = true;
            _input.RemoveFromClassList("hidden");
            _input.value = prefill;
            _input.Focus();
            foreach (var (label, _) in _lines) label.style.opacity = 1f;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            InputGate.Typing = false;
            _input.value = "";
            _input.AddToClassList("hidden");
            _input.Blur();
            _closedFrame = Time.frameCount;
        }

        public void AddLine(string text, string cssClass = null)
        {
            foreach (var part in text.Split('\n'))
            {
                var label = new Label(part) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("chat-line");
                if (cssClass != null) label.AddToClassList(cssClass);
                _log.Add(label);
                _lines.Add((label, Time.time));
            }
            while (_lines.Count > MaxLines)
            {
                _lines[0].Label.RemoveFromHierarchy();
                _lines.RemoveAt(0);
            }
            for (int i = 0; i < _lines.Count; i++)
                _lines[i].Label.style.display = i >= _lines.Count - VisibleLines ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Update()
        {
            if (IsOpen) return;
            float now = Time.time;
            foreach (var (label, time) in _lines)
            {
                float age = now - time;
                label.style.opacity = age < FadeAfterSeconds ? 1f : Mathf.Clamp01(1f - (age - FadeAfterSeconds) / 3f);
            }
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                var text = _input.value?.Trim();
                if (!string.IsNullOrEmpty(text)) _send(text);
                Close();
                e.StopPropagation();
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                Close();
                e.StopPropagation();
            }
        }
    }
}
