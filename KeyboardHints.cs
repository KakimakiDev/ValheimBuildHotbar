using System.Collections.Generic;
using UnityEngine;

namespace Kakimaki.BuildHotbar;

public sealed partial class BuildHotbarPlugin
{
    private static string KeyboardKeyHint(KeyCode key) => "<color=#FFD580>[" + KeyName(key) + "]</color>";

    private List<string> KeyboardHintLines()
    {
        var lines = new List<string>();
        if (_toggleKey.Value != KeyCode.None)
            lines.Add((_hotbarActive ? "Disable build shortcuts  " : "Enable build shortcuts  ") + KeyboardKeyHint(_toggleKey.Value));
        if (!_hotbarActive) return lines;
        var keys = new List<string>();
        for (int i = 0; i < _count.Value; i++)
            if (_slotKeys[i].Value != KeyCode.None) keys.Add(KeyName(_slotKeys[i].Value));
        if (keys.Count > 0)
        {
            lines.Add("Select build shortcut  <color=#FFD580>[" + string.Join(" / ", keys) + "]</color>");
            if (_saveKey.Value != KeyCode.None) lines.Add("Save piece  " + KeyboardKeyHint(_saveKey.Value) + " + slot key");
            if (_clearKey.Value != KeyCode.None) lines.Add("Clear slot  " + KeyboardKeyHint(_clearKey.Value) + " + slot key");
        }
        if (_rows.Value > 1 && _scrollKey.Value != KeyCode.None)
            lines.Add("Hotbar page  " + KeyboardKeyHint(_scrollKey.Value) + " + mouse wheel");
        return lines;
    }
}
