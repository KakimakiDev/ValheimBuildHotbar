# Changelog

## 0.9.1

- Fix outdated left-stick API calls that caused missing-method errors with the current Valheim version.
- Preserve existing settings, saved pieces and controller behaviour.
- Fully restart Valheim after updating so the corrected DLL replaces the version loaded in memory.

## 0.9.0

- Add a paged controller radial: hold Square/X, aim with the left stick and release the opening shortcut to select. The last highlight stays selected when the stick returns to centre.
- Save the current piece with R2/RT and clear a slot with L2/LT while the radial is open. Hold R2/RT in the build menu to assign its highlighted piece to a radial slot; click the left stick to deselect the target.
- Add controller-accessible settings, configurable opening/save combinations and an optional toggle-and-confirm mode.
- Prevent movement, camera rotation and build-menu navigation while selecting radial slots. Debounce D-pad paging.
- Add native button icons and contextual bottom control hints for controller and keyboard. Hide irrelevant placement hints while the build menu is open.
- Refresh the PC hotbar and add borderless translucent backgrounds to both menus. Hide the horizontal bar while a controller is active and remove radial slot numbers.
- Add independent horizontal hotbar and radial size settings from 50% to 200%, fitted to the screen.
- Enable auto activation on equip by default for new configurations. Existing activation preferences and saved slots are preserved.
- Keep an open controller radial and its hints visible when a keyboard screenshot key changes the active input device.


## 0.8.0

- Rename the DLL, project, namespace and plugin class to BuildHotbar.
- Use ValheimEnthusiestKakimaki.BuildHotbar for the plugin, patches and configuration.
- Start with fresh settings and saved slots; no legacy configuration migration.
- Remove the obsolete row-scroll modifier setting; use the configurable row-scroll key.
- Update standalone installation instructions and package naming.


