# Changelog

## [1.3.0] - 2026-04-26

- Added three selectable weapon models: `AK47`, `HK416`, and `MPX`.
- Added a shared in-game config panel for `ShootZombies` and `Fog&ColdControl`.
- Moved fog and cold handling out of this package and into `Fog&ColdControl`.
- Simplified zombie tuning around visible difficulty presets and core spawn settings.
- Improved zombie behavior and aggression.
- Improved runtime performance with object reuse and reduced repeated work.

## [1.2.11] - 2026-04-11

- Reduced unnecessary diagnostic logging in normal gameplay.
- Fixed fog multiplayer sync so host updates only publish when the fog state actually changes.
- Fixed fog sync handling so fog speed is no longer treated as fog progress.
- Changed the default weapon fire interval to `0.3`.
- Changed the default zombie spawn count to `3`.

## [1.2.10] - 2026-04-11

- Fixed fog control against the current game assembly for offline and host-authority multiplayer.
- Fixed offline fog startup so fog UI, countdown, and movement begin correctly outside online rooms.
- Fixed the fog countdown HUD so waiting time is shown as `Fog Rising`.

## [1.2.9] - 2026-04-11

- Fixed the lobby prompt so compass guidance stays visible when weapon granting is disabled.
- Fixed in-game fog speed updates so non-Fog-Mode changes apply immediately.

## [1.2.8] - 2026-03-31

- Improved zombie spawn validation to prefer rear and out-of-sight positions.
- Added a host-synced zombie destroy distance setting with a default of `70`.
- Set initial zombie wake-up time to `0` so spawned zombies stand up immediately.

## [1.2.7] - 2026-03-31

- Removed recoil and its related config entries from the weapon section.
- Expanded host-to-client sync for combat, zombie, and fog settings.
- Synced additional room values including fire range, bullet size, zombie tuning, knockback, chase thresholds, and fog start delay.

## [1.2.6] - 2026-03-31

- Updated the mod for the latest PEAK custom run changes.
- Reworked item grant creation for weapon, compass, and first aid grants against current game signatures.
- Added run lifecycle refresh hooks for run start, mini-runs, and quicksave restores.
- Reduced unnecessary AK refresh work during gameplay.
- Simplified multiplayer ownership checks to avoid repeated full-scene item scans.

## [1.2.5] - 2026-03-30

- Unified the fog speed UI and fog-related lobby notice under the same bottom-left layout settings.
- Limited compass spawning to Fog Mode.
