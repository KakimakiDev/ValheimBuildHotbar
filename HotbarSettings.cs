using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Kakimaki.BuildHotbar;

public sealed partial class BuildHotbarPlugin
{
    private GameObject? _settingsRoot;
    private GameObject? _settingsButton;
    private TMP_Text? _slotsValue;
    private TMP_Text? _rowsValue;
    private TMP_Text? _reverseValue;
    private TMP_Text? _autoValue;
    private Button? _slotsLess;
    private Button? _slotsMore;
    private Button? _rowsLess;
    private Button? _rowsMore;
    private bool SettingsOpen => ControllerSettingsOpen || (_settingsRoot && _settingsRoot!.activeInHierarchy);

    // Keep the underlying build menu open for its cursor, but stop gameplay input
    // while the settings dialog covers it. Never change player input outside it.
    [HarmonyPatch(typeof(Player), "TakeInput")]
    private static class SettingsInputPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (__instance == Player.m_localPlayer && _instance != null && _instance.SettingsOpen)
                __result = false;
        }
    }

    private static Button MakeButton(Transform parent, string caption, TMP_Text font,
        Vector2 position, Vector2 size, UnityAction action)
    {
        var obj = new GameObject(caption + "Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        Position((RectTransform)obj.transform, position, size);
        var image = obj.GetComponent<Image>();
        image.color = new Color(0.24f, 0.19f, 0.11f, 1);
        image.raycastTarget = true;
        var button = obj.GetComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors;
        colors.highlightedColor = new Color(1, 0.87f, 0.6f);
        colors.pressedColor = new Color(0.7f, 0.6f, 0.4f);
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
        button.colors = colors;
        button.onClick.AddListener(action);
        var label = Text(obj.transform, "Label", font, 16, new Vector2(0, size.y / 2), size - new Vector2(8, 2));
        label.text = caption;
        return button;
    }

    private void OpenSettings()
    {
        if (SettingsOpen || !Eligible(Player.m_localPlayer) || !Hud.IsPieceSelectionVisible()) return;
        Hud hud = Hud.instance;
        MessageHud messageHud = MessageHud.instance;
        if (!hud || !messageHud || !messageHud.m_messageText || !messageHud.m_messageText.font) return;
        EndDrag();
        TMP_Text font = messageHud.m_messageText;
        _settingsRoot = new GameObject("BuildHotbarSettingsOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        _settingsRoot.transform.SetParent(hud.m_rootObject.transform, false);
        var overlayRect = (RectTransform)_settingsRoot.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
        var overlay = _settingsRoot.GetComponent<Image>();
        overlay.color = new Color(0, 0, 0, 0.55f);
        overlay.raycastTarget = true;

        var panel = new GameObject("SettingsPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(_settingsRoot.transform, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(590, 836);
        var canvasRect = (RectTransform)hud.m_rootObject.transform;
        float scale = Mathf.Min(1, Mathf.Min((canvasRect.rect.width - 24) / 590f, (canvasRect.rect.height - 24) / 836f));
        rect.localScale = Vector3.one * Mathf.Max(0.1f, scale);
        panel.GetComponent<Image>().color = Background;

        Text(panel.transform, "Title", font, 23, new Vector2(0, 716), new Vector2(550, 36)).text = "BUILD HOTBAR SETTINGS";
        Text(panel.transform, "Subtitle", font, 13, new Vector2(0, 686), new Vector2(550, 22)).text = "Changes apply and save immediately. No extra mod needed.";
        _bindingLabels.Clear();
        SettingsLabel(panel.transform, font, "Toggle hotbar key", 638);
        BindingButton(panel.transform, font, _toggleKey, new Vector2(134, 638), new Vector2(174, 32));
        SettingsLabel(panel.transform, font, "Auto activate on equip", 594);
        var auto = MakeButton(panel.transform, "Off", font, new Vector2(134, 594), new Vector2(174, 32), () =>
        {
            _autoActivate.Value = !_autoActivate.Value;
            RefreshSettings();
        });
        _autoValue = auto.GetComponentInChildren<TMP_Text>();

        _slotsValue = SettingsRow(panel.transform, font, "Slots per row", 550);
        _slotsLess = MakeButton(panel.transform, "−", font, new Vector2(66, 550), new Vector2(36, 32), () => ChangeSlots(-1));
        _slotsMore = MakeButton(panel.transform, "+", font, new Vector2(202, 550), new Vector2(36, 32), () => ChangeSlots(1));
        _rowsValue = SettingsRow(panel.transform, font, "Hotbar rows", 506);
        _rowsLess = MakeButton(panel.transform, "−", font, new Vector2(66, 506), new Vector2(36, 32), () => ChangeRows(-1));
        _rowsMore = MakeButton(panel.transform, "+", font, new Vector2(202, 506), new Vector2(36, 32), () => ChangeRows(1));
        SettingsLabel(panel.transform, font, "Row scroll key + wheel", 462);
        BindingButton(panel.transform, font, _scrollKey, new Vector2(134, 462), new Vector2(174, 32));
        SettingsLabel(panel.transform, font, "Save key + slot", 418);
        BindingButton(panel.transform, font, _saveKey, new Vector2(134, 418), new Vector2(174, 32));
        SettingsLabel(panel.transform, font, "Clear key + slot", 374);
        BindingButton(panel.transform, font, _clearKey, new Vector2(134, 374), new Vector2(174, 32));
        SettingsLabel(panel.transform, font, "Reverse scrolling", 330);
        var reverse = MakeButton(panel.transform, "Off", font, new Vector2(134, 330), new Vector2(174, 32), () =>
        {
            _reverseScroll.Value = !_reverseScroll.Value;
            RefreshSettings();
        });
        _reverseValue = reverse.GetComponentInChildren<TMP_Text>();
        for (int i = 0; i < 8; i++)
        {
            float x = -204 + (i % 4) * 136;
            float y = 268 - (i / 4) * 66;
            Text(panel.transform, "SlotLabel" + i, font, 13, new Vector2(x, y + 24), new Vector2(128, 20)).text = "Slot " + (i + 1);
            BindingButton(panel.transform, font, _slotKeys[i], new Vector2(x, y), new Vector2(128, 32));
        }
        _bindingHelp = Text(panel.transform, "BindingHelp", font, 13, new Vector2(0, 132), new Vector2(550, 46));
        _bindingHelp.overflowMode = TextOverflowModes.Overflow;
        MakeButton(panel.transform, "Reset position", font, new Vector2(-115, 75), new Vector2(210, 34), () => _position.Value = new Vector2(0, -24));
        MakeButton(panel.transform, "Done", font, new Vector2(115, 75), new Vector2(210, 34), CloseSettings);
        Text(panel.transform, "PreservedSlots", font, 13, new Vector2(0, 28), new Vector2(450, 28)).text = "Reducing rows or slots keeps hidden assignments saved.";
        AddScaleSettings(panel.transform, font);
        RefreshSettings();
    }

    private static void SettingsLabel(Transform parent, TMP_Text font, string label, float y)
    {
        var text = Text(parent, label, font, 17, new Vector2(-94, y), new Vector2(242, 34));
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.text = label;
    }

    private static TMP_Text SettingsRow(Transform parent, TMP_Text font, string label, float y)
    {
        SettingsLabel(parent, font, label, y);
        return Text(parent, label + "Value", font, 18, new Vector2(134, y), new Vector2(94, 34));
    }

    private void ChangeSlots(int delta)
    {
        _count.Value = Mathf.Clamp(_count.Value + delta, 1, 8);
        RefreshSettings();
    }

    private void ChangeRows(int delta)
    {
        _rows.Value = Mathf.Clamp(_rows.Value + delta, 1, 12);
        _activeRow.Value = CurrentRow;
        _nextRefresh = 0;
        RefreshSettings();
    }

    private void RefreshSettings()
    {
        if (!_settingsRoot) return;
        if (_horizontalScaleValue) _horizontalScaleValue!.text = ScaleLabel(_horizontalScale.Value);
        if (_radialScaleValue) _radialScaleValue!.text = ScaleLabel(_radialScale.Value);
        _slotsValue!.text = _count.Value.ToString();
        _rowsValue!.text = _rows.Value.ToString();
        foreach (var binding in _bindingLabels)
            binding.Item2.text = _capture == binding.Item1 ? "Press a key…" : KeyName(binding.Item1.Value);
        if (_bindingHelp) _bindingHelp!.text = _bindingMessage;
        _reverseValue!.text = _reverseScroll.Value ? "On" : "Off";
        _autoValue!.text = _autoActivate.Value ? "On" : "Off";
        _slotsLess!.interactable = _count.Value > 1;
        _slotsMore!.interactable = _count.Value < 8;
        _rowsLess!.interactable = _rows.Value > 1;
        _rowsMore!.interactable = _rows.Value < 12;
    }

    private void CloseSettings()
    {
        CloseControllerSettings();
        _capture = null;
        _bindingLabels.Clear();
        _bindingHelp = null;
        if (_settingsRoot) { _settingsRoot!.SetActive(false); Destroy(_settingsRoot); }
        _settingsRoot = null;
    }
}
