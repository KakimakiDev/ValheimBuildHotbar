using System;
using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Kakimaki.BuildHotbar;

[BepInPlugin(Guid, "Build Hotbar", "0.9.4")]
public sealed partial class BuildHotbarPlugin : BaseUnityPlugin
{
    public const string Guid = "ValheimEnthusiestKakimaki.BuildHotbar";
    private static BuildHotbarPlugin? _instance;
    private readonly Harmony _harmony = new Harmony(Guid);
    private readonly Dictionary<string, ConfigEntry<string>[]> _toolSlots = new Dictionary<string, ConfigEntry<string>[]>();
    private ConfigEntry<bool> _enabled = null!;
    private ConfigEntry<bool> _autoActivate = null!;
    private ConfigEntry<KeyCode> _toggleKey = null!;
    private PieceTable? _activationTool;
    private bool _hotbarActive;
    private int _toggleFrame = -1;
    private GameObject? _toggleButton;
    private TMP_Text? _toggleText;
    private ConfigEntry<int> _count = null!;
    private ConfigEntry<int> _rows = null!;
    private ConfigEntry<int> _activeRow = null!;
    private ConfigEntry<bool> _reverseScroll = null!;
    private int _lastScrollFrame = -1;
    private static readonly System.Reflection.MethodInfo TakeInput = AccessTools.Method(typeof(Player), "TakeInput");
    private ConfigEntry<Vector2> _position = null!;
    private static readonly AccessTools.FieldRef<Hud, Piece> HoveredPiece = AccessTools.FieldRefAccess<Hud, Piece>("m_hoveredPiece");
    private Image? _dragImage;
    private TMP_Text? _dragLabel;
    private bool _dragging;
    private Vector2 _dragStartPointer;
    private Vector2 _dragStartPosition;
    private GameObject? _panel;
    private RectTransform? _panelRect;
    private TMP_Text? _hint;
    private readonly List<SlotView> _views = new List<SlotView>();
    private float _nextRefresh;
    private static readonly Color Background = new Color(0.09f, 0.08f, 0.06f, 0.94f);
    private static readonly Color Selected = new Color(0.42f, 0.29f, 0.10f, 0.98f);

    private void Awake()
    {
        _instance = this;
        _enabled = Config.Bind("General", "Enabled", true, "Show and use the building hotbar while a building tool is equipped.");
        _autoActivate = Config.Bind("General", "Auto activate", true, "Activate when equipping a building tool. Otherwise use the toggle key or Enable button. Putting the tool away resets activation.");
        _toggleKey = Config.Bind("Key bindings", "Toggle key", KeyCode.F6, "Toggle between build and inventory hotbar shortcuts while holding a building tool.");
        _count = Config.Bind("General", "Slot count", 8,
            new ConfigDescription("Slots per hotbar row. Remaining number keys use the inventory hotbar.", new AcceptableValueRange<int>(1, 8)));
        _rows = Config.Bind("General", "Row count", 3,
            new ConfigDescription("Number of saved hotbar rows. One row is displayed at a time; modifier+wheel switches rows. Reducing this does not erase hidden rows.", new AcceptableValueRange<int>(1, 12)));
        _activeRow = Config.Bind("General", "Active row", 1,
            new ConfigDescription("Current row, also updated by scrolling.", new AcceptableValueRange<int>(1, 12)));
        _reverseScroll = Config.Bind("Controls", "Reverse row scrolling", false, "Reverse the direction used to cycle rows. Normally wheel down selects the next row.");
        _position = Config.Bind("Display", "Position", new Vector2(0, -24),
            "Offset from top centre in canvas units. Drag the title while the build menu is open. Reset to (0, -24) for the default position.");
        InitializeBindings();
        InitializeScale();
        InitializeWheel();
        _harmony.PatchAll(typeof(BuildHotbarPlugin).Assembly);
        Logger.LogInfo("Build hotbar ready: number selects, Shift+number saves, Alt+number clears.");
    }

    private int CurrentRow => Mathf.Clamp(_activeRow.Value, 1, _rows.Value);

    private bool ScrollKeyHeld()
    {
        return Held(_scrollKey.Value);
    }

    private void Update()
    {
        RefreshActivation(Player.m_localPlayer);
        PollWheel();
        if (WheelConsumesInput) return;
        if (SettingsOpen) { CaptureBinding(); return; }
        if (Active(Player.m_localPlayer) && (bool)TakeInput.Invoke(Player.m_localPlayer, null))
            for (int i = 0; i < _count.Value; i++)
                if (_slotKeys[i].Value != KeyCode.None && Input.GetKeyDown(_slotKeys[i].Value))
                    TriggerSlot(Player.m_localPlayer, i);
        // Poll the same API as placement. The postfix also handles earlier game calls.
        if (Active(Player.m_localPlayer) && _rows.Value > 1 && ScrollKeyHeld())
            ZInput.GetMouseScrollWheel();
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    private static class RowScrollPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            var owner = _instance;
            Player player = Player.m_localPlayer;
            if (owner == null || __result == 0 || !Active(player) || owner._rows.Value <= 1 ||
                !owner.ScrollKeyHeld() || !(bool)TakeInput.Invoke(player, null)) return;
            float wheel = __result;
            // Consume the wheel so changing rows doesn't also rotate the building piece or zoom.
            __result = 0;
            if (owner._lastScrollFrame == Time.frameCount) return;
            owner._lastScrollFrame = Time.frameCount;
            int direction = wheel < 0 ? 1 : -1;
            if (owner._reverseScroll.Value) direction = -direction;
            owner._activeRow.Value = HotbarRows.Step(owner.CurrentRow, owner._rows.Value, direction);
            owner._nextRefresh = 0;
        }
    }

    private static bool Eligible(Player player)
    {
        return _instance != null && _instance._enabled.Value && player &&
               player == Player.m_localPlayer && !player.IsDead() && !player.InCutscene() &&
               !player.IsTeleporting() && player.GetBuildTool();
    }

    private void RefreshActivation(Player player)
    {
        PieceTable? tool = Eligible(player) ? player.GetBuildTool() : null;
        if (_activationTool != tool)
        {
            _activationTool = tool;
            _hotbarActive = tool && _autoActivate.Value;
        }
        if (!tool) { _hotbarActive = false; return; }
        // Every input hook uses this path, so script update order cannot toggle twice.
        if (!SettingsOpen && _toggleFrame != Time.frameCount && _toggleKey.Value != KeyCode.None &&
            Input.GetKeyDown(_toggleKey.Value) && (bool)TakeInput.Invoke(player, null))
        {
            _toggleFrame = Time.frameCount;
            ToggleActivation();
        }
    }

    private void ToggleActivation()
    {
        _hotbarActive = !_hotbarActive;
        EndDrag();
        _nextRefresh = 0;
        if (_panel) ApplyCollapsed();
    }

    private static bool Active(Player player)
    {
        if (_instance == null) return false;
        _instance.RefreshActivation(player);
        return Eligible(player) && _instance._hotbarActive;
    }

    // Vanilla input and other mods can call UseHotbarItem.
    // Consume bound keyboard shortcuts before they also change equipped inventory items.
    [HarmonyPatch(typeof(Player), nameof(Player.UseHotbarItem))]
    private static class UseHotbarItemPatch
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix(Player __instance, int index)
        {
            if (!Eligible(__instance) || index < 1 || index > 8) return true;
            if (_instance!.SettingsOpen) return false;
            if (!Active(__instance)) return true;
            if (!(bool)TakeInput.Invoke(__instance, null)) return true;
            // Controller hotbar actions and programmatic item use retain vanilla behavior.
            KeyCode number = KeyCode.Alpha1 + index - 1;
            if (!Input.GetKeyDown(number)) return true;
            for (int i = 0; i < _instance._count.Value; i++)
                if (_instance._slotKeys[i].Value == number)
                {
                    _instance.TriggerSlot(__instance, i);
                    return false;
                }
            return true;
        }
    }

    private ConfigEntry<string>[] Slots(PieceTable tool)
    {
        // A prefab name survives game restarts and custom tools can have independent bars.
        string key = tool.name;
        int row = CurrentRow;
        string cacheKey = key + "|" + row;
        if (_toolSlots.TryGetValue(cacheKey, out var slots)) return slots;
        string section = "Tool " + BitConverter.ToString(Encoding.UTF8.GetBytes(key)).Replace("-", "");
        // Store every row in its own section.
        section += " Row " + row;
        slots = new ConfigEntry<string>[8];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = Config.Bind(section, "Slot " + (i + 1), "", "Saved piece prefab name for " + key + ". Set in game with Shift+number.");
        _toolSlots.Add(cacheKey, slots);
        return slots;
    }

    private static Piece? Resolve(PieceTable tool, string prefab)
    {
        if (string.IsNullOrEmpty(prefab)) return null;
        foreach (GameObject candidate in tool.m_pieces)
            if (candidate && candidate.name == prefab) return candidate.GetComponent<Piece>();
        return null;
    }

    private void HandleSlot(Player player, int slot)
    {
        PieceTable tool = player.GetBuildTool();
        var entry = Slots(tool)[slot];
        bool clear = Held(_clearKey.Value);
        bool assign = Held(_saveKey.Value);
        if (clear)
        {
            entry.Value = "";
            Notify(player, "Row " + CurrentRow + ", slot " + (slot + 1) + " cleared.");
        }
        else if (assign)
        {
            // The HUD clears this field on pointer exit. Ignore it when its menu is closed.
            Piece piece = Hud.instance && Hud.IsPieceSelectionVisible() ? HoveredPiece(Hud.instance) : null!;
            if (!piece) piece = player.GetSelectedPiece();
            if (!piece || !player.IsPieceAvailable(piece))
            {
                Notify(player, "Hover over or select an available building piece first.");
                return;
            }
            entry.Value = piece.gameObject.name;
            Notify(player, "Row " + CurrentRow + ", slot " + (slot + 1) + ": " + Label(piece));
        }
        else
        {
            Piece? piece = Resolve(tool, entry.Value);
            if (string.IsNullOrEmpty(entry.Value))
                Notify(player, "Empty slot. Hover over or select a piece, then use " + KeyName(_saveKey.Value) + "+" + KeyName(_slotKeys[slot].Value) + " to save it.");
            else if (!piece || !player.IsPieceAvailable(piece) || !player.SetSelectedPiece(piece))
                Notify(player, "That piece is unavailable with this tool or is not unlocked.");
        }
        _nextRefresh = 0;
    }

    private static string Label(Piece piece) => Localization.instance.Localize(piece.m_name);
    private static void Notify(Player player, string message) => player.Message(MessageHud.MessageType.TopLeft, message, 0, null, false);

    private void LateUpdate()
    {
        RenderWheel();
        RenderControllerEntryHint();
        Player player = Player.m_localPlayer;
        Hud hud = Hud.instance;
        RefreshActivation(player);
        if (!Eligible(player) || !hud || !hud.m_rootObject || !hud.m_rootObject.activeInHierarchy || Hud.IsUserHidden())
        {
            CloseSettings();
            EndDrag();
            if (_panel) _panel!.SetActive(false);
            return;
        }
        if (!_panel || _views.Count != _count.Value)
        {
            DestroyPanel();
            if (!CreatePanel(hud)) return;
        }
        _panel!.SetActive(!ShowControllerHotbar);
        ApplyCollapsed();
        var hotbarCanvas = (RectTransform)_panelRect!.parent;
        _panelRect.localScale = Vector3.one * HotbarScale.Fit(_horizontalScale.Value,
            _panelRect.rect.width, _panelRect.rect.height, hotbarCanvas.rect.width - 24, hotbarCanvas.rect.height - 24);
        bool menuOpen = Hud.IsPieceSelectionVisible();
        if (!menuOpen && !ControllerSettingsOpen) CloseSettings();
        if (_settingsButton) _settingsButton!.SetActive(menuOpen);
        if (_collapseButton) _collapseButton!.SetActive(menuOpen);
        if (_toggleButton) _toggleButton!.SetActive(menuOpen);
        if (!menuOpen) EndDrag();
        _dragImage!.raycastTarget = menuOpen;
        string rowTitle = "BUILD HOTBAR   " + (_hotbarActive ? CurrentRow + " / " + _rows.Value : "OFF");
        _dragLabel!.text = menuOpen ? rowTitle + "   ·   drag" : rowTitle;
        _dragLabel.rectTransform.anchoredPosition = new Vector2(menuOpen ? -104 : 0, 14);
        _dragLabel.rectTransform.sizeDelta = new Vector2(_panelRect!.sizeDelta.x - (menuOpen ? 208 : 16), 28);
        _dragLabel.alignment = menuOpen ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
        if (menuOpen) _panel.transform.SetAsLastSibling();
        if (_settingsRoot) { _settingsRoot!.transform.SetAsLastSibling(); RefreshSettings(); }
        if (ControllerSettingsOpen) _controllerSettingsRoot!.transform.SetAsLastSibling();
        if (!_dragging) _panelRect!.anchoredPosition = ClampPosition(_position.Value);
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.1f;
        PieceTable tool = player.GetBuildTool();
        var slots = Slots(tool);
        Piece current = player.GetSelectedPiece();
        for (int i = 0; i < _views.Count; i++)
        {
            var view = _views[i];
            view.Key.text = KeyName(_slotKeys[i].Value);
            Piece? piece = Resolve(tool, slots[i].Value);
            bool available = piece && player.IsPieceAvailable(piece);
            bool selected = piece && piece == current;
            view.Background.color = HotbarFill(selected);
            view.Icon.sprite = piece ? piece!.m_icon : null;
            view.Icon.enabled = view.Icon.sprite != null;
            view.Icon.color = available ? Color.white : new Color(1, 1, 1, 0.3f);
            view.Label.text = piece ? Label(piece!) : string.IsNullOrEmpty(slots[i].Value) ? "Empty" : "Missing piece";
            view.Label.color = available ? Color.white : new Color(0.7f, 0.7f, 0.7f);
        }
        _hint!.text = KeyName(_saveKey.Value) + "+slot save  •  " + KeyName(_clearKey.Value) + "+slot clear" +
            (_rows.Value > 1 && _scrollKey.Value != KeyCode.None ? "  •  " + KeyName(_scrollKey.Value) + "+wheel row" : "");
    }

    private bool CreatePanel(Hud hud)
    {
        MessageHud messageHud = MessageHud.instance;
        if (!messageHud || !messageHud.m_messageText || !messageHud.m_messageText.font) return false;
        TMP_Text font = messageHud.m_messageText;
        _panel = new GameObject("Kakimaki_BuildHotbar", typeof(RectTransform));
        _panel.transform.SetParent(hud.m_rootObject.transform, false);
        _panelRect = (RectTransform)_panel.transform;
        _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 1);
        _panelRect.pivot = new Vector2(0.5f, 1);
        float width = _count.Value * 88f;
        _panelRect.sizeDelta = new Vector2(Math.Max(width, 480), 126);
        for (int i = 0; i < _count.Value; i++)
        {
            var box = new GameObject("Slot" + (i + 1), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            box.transform.SetParent(_panel.transform, false);
            Position((RectTransform)box.transform, new Vector2(-width / 2 + 44 + i * 88, 47), new Vector2(80, 94));
            var background = box.GetComponent<Image>();
            background.raycastTarget = false;
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObject.transform.SetParent(box.transform, false);
            Position((RectTransform)iconObject.transform, new Vector2(0, 52), new Vector2(52, 52));
            var icon = iconObject.GetComponent<Image>();
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var key = Text(box.transform, "Key", font, 11, new Vector2(0, 86), new Vector2(74, 16));
            key.text = KeyName(_slotKeys[i].Value);
            key.color = new Color(1, 0.8f, 0.4f);
            var label = Text(box.transform, "Name", font, 12, new Vector2(0, 14), new Vector2(72, 24));
            _views.Add(new SlotView(background, icon, label, key));
        }
        _hint = Text(_panel.transform, "Instructions", font, 13, new Vector2(0, 114), new Vector2(Math.Max(width, 480), 24));
        var handle = new GameObject("DragHandle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        handle.transform.SetParent(_panel.transform, false);
        Position((RectTransform)handle.transform, new Vector2(0, 112), new Vector2(_panelRect.sizeDelta.x, 28));
        _dragImage = handle.GetComponent<Image>();
        _dragImage.color = HotbarFill(false);
        _dragImage.raycastTarget = false;
        handle.AddComponent<HotbarDragHandle>().Owner = this;
        _dragLabel = Text(handle.transform, "Title", font, 14, new Vector2(-104, 14), new Vector2(_panelRect.sizeDelta.x - 208, 28));
        _handleRect = (RectTransform)handle.transform;
        var toggle = MakeButton(_panel.transform, "Enable", font,
            new Vector2(_panelRect.sizeDelta.x / 2 - 170, 112), new Vector2(64, 28), ToggleActivation);
        _toggleButton = toggle.gameObject;
        _toggleText = toggle.GetComponentInChildren<TMP_Text>();
        _toggleButton.SetActive(false);
        var settingsButton = MakeButton(_panel.transform, "Settings", font,
            new Vector2(_panelRect.sizeDelta.x / 2 - 94, 112), new Vector2(84, 28), OpenSettings);
        _settingsButton = settingsButton.gameObject;
        _settingsButton.SetActive(false);
        var collapse = MakeButton(_panel.transform, "−", font,
            new Vector2(_panelRect.sizeDelta.x / 2 - 24, 112), new Vector2(44, 28), () =>
            {
                EndDrag();
                _collapsed.Value = !_collapsed.Value;
                ApplyCollapsed();
            });
        _collapseButton = collapse.gameObject;
        _collapseText = collapse.GetComponentInChildren<TMP_Text>();
        _collapseButton.SetActive(false);
        ApplyCollapsed();
        _nextRefresh = 0;
        return true;
    }

    private static void Position(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static TMP_Text Text(Transform parent, string name, TMP_Text source, int size, Vector2 position, Vector2 dimensions)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        // Assign the game's font before TextMeshPro runs its initial font lookup.
        obj.SetActive(false);
        obj.transform.SetParent(parent, false);
        var text = obj.AddComponent<TextMeshProUGUI>();
        text.font = source.font;
        text.fontSharedMaterial = source.fontSharedMaterial;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.richText = false;
        Position((RectTransform)obj.transform, position, dimensions);
        obj.SetActive(true);
        return text;
    }

    private void DestroyPanel()
    {
        EndDrag();
        if (_panel) Destroy(_panel);
        _panel = null;
        _panelRect = null;
        _hint = null;
        _dragImage = null;
        _dragLabel = null;
        _settingsButton = null;
        _collapseButton = null;
        _collapseText = null;
        _toggleButton = null;
        _toggleText = null;
        _handleRect = null;
        _views.Clear();
    }

    private Vector2 ClampPosition(Vector2 position)
    {
        if (!_panelRect || !(_panelRect!.parent is RectTransform parent)) return position;
        float halfRange = Mathf.Max(0, (parent.rect.width - _panelRect.rect.width * _panelRect.localScale.x) / 2);
        float minY = Mathf.Min(0, _panelRect.rect.height * _panelRect.localScale.y - parent.rect.height);
        return new Vector2(Mathf.Clamp(position.x, -halfRange, halfRange), Mathf.Clamp(position.y, minY, 0));
    }

    internal void BeginDrag(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || !Hud.IsPieceSelectionVisible() || !_panelRect) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_panelRect!.parent,
                data.position, data.pressEventCamera, out _dragStartPointer)) return;
        _dragStartPosition = _panelRect.anchoredPosition;
        _dragging = true;
    }

    internal void Drag(PointerEventData data)
    {
        if (!_dragging || !_panelRect) return;
        if (!Hud.IsPieceSelectionVisible()) { EndDrag(); return; }
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_panelRect!.parent,
                data.position, data.pressEventCamera, out Vector2 pointer))
            _panelRect.anchoredPosition = ClampPosition(_dragStartPosition + pointer - _dragStartPointer);
    }

    internal void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        if (_panelRect) _position.Value = ClampPosition(_panelRect!.anchoredPosition);
    }

    private void OnDestroy()
    {
        DestroyNativeControlHints();
        DestroyWheel();
        _harmony.UnpatchSelf();
        CloseSettings();
        DestroyPanel();
        if (_instance == this) _instance = null;
    }

    private sealed class SlotView
    {
        public readonly Image Background;
        public readonly Image Icon;
        public readonly TMP_Text Label;
        public readonly TMP_Text Key;
        public SlotView(Image background, Image icon, TMP_Text label, TMP_Text key) { Background = background; Icon = icon; Label = label; Key = key; }
    }
}

public sealed class HotbarDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    internal BuildHotbarPlugin? Owner;
    public void OnBeginDrag(PointerEventData data) => Owner?.BeginDrag(data);
    public void OnDrag(PointerEventData data) => Owner?.Drag(data);
    public void OnEndDrag(PointerEventData data) => Owner?.EndDrag();
    private void OnDisable() => Owner?.EndDrag();
}






