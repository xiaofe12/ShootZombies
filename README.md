# ShootZombies

ShootZombies turns PEAK's blowgun into a configurable zombie-survival weapon mode while keeping the original item and hit flow as close to vanilla as possible.

## What This Mod Adds

- Selectable gun visuals for the blowgun: `AK47`, `HK416`, `MPX`
- Selectable local firing sounds: `ak_sound1`, `ak_sound2`, `ak_sound3`
- Lobby weapon grant hotkey with an on-screen hint
- In-game config panel for ShootZombies
- Shared config panel support for `Thanks-Fog&ColdControl` when that package is installed
- Host-synced zombie and combat rules for multiplayer

## Package Contents

- `Thanks.ShootZombies.dll`
- `Weapons_shootzombies.bundle`
- `AK_Sounds/`

## Config Overview

### Features

- `Mod`: Master on/off switch for the mod
- `Open Config Panel`: Hotkey for the in-game config panel in the lobby
- `Weapon Model X Rotation`: Local held-weapon pitch offset
- `Weapon Model Y Rotation`: Local held-weapon yaw offset
- `Weapon Model Z Rotation`: Local held-weapon roll offset
- `Weapon Model X Position`: Local held-weapon horizontal offset
- `Weapon Model Y Position`: Local held-weapon vertical offset
- `Weapon Model Z Position`: Local held-weapon depth offset

### Weapon

- `Weapon`: Enables or disables the blowgun-to-weapon presentation
- `Weapon Selection`: Chooses the active weapon model
- `Spawn Weapon`: Lobby hotkey used to grant the selected weapon
- `Fire Interval`: Time between shots
- `Fire Volume`: Local weapon sound volume
- `AK Sound`: Chooses the local firing sound preset
- `Max Distance`: Maximum hit range
- `Bullet Size`: Projectile size used by the hit logic
- `Zombie Time Reduction`: Reduces zombie lifetime when zombies are hit
- `Weapon Model Scale`: Local held-weapon scale

### Zombie

- `Zombie Spawn`: Enables zombie spawning during gameplay
- `Behavior Difficulty`: Main zombie difficulty preset
- `Max Count`: Maximum number of active zombies
- `Spawn Interval`: Delay between spawn waves
- `Max Lifetime`: Time before spawned zombies are cleaned up
- `Destroy Distance`: Distance at which far zombies are removed

## Difficulty Presets

`Behavior Difficulty` is the main zombie tuning control.

Available presets:

- `Easy`
- `Standard`
- `Hard`
- `Insane`
- `Nightmare`

Default preset:

- `Standard`

Advanced zombie values such as chase timing, sprint distance, lunge timing, bite recovery, wake-up behavior, and spawn randomness are derived from the selected preset. They are intentionally hidden from normal player tuning.

## Multiplayer Rules

Host-synced gameplay settings:

- mod enable state
- weapon enable state
- fire timing and hit-related values
- zombie spawning
- zombie difficulty
- zombie lifetime
- zombie destroy distance

Local-only settings:

- weapon model position
- weapon model rotation
- weapon model scale
- local firing sound choice
- local panel hotkey

## Default Values

- `Mod`: `true`
- `Weapon`: `true`
- `Weapon Selection`: `AK47`
- `Spawn Weapon`: `T`
- `Open Config Panel`: `\`
- `Fire Interval`: `0.4`
- `Fire Volume`: `0.8`
- `Max Distance`: `100`
- `Bullet Size`: `0.1`
- `Zombie Time Reduction`: `15`
- `Zombie Spawn`: `true`
- `Behavior Difficulty`: `Standard`
- `Max Count`: `5`
- `Spawn Interval`: `15`
- `Max Lifetime`: `120`
- `Destroy Distance`: `70`

## Notes

- Use the same package version on host and clients for stable multiplayer behavior.
- The custom lobby config panel and the ModConfig page are intended to expose the same player-facing settings.
