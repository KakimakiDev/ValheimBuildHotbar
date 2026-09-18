using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kakimaki.BuildHotbar;

public sealed partial class BuildHotbarPlugin
{
    private GameObject? _controllerSettingsRoot;
    private readonly List<Button> _controllerSettingRows = new List<Button>();
    private TMP_Text? _controllerSettingsHelp;
    private int _controllerSettingIndex, _settingsHeldMask, _controllerCapture = -1;
    private readonly ControllerChordCapture _controllerChord = new ControllerChordCapture();
    private string _controllerSettingsMessage = "Changes save immediately.";
    private bool ControllerSettingsOpen => _controllerSettingsRoot && _controllerSettingsRoot!.activeInHierarchy;

    private int ControllerMenuMask()
    {
        string[] actions = { "JoyDPadUp", "JoyDPadDown", "JoyDPadLeft", "JoyDPadRight", "JoyButtonA", "JoyButtonB", "JoyStart" };
        int mask = 0;
        for (int i = 0; i < actions.Length; i++) if (ZInput.GetButton(actions[i])) mask |= 1 << i;
        return mask;
    }

    private void OpenControllerSettings()
    {
        if (ControllerSettingsOpen) return;
        var message = MessageHud.instance;
        if (!message || !message.m_messageText || !message.m_messageText.font || !Hud.instance) return;
        _wheelConsumedFrame = Time.frameCount;
        _controllerCapture = -1;
        _controllerSettingIndex = 0;
        _settingsHeldMask = ControllerMenuMask();
        _controllerSettingsMessage = "Changes save immediately.";
        var font = message.m_messageText;
        _controllerSettingsRoot = new GameObject("ControllerHotbarSettings", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var overlay = (RectTransform)_controllerSettingsRoot.transform;
        overlay.SetParent(Hud.instance.m_rootObject.transform, false);
        overlay.anchorMin = Vector2.zero;
        overlay.anchorMax = Vector2.one;
        overlay.offsetMin = overlay.offsetMax = Vector2.zero;
        _controllerSettingsRoot.GetComponent<Image>().color = new Color(0, 0, 0, 0.7f);
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(overlay, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(650, 780);
        var canvas = (RectTransform)Hud.instance.m_rootObject.transform;
        float scale = Mathf.Clamp(Mathf.Min((canvas.rect.width - 24) / 650, (canvas.rect.height - 24) / 780), 0.1f, 1);
        rect.localScale = Vector3.one * scale;
        panel.GetComponent<Image>().color = Background;
        Text(rect, "Title", font, 24, new Vector2(0, 744), new Vector2(610, 38)).text = "BUILD HOTBAR SETTINGS";
        NativeHint(Text(rect, "Navigation", font, 15, new Vector2(0, 709), new Vector2(610, 30)),
            Glyph("JoyDPadUp") + "/" + Glyph("JoyDPadDown") + " Navigate   " + Glyph("JoyDPadLeft") + "/" + Glyph("JoyDPadRight") + " Adjust   " + Glyph("JoyButtonA") + " Edit   " + Glyph("JoyButtonB") + " Back");
        _controllerSettingRows.Clear();
        for (int i = 0; i < 16; i++)
        {
            int row = i;
            var button = MakeButton(rect, "", font, new Vector2(0, 664 - i * 34), new Vector2(590, 30), () =>
            {
                _controllerSettingIndex = row;
                AdjustControllerSetting(1);
            });
            _controllerSettingRows.Add(button);
        }
        _controllerSettingsHelp = Text(rect, "Help", font, 15, new Vector2(0, 73), new Vector2(590, 88));
        RefreshControllerSettings();
    }

    private void PollControllerSettings()
    {
        int mask = ControllerMenuMask();
        int pressed = mask & ~_settingsHeldMask;
        _settingsHeldMask = mask;
        if (_controllerCapture >= 0)
        {
            if ((pressed & 64) != 0 || Input.GetKeyDown(KeyCode.Escape))
            {
                _controllerCapture = -1;
                _controllerSettingsMessage = "Binding cancelled.";
            }
            else
            {
                WheelButtons held = ReadControllerButtons();
                if (_controllerChord.Sample((int)held))
                {
                    var capturedButtons = (WheelButtons)_controllerChord.Buttons;
                    bool saving = _controllerCapture == 9;
                    if ((saving && (capturedButtons & (WheelButtons.Cross_A | WheelButtons.Circle_B)) != 0) ||
                        capturedButtons == (saving ? _wheelShortcut.Value : _wheelSaveShortcut.Value))
                        _controllerSettingsMessage = "Conflict: use distinct shortcuts; save cannot contain Cross/A or Circle/B.";
                    else
                    {
                        if (saving) _wheelSaveShortcut.Value = capturedButtons;
                        else _wheelShortcut.Value = capturedButtons;
                        _controllerSettingsMessage = "Saved: " + capturedButtons;
                    }
                    _controllerCapture = -1;
                }
            }
            RefreshControllerSettings();
            return;
        }
        if ((pressed & (32 | 64)) != 0 || Input.GetKeyDown(KeyCode.Escape)) { CloseControllerSettings(); return; }
        if ((pressed & 3) == 1) _controllerSettingIndex = (_controllerSettingIndex + _controllerSettingRows.Count - 1) % _controllerSettingRows.Count;
        if ((pressed & 3) == 2) _controllerSettingIndex = (_controllerSettingIndex + 1) % _controllerSettingRows.Count;
        if ((pressed & 12) == 4) AdjustControllerSetting(-1);
        else if ((pressed & 12) == 8 || (pressed & 16) != 0) AdjustControllerSetting(1);
        RefreshControllerSettings();
    }

    private static WheelButtons ReadControllerButtons()
    {
        int mask = 0;
        for (int i = 0; i < WheelButtonActions.Length; i++)
            if (ZInput.GetButton(WheelButtonActions[i])) mask |= 1 << i;
        return (WheelButtons)mask;
    }

    private void AdjustControllerSetting(int direction)
    {
        if (_controllerCapture >= 0) return;
        switch (_controllerSettingIndex)
        {
            case 0: _wheelEnabled.Value = !_wheelEnabled.Value; break;
            case 1: _wheelToggle.Value = !_wheelToggle.Value; break;
            case 2: ChangeSlots(direction); break;
            case 3: ChangeRows(direction); break;
            case 4: _autoActivate.Value = !_autoActivate.Value; break;
            case 5: _collapsed.Value = !_collapsed.Value; break;
            case 6: _reverseScroll.Value = !_reverseScroll.Value; break;
            case 7: _wheelDeadzone.Value = Mathf.Clamp(Mathf.Round((_wheelDeadzone.Value + direction * 0.05f) * 100) / 100, 0.15f, 0.8f); break;
            case 8:
            case 9:
                _controllerCapture = _controllerSettingIndex;
                _controllerChord.Reset();
                break;
            case 10: _position.Value += new Vector2(direction * 10, 0); break;
            case 11: _position.Value += new Vector2(0, direction * 10); break;
            case 12: ChangeScale(false, direction); break;
            case 13: ChangeScale(true, direction); break;
            case 14: _position.Value = new Vector2(0, -24); break;
            case 15: CloseControllerSettings(); return;
        }
        _nextRefresh = 0;
        RefreshControllerSettings();
    }

    private void RefreshControllerSettings()
    {
        if (!ControllerSettingsOpen) return;
        string[] labels = {
            "Controller wheel: " + (_wheelEnabled.Value ? "On" : "Off"),
            "Confirm selection: " + (_wheelToggle.Value ? Glyph("JoyButtonA") : "Release opening shortcut"),
            "Slots per row: " + _count.Value, "Saved rows: " + _rows.Value,
            "Auto activate on equip: " + (_autoActivate.Value ? "On" : "Off"),
            "Collapse horizontal hotbar: " + (_collapsed.Value ? "On" : "Off"),
            "Reverse mouse-wheel paging: " + (_reverseScroll.Value ? "On" : "Off"),
            "Stick deadzone: " + _wheelDeadzone.Value.ToString("0.00"),
            "Open wheel: " + ChordGlyphs(_wheelShortcut.Value), "Additional save shortcut: " + ChordGlyphs(_wheelSaveShortcut.Value),
            "Hotbar horizontal position: " + _position.Value.x.ToString("0"),
            "Hotbar vertical position: " + _position.Value.y.ToString("0"),
            "Horizontal hotbar size: " + ScaleLabel(_horizontalScale.Value),
            "Radial menu size: " + ScaleLabel(_radialScale.Value),
            "Reset hotbar position", "Done"
        };
        for (int i = 0; i < labels.Length; i++)
        {
            NativeHint(_controllerSettingRows[i].GetComponentInChildren<TMP_Text>(), (i == _controllerSettingIndex ? ">  " : "") + labels[i]);
            _controllerSettingRows[i].GetComponent<Image>().color = i == _controllerSettingIndex ? Selected : Background;
        }
        NativeHint(_controllerSettingsHelp!, _controllerCapture >= 0
            ? "Release all buttons, then hold your new combination together.\nRelease all buttons to save. " + Glyph("JoyStart") + " cancels.\n" + ChordGlyphs((WheelButtons)_controllerChord.Buttons)
            : _controllerSettingsMessage + "\nAlso accessible from the build menu: " + Glyph("JoyStart"));
    }

    private void CloseControllerSettings()
    {
        if (!_controllerSettingsRoot) return;
        _controllerSettingsRoot!.SetActive(false);
        Destroy(_controllerSettingsRoot);
        _controllerSettingsRoot = null;
        _controllerSettingRows.Clear();
        _controllerCapture = -1;
        _wheelConsumedFrame = Time.frameCount;
        _wheelWaitRelease = true;
    }
}
