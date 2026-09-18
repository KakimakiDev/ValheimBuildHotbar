using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Kakimaki.BuildHotbar;

public sealed partial class BuildHotbarPlugin
{
    private readonly List<TMP_Text> _nativeControlHints = new List<TMP_Text>();
    private KeyHints? _nativeKeyHints;
    private Transform? _nativeHintParent;
    private bool _hintLayoutWarning;
    private bool _uiNavigationWaitForCentre;
    private readonly List<GameObject> _hiddenVanillaHints = new List<GameObject>();

    // Unity UI navigation reads InputActions directly, bypassing ZInput's axes.
    // Suppress dispatch rather than moving focus back after it visibly changes.
    [HarmonyPatch]
    private static class WheelUiNavigationPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(AccessTools.TypeByName("UnityEngine.InputSystem.UI.InputSystemUIInputModule"), "ProcessNavigation");
            yield return AccessTools.Method(typeof(BuildUi), "NavigationUpdate");
            yield return AccessTools.Method(typeof(BuildUi), "UpdateTriggerNavigation");
        }

        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Prefix()
        {
            var owner = _instance;
            if (owner == null) return true;
            owner.PollWheel();
            return !owner.WheelConsumesInput && !owner._uiNavigationWaitForCentre;
        }
    }

    // Same sprite markup and controller-family selection used by vanilla key hints.
    private static string Glyph(string action)
    {
        var input = ZInput.instance;
        if (input == null) return action;
        string glyph = input.GetBoundKeyString(action, true);
        return string.IsNullOrEmpty(glyph) ? action : glyph;
    }

    private static string ChordGlyphs(WheelButtons buttons)
    {
        var glyphs = new List<string>();
        for (int i = 0; i < WheelButtonActions.Length; i++)
            if (((int)buttons & (1 << i)) != 0) glyphs.Add(Glyph(WheelButtonActions[i]));
        return glyphs.Count == 0 ? "Unbound" : string.Join(" + ", glyphs);
    }

    private static void NativeHint(TMP_Text text, string caption)
    {
        text.richText = true;
        text.text = caption;
    }

    [HarmonyPatch(typeof(KeyHints), "UpdateHints")]
    private static class AppendBuildHotbarHintsPatch
    {
        // Restore before vanilla evaluates visibility so changes of tool/menu/input
        // update the real state. Postfix hides only the rows vanilla wanted visible.
        [HarmonyPrefix]
        private static void Prefix() => _instance?.RestoreVanillaControlHints();

        [HarmonyPostfix]
        private static void Postfix(KeyHints __instance)
        {
            if (_instance == null) return;
            _instance._nativeKeyHints = __instance;
            _instance.RenderControllerEntryHint();
        }
    }

    private List<string> ControllerHintLines()
    {
        if (!ShowControllerHotbar) return KeyboardHintLines();
        string pages = "Page  " + Glyph("JoyDPadLeft") + " / " + Glyph("JoyDPadRight");
        if (_wheelOpen)
        {
            if (_wheelAssigning) return new List<string> {
                "Aim / click to deselect  " + Glyph("JoyLStick"), pages,
                "Save  " + Glyph("JoyRTrigger") + "  (release)",
                "Cancel  " + Glyph("JoyButtonB")
            };
            return new List<string> {
                "Save piece  " + Glyph("JoyRTrigger"), "Clear slot  " + Glyph("JoyLTrigger"),
                pages, "Hotbar settings  " + Glyph("JoyDPadDown"),
                "Select  " + (_wheelToggle.Value ? Glyph("JoyButtonA") : ChordGlyphs(_wheelShortcut.Value) + "  (release)"),
                "Cancel  " + Glyph("JoyButtonB")
            };
        }
        if (Hud.IsPieceSelectionVisible()) return new List<string> {
            "Assign build shortcut  " + Glyph("JoyRTrigger") + "  (hold)",
            "Hotbar settings  " + Glyph("JoyStart")
        };
        return _wheelEnabled.Value ? new List<string> {
            "Build shortcuts  " + ChordGlyphs(_wheelShortcut.Value) + "  (hold)"
        } : new List<string>();
    }

    private void RenderControllerEntryHint()
    {
        var hints = _nativeKeyHints;
        bool visible = hints && hints!.m_buildHints && hints.m_buildHints.activeInHierarchy &&
            Eligible(Player.m_localPlayer) && !Hud.IsUserHidden() && !SettingsOpen;
        if (!visible)
        {
            RestoreVanillaControlHints();
            foreach (var row in _nativeControlHints) if (row) row.gameObject.SetActive(false);
            if (hints && hints!.m_buildHints && hints.m_buildHints!.activeInHierarchy && Eligible(Player.m_localPlayer) && Hud.IsPieceSelectionVisible() && !SettingsOpen)
                HidePlacementHintsInMenu(hints);
            return;
        }
        TMP_Text template = hints!.m_buildMenuKey;
        if (!ShowControllerHotbar)
        {
            foreach (var candidate in hints!.m_buildHints!.GetComponentsInChildren<TMP_Text>())
                if (!_nativeControlHints.Contains(candidate)) { template = candidate; break; }
        }
        if (!template) return;
        // Follow the actual native hint hierarchy rather than inventing screen coordinates.
        Transform? layout = null;
        for (Transform current = template.transform.parent; current && current!.IsChildOf(hints!.m_buildHints!.transform); current = current.parent)
        {
            if (current.GetComponent<LayoutGroup>()) { layout = current; break; }
        }
        if (!layout)
        {
            if (!_hintLayoutWarning) Logger.LogWarning("Build Hotbar: native build-hint layout was not found; no floating hints will be drawn.");
            _hintLayoutWarning = true;
            return;
        }
        if (_nativeHintParent != layout || (_nativeControlHints.Count > 0 && !_nativeControlHints[0]))
        {
            DestroyNativeControlHints();
            _nativeHintParent = layout;
        }
        var lines = ControllerHintLines();
        while (_nativeControlHints.Count < lines.Count)
        {
            var row = Text(layout!, "BuildHotbarNativeHint" + _nativeControlHints.Count, template,
                Mathf.RoundToInt(template.fontSize), Vector2.zero, template.rectTransform.sizeDelta);
            row.alignment = template.alignment;
            row.color = template.color;
            row.fontStyle = template.fontStyle;
            row.spriteAsset = template.spriteAsset;
            row.margin = template.margin;
            row.textWrappingMode = template.textWrappingMode;
            row.overflowMode = template.overflowMode;
            row.rectTransform.localScale = template.rectTransform.localScale;
            var element = row.gameObject.AddComponent<LayoutElement>();
            var originalElement = template.GetComponent<LayoutElement>();
            if (originalElement)
            {
                element.minWidth = originalElement.minWidth;
                element.minHeight = originalElement.minHeight;
                element.preferredWidth = originalElement.preferredWidth;
                element.preferredHeight = originalElement.preferredHeight;
                element.flexibleWidth = originalElement.flexibleWidth;
                element.flexibleHeight = originalElement.flexibleHeight;
            }
            else element.minHeight = Mathf.Max(template.rectTransform.rect.height, template.fontSize + 4);
            _nativeControlHints.Add(row);
        }
        bool changed = false;
        for (int i = 0; i < _nativeControlHints.Count; i++)
        {
            var row = _nativeControlHints[i];
            bool active = i < lines.Count;
            if (row.gameObject.activeSelf != active) { row.gameObject.SetActive(active); changed = true; }
            if (active && row.text != lines[i]) { NativeHint(row, lines[i]); changed = true; }
        }
        if (changed) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)layout!);
        if (_wheelOpen) HideVanillaControlHints(hints!.m_buildHints!.transform);
        else if (Hud.IsPieceSelectionVisible()) HidePlacementHintsInMenu(hints!);
        else RestoreVanillaControlHints();
    }

    private void HidePlacementHintsInMenu(KeyHints hints)
    {
        // Vanilla explicitly identifies the rows relevant only to its build menu.
        // Keep those and our added rows; the remaining building actions are for placement.
        var keep = new List<Transform>();
        foreach (var row in hints.m_buildMenuHintsGP) if (row) keep.Add(row.transform);
        foreach (var row in hints.m_buildMenuHintsKB) if (row) keep.Add(row.transform);
        foreach (var row in _nativeControlHints) if (row) keep.Add(row.transform);
        FilterBuildHintBranches(hints.m_buildHints.transform, keep);
    }

    private void FilterBuildHintBranches(Transform parent, List<Transform> keep)
    {
        foreach (Transform child in parent)
        {
            if (keep.Contains(child)) continue;
            if (keep.Exists(row => row.IsChildOf(child))) FilterBuildHintBranches(child, keep);
            else HideVanillaHint(child.gameObject);
        }
    }

    private void HideVanillaControlHints(Transform buildHints)
    {
        if (!_nativeHintParent) return;
        // Keep the native layout and our rows alive; hide the other native branches.
        // Walk up too, since some vanilla hint rows can occupy neighbouring groups.
        Transform branch = _nativeHintParent!;
        foreach (Transform child in branch)
        {
            bool ours = _nativeControlHints.Exists(row => row && row.transform == child);
            if (!ours) HideVanillaHint(child.gameObject);
        }
        while (branch != buildHints && branch.parent && branch.IsChildOf(buildHints))
        {
            Transform parent = branch.parent;
            foreach (Transform sibling in parent)
                if (sibling != branch) HideVanillaHint(sibling.gameObject);
            branch = parent;
        }
        if (_nativeHintParent is RectTransform rect) LayoutRebuilder.MarkLayoutForRebuild(rect);
    }

    private void HideVanillaHint(GameObject hint)
    {
        if (!hint.activeSelf) return;
        if (!_hiddenVanillaHints.Contains(hint)) _hiddenVanillaHints.Add(hint);
        hint.SetActive(false);
    }

    private void RestoreVanillaControlHints()
    {
        if (_hiddenVanillaHints.Count == 0) return;
        foreach (var hint in _hiddenVanillaHints) if (hint) hint.SetActive(true);
        _hiddenVanillaHints.Clear();
        if (_nativeHintParent is RectTransform rect && rect) LayoutRebuilder.MarkLayoutForRebuild(rect);
    }

    private void DestroyNativeControlHints()
    {
        RestoreVanillaControlHints();
        foreach (var row in _nativeControlHints)
            if (row) { row.gameObject.SetActive(false); Destroy(row.gameObject); }
        _nativeControlHints.Clear();
        _nativeHintParent = null;
    }
}



