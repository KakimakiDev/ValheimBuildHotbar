using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace Kakimaki.BuildHotbar;

public sealed partial class BuildHotbarPlugin
{
    private ConfigEntry<KeyCode> _scrollKey = null!, _saveKey = null!, _clearKey = null!;
    private readonly ConfigEntry<KeyCode>[] _slotKeys = new ConfigEntry<KeyCode>[8];
    private readonly int[] _slotFrames = { -1, -1, -1, -1, -1, -1, -1, -1 };
    private ConfigEntry<bool> _collapsed = null!;
    private ConfigEntry<KeyCode>? _capture;
    private int _captureFrame;
    private string _bindingMessage = "Click a binding, then press a key. Esc cancels; Backspace unbinds.";
    private TMP_Text? _bindingHelp;
    private readonly List<Tuple<ConfigEntry<KeyCode>, TMP_Text>> _bindingLabels = new List<Tuple<ConfigEntry<KeyCode>, TMP_Text>>();
    private GameObject? _collapseButton;
    private TMP_Text? _collapseText;
    private RectTransform? _handleRect;
    private static readonly KeyCode[] CaptureKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

    private void InitializeBindings()
    {
        _scrollKey = Config.Bind("Key bindings", "Row scroll key", KeyCode.LeftAlt, "Hold this key with the mouse wheel to switch rows. None disables row scrolling.");
        _saveKey = Config.Bind("Key bindings", "Save key", KeyCode.LeftShift, "Hold with a slot key to save the hovered or selected piece.");
        _clearKey = Config.Bind("Key bindings", "Clear key", KeyCode.LeftAlt, "Hold with a slot key to clear it.");
        for (int i = 0; i < 8; i++)
            _slotKeys[i] = Config.Bind("Key bindings", "Slot " + (i + 1) + " key", KeyCode.Alpha1 + i, "Select this slot on the current row. Rebind in the built-in settings.");
        _collapsed = Config.Bind("Display", "Collapsed", false, "Hide the slots but retain the title, Settings and expand control. Slot shortcuts remain active.");
    }

    private static KeyCode Canonical(KeyCode key)
    {
        if (key == KeyCode.RightShift) return KeyCode.LeftShift;
        if (key == KeyCode.RightControl) return KeyCode.LeftControl;
        if (key == KeyCode.RightAlt) return KeyCode.LeftAlt;
        return key;
    }

    private static bool Held(KeyCode key)
    {
        if (key == KeyCode.None) return false;
        key = Canonical(key);
        return Input.GetKey(key) || key == KeyCode.LeftShift && Input.GetKey(KeyCode.RightShift) ||
            key == KeyCode.LeftControl && Input.GetKey(KeyCode.RightControl) || key == KeyCode.LeftAlt && Input.GetKey(KeyCode.RightAlt);
    }

    private static string KeyName(KeyCode key)
    {
        if (key == KeyCode.None) return "Unbound";
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)key - (int)KeyCode.Alpha0).ToString();
        if (Canonical(key) == KeyCode.LeftShift) return "Shift";
        if (Canonical(key) == KeyCode.LeftAlt) return "Alt";
        if (Canonical(key) == KeyCode.LeftControl) return "Ctrl";
        return key.ToString();
    }

    private void TriggerSlot(Player player, int slot)
    {
        if (_slotFrames[slot] == Time.frameCount) return;
        _slotFrames[slot] = Time.frameCount;
        HandleSlot(player, slot);
    }

    private void StartBinding(ConfigEntry<KeyCode> entry)
    {
        _capture = entry;
        _captureFrame = Time.frameCount;
        _bindingMessage = "Press a key for " + entry.Definition.Key + ". Esc cancels; Backspace unbinds.";
        RefreshSettings();
    }

    private bool BindingConflict(ConfigEntry<KeyCode> target, KeyCode key)
    {
        if (key == KeyCode.None) return false;
        if (target != _toggleKey && Canonical(key) == Canonical(_toggleKey.Value)) return true;
        if (target == _toggleKey && (Canonical(key) == Canonical(_saveKey.Value) ||
            Canonical(key) == Canonical(_clearKey.Value) || Canonical(key) == Canonical(_scrollKey.Value))) return true;
        foreach (var slot in _slotKeys)
            if (slot != target && Canonical(slot.Value) == Canonical(key)) return true;
        bool isSlot = Array.IndexOf(_slotKeys, target) >= 0;
        if (isSlot && (Canonical(key) == Canonical(_saveKey.Value) || Canonical(key) == Canonical(_clearKey.Value) || Canonical(key) == Canonical(_scrollKey.Value))) return true;
        // Scroll may share a key with save/clear (different gestures); save and clear may not.
        return target == _saveKey && Canonical(key) == Canonical(_clearKey.Value) ||
               target == _clearKey && Canonical(key) == Canonical(_saveKey.Value);
    }

    private void CaptureBinding()
    {
        if (_capture == null || Time.frameCount <= _captureFrame) return;
        if (Input.GetKeyDown(KeyCode.Escape)) { _capture = null; _bindingMessage = "Binding cancelled."; RefreshSettings(); return; }
        foreach (KeyCode rawKey in CaptureKeys)
        {
            if (rawKey == KeyCode.None || (int)rawKey >= (int)KeyCode.Mouse0 || !Input.GetKeyDown(rawKey)) continue;
            KeyCode key = rawKey == KeyCode.Backspace ? KeyCode.None : rawKey;
            if (BindingConflict(_capture, key)) { _bindingMessage = "That key is already assigned. Choose another key, or unbind the other action first."; RefreshSettings(); return; }
            _capture.Value = key;
            _bindingMessage = _capture.Definition.Key + " saved: " + KeyName(key);
            _capture = null;
            _nextRefresh = 0;
            RefreshSettings();
            return;
        }
    }

    private void BindingButton(Transform parent, TMP_Text font, ConfigEntry<KeyCode> entry, Vector2 position, Vector2 size)
    {
        var button = MakeButton(parent, KeyName(entry.Value), font, position, size, () => StartBinding(entry));
        _bindingLabels.Add(Tuple.Create(entry, button.GetComponentInChildren<TMP_Text>()));
    }

    private void ApplyCollapsed()
    {
        if (!_panelRect || !_handleRect) return;
        bool collapsed = _collapsed.Value || !_hotbarActive;
        float height = collapsed ? 28 : 126;
        _panelRect!.sizeDelta = new Vector2(_panelRect.sizeDelta.x, height);
        _handleRect!.anchoredPosition = new Vector2(0, height - 14);
        if (_settingsButton) ((RectTransform)_settingsButton!.transform).anchoredPosition = new Vector2(_panelRect.sizeDelta.x / 2 - 94, height - 14);
        if (_toggleButton) ((RectTransform)_toggleButton!.transform).anchoredPosition = new Vector2(_panelRect.sizeDelta.x / 2 - 170, height - 14);
        if (_toggleText) _toggleText!.text = _hotbarActive ? "Disable" : "Enable";
        if (_collapseButton) ((RectTransform)_collapseButton!.transform).anchoredPosition = new Vector2(_panelRect.sizeDelta.x / 2 - 24, height - 14);
        if (_collapseText) _collapseText!.text = _collapsed.Value ? "+" : "−";
        foreach (var slot in _views) slot.Background.gameObject.SetActive(!collapsed);
        if (_hint) _hint!.gameObject.SetActive(false);
    }
}

