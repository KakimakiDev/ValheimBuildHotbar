using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace Kakimaki.BuildHotbar;

public sealed partial class BuildHotbarPlugin
{
    private ConfigEntry<float> _horizontalScale = null!, _radialScale = null!;
    private TMP_Text? _horizontalScaleValue, _radialScaleValue;

    private void InitializeScale()
    {
        _horizontalScale = Config.Bind("Display", "Horizontal hotbar scale", 1f,
            new ConfigDescription("Horizontal hotbar size. 1 = 100%. Automatically fits within the screen.", new AcceptableValueRange<float>(0.5f, 2f)));
        _radialScale = Config.Bind("Display", "Radial menu scale", 1f,
            new ConfigDescription("Radial menu size including icons and labels. 1 = 100%. Automatically fits within the screen.", new AcceptableValueRange<float>(0.5f, 2f)));
    }

    private void ChangeScale(bool radial, int direction)
    {
        var setting = radial ? _radialScale : _horizontalScale;
        setting.Value = HotbarScale.Step(setting.Value, direction);
        _nextRefresh = 0;
        RefreshSettings();
        RefreshControllerSettings();
    }

    private static string ScaleLabel(float value) => Mathf.RoundToInt(value * 100) + "%";

    private void AddScaleSettings(Transform panel, TMP_Text font)
    {
        // Make room below keyboard bindings, above the existing Done/reset buttons.
        foreach (RectTransform child in panel)
            if (child.anchoredPosition.y >= 100) child.anchoredPosition += new Vector2(0, 88);
        _horizontalScaleValue = SettingsRow(panel, font, "Horizontal hotbar size", 172);
        _radialScaleValue = SettingsRow(panel, font, "Radial menu size", 128);
        foreach (bool radial in new[] { false, true })
        {
            bool target = radial;
            float y = radial ? 128 : 172;
            MakeButton(panel, "−", font, new Vector2(66, y), new Vector2(36, 32), () => ChangeScale(target, -1));
            MakeButton(panel, "+", font, new Vector2(202, y), new Vector2(36, 32), () => ChangeScale(target, 1));
        }
    }
}
