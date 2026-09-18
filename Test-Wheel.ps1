$ErrorActionPreference = 'Stop'
# Compile the actual pure selection/paging code without loading Unity or the game.
$sources = @('using System;')
foreach ($name in @('WheelSelection', 'HotbarRows', 'WheelGesture', 'WheelPaging', 'ControllerChordCapture', 'HotbarScale')) {
    $source = Get-Content -LiteralPath (Join-Path $PSScriptRoot "./$name.cs") -Raw
    $source = $source.Replace('using System;', '').Replace('namespace Kakimaki.BuildHotbar;', 'namespace Kakimaki.BuildHotbar {').Replace('internal static class', 'public static class').Replace('internal sealed class', 'public sealed class') + "`n}"
    $sources += $source
}
Add-Type -TypeDefinition ($sources -join "`n")
$script:checks = 0
function Assert-Equal($actual, $expected, $label) {
    $script:checks++
    if ($actual -ne $expected) { throw "$label : expected $expected, got $actual" }
}
for ($slot = 0; $slot -lt 8; $slot++) {
    foreach ($offset in @(-21, 0, 21)) {
        $angle = ($slot * 45 + $offset) * [Math]::PI / 180
        Assert-Equal ([Kakimaki.BuildHotbar.WheelSelection]::Slot([Math]::Sin($angle), [Math]::Cos($angle), 0.35, 8)) $slot "Direction $slot at offset $offset"
    }
}
Assert-Equal ([Kakimaki.BuildHotbar.WheelSelection]::Slot(0, 0, 0.35, 8)) -1 'Centre cancels'
Assert-Equal ([Kakimaki.BuildHotbar.WheelSelection]::Slot(0.1, 0.1, 0.35, 8)) -1 'Drift cancels'
Assert-Equal ([Kakimaki.BuildHotbar.WheelSelection]::Slot(0, 0.35, 0.35, 8)) -1 'Deadzone boundary cancels'
Assert-Equal ([Kakimaki.BuildHotbar.WheelSelection]::Slot(-1, 0, 0.35, 4)) -1 'Hidden direction cancels'
Assert-Equal ([Kakimaki.BuildHotbar.WheelSelection]::Slot(1, 0, 0.35, 4)) 2 'Visible directions stay fixed'
for ($count = 1; $count -le 12; $count++) {
    Assert-Equal ([Kakimaki.BuildHotbar.HotbarRows]::Step($count, $count, 1)) 1 'Next wraps'
    Assert-Equal ([Kakimaki.BuildHotbar.HotbarRows]::Step(1, $count, -1)) $count 'Previous wraps'
    Assert-Equal ([Kakimaki.BuildHotbar.HotbarRows]::Step(12, $count, 1)) 1 'Reduced row count clamps'
}
$gesture = New-Object Kakimaki.BuildHotbar.WheelGesture
$gesture.Update(1, 0, 0.35, 8)
Assert-Equal $gesture.Selected 2 'Existing walking direction highlights immediately'
$gesture.Update(0, 0, 0.35, 8)
$gesture.Update(1, 0, 0.35, 8)
Assert-Equal $gesture.Selected 2 'Right highlighted'
$gesture.Update(0.2, 0, 0.35, 8)
Assert-Equal $gesture.Selected 2 'Highlight retained while releasing stick'
$gesture.Update(0, 0, 0.35, 8)
Assert-Equal $gesture.Selected 2 'Neutral retains highlight for Square release'
$gesture.Update(0.08, -0.1, 0.35, 8)
Assert-Equal $gesture.Selected 2 'Drift cannot clear highlight'
$gesture.Update(0, 1, 0.35, 8)
Assert-Equal $gesture.Selected 0 'New direction changes highlight'
$gesture.Reset()
Assert-Equal $gesture.Selected -1 'Page change clears stale selection'
$gesture.Update(0, -1, 0.35, 8)
Assert-Equal $gesture.Selected 4 'Held direction selects immediately on new page'
$gesture.Reset()
$gesture.Update(0, 0, 0.35, 4)
$gesture.Update(-1, 0, 0.35, 4)
Assert-Equal $gesture.Selected -1 'Hidden direction cannot select'
$paging = New-Object Kakimaki.BuildHotbar.WheelPaging
$paging.Reset($false, $false)
Assert-Equal ($paging.Update($false, $true, 0)) 1 'First right press advances once'
foreach ($time in @(0.016, 0.033, 0.3, 1, 3)) {
    Assert-Equal ($paging.Update($false, $true, $time)) 0 'Holding never repeats'
}
$null = $paging.Update($false, $false, 3.01)
Assert-Equal ($paging.Update($false, $true, 3.02)) 1 'Released then pressed advances'
$null = $paging.Update($false, $false, 3.03)
Assert-Equal ($paging.Update($false, $true, 3.04)) 0 'Brief release bounce is ignored'
Assert-Equal ($paging.Update($false, $true, 3.5)) 0 'Ignored bounce cannot repeat later while held'
$null = $paging.Update($false, $false, 3.6)
Assert-Equal ($paging.Update($true, $false, 3.7)) -1 'New left press goes back'
Assert-Equal ($paging.Update($false, $true, 3.9)) 0 'Direction crossover without release ignored'
$paging.Reset($true, $false)
Assert-Equal ($paging.Update($true, $false, 4)) 0 'Preheld D-pad on opening ignored'
$null = $paging.Update($false, $false, 4.1)
Assert-Equal ($paging.Update($true, $true, 4.2)) 0 'Opposing directions ignored'
Assert-Equal ($paging.Update($false, $true, 4.3)) 0 'Opposing direction release cannot create phantom press'

$capture = New-Object Kakimaki.BuildHotbar.ControllerChordCapture
$capture.Reset()
Assert-Equal ($capture.Sample(1)) $false 'Activation Cross is ignored'
Assert-Equal $capture.Buttons 0 'Opening input excluded'
Assert-Equal ($capture.Sample(0)) $false 'Neutral arms recording'
Assert-Equal ($capture.Sample(16)) $false 'Modifier alone waits'
Assert-Equal ($capture.Sample(20)) $false 'Full combination waits for release'
Assert-Equal ($capture.Sample(4)) $false 'Partial release does not finish'
Assert-Equal ($capture.Sample(0)) $true 'Full release saves combination'
Assert-Equal $capture.Buttons 20 'Modifier and Square captured together'
$capture.Reset()
Assert-Equal $capture.Buttons 0 'New capture discards old combination'

$gesture.Reset()
$gesture.Update(1, 0, 0.35, 8)
$gesture.ClearHighlight()
Assert-Equal $gesture.Selected -1 'Stick click clears assignment target'
$gesture.Update(1, 0, 0.35, 8)
Assert-Equal $gesture.Selected -1 'Tilted stick cannot immediately reselect after cancel'
$gesture.Update(0, 1, 0.35, 8)
Assert-Equal $gesture.Selected -1 'Another tilted direction stays cancelled until neutral'
$gesture.Update(0, 0, 0.35, 8)
Assert-Equal $gesture.Selected -1 'Neutral keeps target empty for trigger release'
$gesture.Update(-1, 0, 0.35, 8)
Assert-Equal $gesture.Selected 6 'Aim after neutral chooses a fresh target'
$gesture.ClearHighlight()
$gesture.Reset()
$gesture.Update(0, 1, 0.35, 8)
Assert-Equal $gesture.Selected 0 'Reopening restores immediate aiming'

Assert-Equal ([Kakimaki.BuildHotbar.HotbarScale]::Step(1, 1)) ([single]1.05) 'Scale increases in five-percent steps'
Assert-Equal ([Kakimaki.BuildHotbar.HotbarScale]::Step(1, -1)) ([single]0.95) 'Scale decreases in five-percent steps'
Assert-Equal ([Kakimaki.BuildHotbar.HotbarScale]::Step(0.5, -1)) ([single]0.5) 'Scale minimum'
Assert-Equal ([Kakimaki.BuildHotbar.HotbarScale]::Step(2, 1)) ([single]2) 'Scale maximum'
Assert-Equal ([Kakimaki.BuildHotbar.HotbarScale]::Fit(1, 536, 680, 1920, 1080)) ([single]1) 'Normal size retained'
Assert-Equal ([Kakimaki.BuildHotbar.HotbarScale]::Fit(2, 536, 680, 1072, 680)) ([single]1) 'Tall radial fits available height'
Assert-Equal ([Kakimaki.BuildHotbar.HotbarScale]::Fit(2, 704, 156, 704, 1080)) ([single]1) 'Wide hotbar fits available width'
Write-Output "Passed: $script:checks wheel, paging, binding and scaling checks."

