using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kakimaki.BuildHotbar;

public sealed partial class BuildHotbarPlugin
{
    private ConfigEntry<bool> _wheelEnabled = null!;
    private ConfigEntry<bool> _wheelToggle = null!;
    private ConfigEntry<float> _wheelDeadzone = null!;
    private ConfigEntry<WheelButtons> _wheelShortcut = null!, _wheelSaveShortcut = null!;
    private bool _shortcutWasHeld, _saveWasHeld, _settingsOpenWasHeld;
    private bool _rightTriggerWasHeld, _leftTriggerWasHeld, _leftClickWasHeld, _wheelAssigning;
    private int _wheelOpenedFrame = -1;
    private readonly WheelGesture _wheelGesture = new WheelGesture();
    private readonly WheelPaging _wheelPaging = new WheelPaging();
    private bool _wheelOpen, _readingWheel, _wheelWaitRelease;
    private int _wheelFrame = -1, _wheelConsumedFrame = -1, _wheelSlot = -1;
    private PieceTable? _wheelTool;
    private Piece? _wheelSource;
    private GameObject? _wheelRoot;
    private TMP_Text? _wheelTitle;
    private readonly List<SlotView> _wheelViews = new List<SlotView>();
    private bool WheelConsumesInput => ControllerSettingsOpen || _wheelOpen || _wheelConsumedFrame == Time.frameCount;
    // Keep the active gesture and its controller presentation when a screenshot
    // key changes the game's most recent input device. Opening still requires a pad.
    private bool ShowControllerHotbar => _wheelOpen || ZInput.IsGamepadActive();

    private void InitializeWheel()
    {
        _wheelEnabled = Config.Bind("Controller", "Paged wheel", true, "Enable building shortcuts using the configured controller combination.");
        _wheelShortcut = Config.Bind("Controller", "Wheel shortcut", WheelButtons.Square_X, "Buttons held together to open. Combine names with commas, e.g. LeftBumper, Square_X. None disables opening. Stick clicks retain vanilla snapping unless explicitly bound here.");
        _wheelSaveShortcut = Config.Bind("Controller", "Save shortcut", WheelButtons.RightTrigger, "Additional save combination while the normal wheel is open. Right trigger always saves; left trigger clears. Use a different combination from Wheel shortcut.");
        _wheelToggle = Config.Bind("Controller", "Toggle wheel", false, "If enabled, A/Cross confirms explicitly. Otherwise hold the shortcut, aim with the LEFT stick, and release the shortcut to select. The last highlight stays selected when the stick returns to centre.");
        _wheelDeadzone = Config.Bind("Controller", "Wheel deadzone", 0.35f,
            new ConfigDescription("Left-stick selection threshold. Returning to centre retains the highlight without confirming. Independent of camera inversion.", new AcceptableValueRange<float>(0.15f, 0.8f)));
    }

    // Called by input hooks as well as Update so the opening gesture cannot also
    // open the vanilla radial when HUD scripts run before this component.
    private void PollWheel()
    {
        if (_readingWheel || _wheelFrame == Time.frameCount) return;
        _wheelFrame = Time.frameCount;
        _readingWheel = true;
        try
        {
            Player player = Player.m_localPlayer;
            if (_uiNavigationWaitForCentre)
            {
                float x = ZInput.GetJoyLeftStickX(), y = ZInput.GetJoyLeftStickY();
                if (!Hud.IsPieceSelectionVisible() || x * x + y * y <= _wheelDeadzone.Value * _wheelDeadzone.Value)
                    _uiNavigationWaitForCentre = false;
            }
            bool rightTrigger = ZInput.GetButton("JoyRTrigger");
            bool rightPressed = rightTrigger && !_rightTriggerWasHeld;
            _rightTriggerWasHeld = rightTrigger;
            bool leftTrigger = ZInput.GetButton("JoyLTrigger");
            bool leftPressed = leftTrigger && !_leftTriggerWasHeld;
            _leftTriggerWasHeld = leftTrigger;
            bool leftClick = ZInput.GetButton("JoyLStick");
            bool leftClickPressed = leftClick && !_leftClickWasHeld;
            _leftClickWasHeld = leftClick;
            bool settingsHeld = ZInput.GetButton("JoyStart");
            bool settingsPressed = settingsHeld && !_settingsOpenWasHeld;
            _settingsOpenWasHeld = settingsHeld;
            if (ControllerSettingsOpen)
            {
                _wheelConsumedFrame = Time.frameCount;
                if (!Eligible(player) || !Application.isFocused || !Hud.instance || Hud.IsUserHidden()) CloseControllerSettings();
                else PollControllerSettings();
                return;
            }
            // Also available when the wheel is disabled, through the normal build menu.
            if (Eligible(player) && !SettingsOpen && Hud.IsPieceSelectionVisible() && settingsPressed)
            {
                if (_wheelOpen) CloseWheel(rightTrigger || leftTrigger || ShortcutHeld(_wheelShortcut.Value));
                OpenControllerSettings();
                return;
            }
            bool held = ShortcutHeld(_wheelShortcut.Value);
            bool pressed = held && !_shortcutWasHeld;
            _shortcutWasHeld = held;
            bool saveHeld = ShortcutHeld(_wheelSaveShortcut.Value);
            bool savePressed = saveHeld && !_saveWasHeld;
            _saveWasHeld = saveHeld;
            bool menuOpen = Hud.IsPieceSelectionVisible();
            bool openerHeld = _wheelAssigning ? rightTrigger : held;
            bool editingHeld = held || rightTrigger || leftTrigger;
            if (_wheelWaitRelease)
            {
                _wheelConsumedFrame = Time.frameCount;
                if (!editingHeld) _wheelWaitRelease = false;
            }
            bool allowed = _wheelEnabled.Value && Eligible(player) && !SettingsOpen &&
                Hud.instance && Hud.instance.m_rootObject.activeInHierarchy && !Hud.IsUserHidden() &&
                ShowControllerHotbar && Application.isFocused &&
                (bool)TakeInput.Invoke(player, null);
            if (!allowed || (_wheelOpen && (player.GetBuildTool() != _wheelTool || menuOpen != _wheelAssigning)))
            {
                if (_wheelOpen) CloseWheel(editingHeld);
                return;
            }
            if (!_wheelOpen)
            {
                if (_wheelWaitRelease || !(menuOpen ? rightPressed : pressed)) return;
                _wheelOpen = true;
                _wheelAssigning = menuOpen;
                openerHeld = _wheelAssigning ? rightTrigger : held;
                _wheelOpenedFrame = Time.frameCount;
                _wheelTool = player.GetBuildTool();
                _wheelSource = _wheelAssigning ? HoveredPiece(Hud.instance!) : player.GetSelectedPiece();
                _wheelSlot = -1;
                _wheelGesture.Reset();
                _wheelPaging.Reset(ZInput.GetButton("JoyDPadLeft"), ZInput.GetButton("JoyDPadRight"));
                _hotbarActive = true;
                _nextRefresh = 0;
            }
            _wheelConsumedFrame = Time.frameCount;
            if (ZInput.GetButtonDown("JoyDPadDown"))
            {
                CloseWheel(editingHeld);
                OpenControllerSettings();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape) || (Time.frameCount != _wheelOpenedFrame && ZInput.GetButtonDown("JoyButtonB"))) { CloseWheel(editingHeld); return; }
            int pageStep = _wheelPaging.Update(ZInput.GetButton("JoyDPadLeft"),
                ZInput.GetButton("JoyDPadRight"), Time.unscaledTime);
            if (pageStep != 0)
            {
                _activeRow.Value = HotbarRows.Step(CurrentRow, _rows.Value, pageStep);
                _nextRefresh = 0;
                _wheelGesture.Reset();
                _wheelSlot = -1;
            }
            // Both ZInput Y axes are inverted; convert back to screen-up.
            _wheelGesture.Update(ZInput.GetJoyLeftStickX(),
                -ZInput.GetJoyLeftStickY(), _wheelDeadzone.Value, _count.Value);
            if (leftClickPressed) _wheelGesture.ClearHighlight();
            _wheelSlot = _wheelGesture.Selected;
            if (!_wheelAssigning && _wheelSlot >= 0 && Time.frameCount != _wheelOpenedFrame)
            {
                // Clearing wins if both triggers are pressed together.
                if (leftPressed)
                {
                    Slots(_wheelTool!)[_wheelSlot].Value = "";
                    Notify(player, "Cleared row " + CurrentRow + ", slot " + (_wheelSlot + 1));
                    _nextRefresh = 0;
                }
                else if (rightPressed || (savePressed && _wheelSaveShortcut.Value != _wheelShortcut.Value)) SaveWheelSlot(player);
            }
            bool finish = _wheelAssigning ? !rightTrigger :
                (_wheelToggle.Value ? Time.frameCount != _wheelOpenedFrame && ZInput.GetButtonDown("JoyButtonA") : !openerHeld);
            if (finish)
            {
                if (_wheelAssigning)
                {
                    if (_wheelSlot >= 0) SaveWheelSlot(player);
                }
                else if (_wheelSlot >= 0)
                {
                    Piece? piece = Resolve(_wheelTool!, Slots(_wheelTool!)[_wheelSlot].Value);
                    if (piece && player.IsPieceAvailable(piece) && player.SetSelectedPiece(piece)) _nextRefresh = 0;
                    else Notify(player, "This slot is empty or its piece is unavailable.");
                }
                CloseWheel(editingHeld);
            }
        }
        finally { _readingWheel = false; }
    }

    private void SaveWheelSlot(Player player)
    {
        if (_wheelSource && player.IsPieceAvailable(_wheelSource))
        {
            Slots(_wheelTool!)[_wheelSlot].Value = _wheelSource!.gameObject.name;
            Notify(player, "Saved " + Label(_wheelSource) + " to row " + CurrentRow + ", slot " + (_wheelSlot + 1));
            _nextRefresh = 0;
        }
        else Notify(player, "Highlight an available piece in the build menu first.");
    }

    private void CloseWheel(bool waitForRelease)
    {
        RestoreVanillaControlHints();
        if (_wheelAssigning) _uiNavigationWaitForCentre = true;
        _wheelOpen = false;
        _wheelAssigning = false;
        _wheelWaitRelease = waitForRelease;
        _wheelConsumedFrame = Time.frameCount;
        _wheelSlot = -1;
        _wheelTool = null;
        _wheelSource = null;
        if (_wheelRoot) _wheelRoot!.SetActive(false);
    }

    [HarmonyPatch]
    private static class WheelButtonsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButton));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonDown));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetButtonUp));
        }
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            var owner = _instance;
            if (owner == null || owner._readingWheel) return true;
            owner.PollWheel();
            if (!owner.WheelConsumesInput) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    private static class WheelLookPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyRightStickX));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyRightStickY));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLeftStickX));
            yield return AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLeftStickY));
        }
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(ref float __result)
        {
            var owner = _instance;
            if (owner == null || owner._readingWheel) return true;
            owner.PollWheel();
            if (!owner.WheelConsumesInput) return true;
            __result = 0;
            return false;
        }
    }

    private void RenderWheel()
    {
        PollWheel();
        if (!_wheelOpen) { if (_wheelRoot) _wheelRoot!.SetActive(false); return; }
        if (!_wheelRoot)
        {
            var message = MessageHud.instance;
            if (!message || !message.m_messageText || !message.m_messageText.font) return;
            var font = message.m_messageText;
            _wheelRoot = new GameObject("BuildHotbar_ControllerWheel", typeof(RectTransform));
            _wheelRoot.transform.SetParent(Hud.instance.m_rootObject.transform, false);
            var root = (RectTransform)_wheelRoot.transform;
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = Vector2.zero;
            _wheelViews.Clear();
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                var box = new GameObject("Direction" + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(RadialSlice));
                box.transform.SetParent(root, false);
                Position((RectTransform)box.transform, Vector2.zero, new Vector2(536, 536));
                var background = box.GetComponent<RadialSlice>();
                background.Slot = i;
                background.SetVerticesDirty();
                background.raycastTarget = false;
                Vector2 content = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * 195;
                // Position() anchors children to the sector rect's bottom centre.
                content.y += RadialSlice.OuterRadius;
                var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconObject.transform.SetParent(box.transform, false);
                Position((RectTransform)iconObject.transform, content + new Vector2(0, 10), new Vector2(48, 48));
                var icon = iconObject.GetComponent<Image>();
                icon.raycastTarget = false;
                icon.preserveAspect = true;
                var label = Text(box.transform, "Piece", font, 14, content + new Vector2(0, -30), new Vector2(104, 34));
                var key = Text(box.transform, "Slot", font, 12, content + new Vector2(0, 45), new Vector2(24, 20));
                key.gameObject.SetActive(false);
                _wheelViews.Add(new SlotView(background, icon, label, key));
            }
            _wheelTitle = Text(root, "Page", font, 21, new Vector2(0, 310), new Vector2(360, 60));

        }
        _wheelRoot!.SetActive(true);
        var wheelCanvas = (RectTransform)_wheelRoot.transform.parent;
        _wheelRoot.transform.localScale = Vector3.one * HotbarScale.Fit(_radialScale.Value,
            536, 680, wheelCanvas.rect.width - 24, wheelCanvas.rect.height - 24);
        _wheelRoot.transform.SetAsLastSibling();
        _wheelTitle!.text = (_wheelAssigning ? "SAVE BUILD PIECE" : "BUILD SHORTCUTS") + "\nRow " + CurrentRow + " / " + _rows.Value;
        var slots = Slots(_wheelTool!);
        for (int i = 0; i < 8; i++)
        {
            var view = _wheelViews[i];
            view.Background.gameObject.SetActive(i < _count.Value);
            var piece = Resolve(_wheelTool!, slots[i].Value);
            view.Background.color = HotbarFill(i == _wheelSlot);
            view.Icon.sprite = piece ? piece!.m_icon : null;
            view.Icon.enabled = view.Icon.sprite;
            view.Icon.color = piece && Player.m_localPlayer.IsPieceAvailable(piece) ? Color.white : new Color(1, 1, 1, 0.3f);
            view.Label.text = piece ? Label(piece!) : string.IsNullOrEmpty(slots[i].Value) ? "Empty" : "Unavailable";
        }
    }

    private void DestroyWheel()
    {
        if (_wheelRoot) Destroy(_wheelRoot);
        _wheelRoot = null;
        _wheelViews.Clear();
    }
}



