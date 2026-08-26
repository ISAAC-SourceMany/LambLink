# devbridge10s - Dungeon donation events

This patch deliberately uses only members verified in the user's Cult Of The Lamb 1.5.25.1049 `Assembly-CSharp.dll` metadata.

Verified runtime members:

- `DungeonSandboxManager.Active : bool` (public static property getter)
- `PlayerFarming.health : HealthPlayer` (public property getter)
- `HealthPlayer.HP : float` (public get/set)
- `HealthPlayer.Heal(float) : void`
- `PlayerFarming.playerSpells : PlayerSpells` (public property getter)
- `PlayerSpells.faithAmmo : FaithAmmo` (private field; accessed by reflection after exact-name capability check)
- `FaithAmmo.Ammo : float` (public get/set)
- `FaithAmmo.Total : float` (public getter)
- `PlayerController.GetPlayerMaxSpeed() : float`
- `PlayerWeapon.GetDamage(float, int, PlayerFarming) : float` (public static)
- `Health.DamageAllEnemies(float, DamageAllEnemiesType) : void` (public static)
- `DamageAllEnemiesType.Manipulation` enum member

No guessed combat API names are used.

## Dungeon pools

1,000-2,999:
- player heal +0.5
- non-lethal player HP -0.5
- fervour +20% of max
- movement speed x1.15 for 8 sec
- all current enemies damage 0.5

3,000-4,999:
- player heal +1.0
- non-lethal player HP -1.0
- fervour +35% of max
- movement speed x1.20 for 10 sec
- attack damage x1.20 for 10 sec
- all current enemies damage 1.0

5,000-9,999:
- player heal +1.5
- non-lethal player HP -1.5
- fervour +50% of max
- movement + attack x1.25 for 12 sec
- all current enemies damage 1.5

10,000+:
- player heal +2.0
- non-lethal player HP -2.0
- fervour fill to max
- movement + attack x1.40 for 15 sec
- all current enemies damage 2.5

## Safety

- Player donation damage is non-lethal and bottoms at 0.5 HP.
- No boss instant-kill, room completion, room movement, quest flags, unlocks, doctrine, relic unlock, DLC state or story state are touched.
- Mod runtime area is authoritative. If Companion area status is stale during a transition, the Mod corrects the event pool to the actual `DungeonSandboxManager.Active` state before application.
- OBS donation card is shown after `DONATION_EFFECT_RESULT`, so a corrected event name is displayed rather than a stale pre-send event name.

## Diagnostics

At Mod startup:
`[DONATION][CAPABILITY] verified against runtime types: ...`

Per donation:
- `[DONATION][INPUT] ... actualArea=DUNGEON`
- optional `[DONATION][CONTEXT] ... corrected ...`
- `[DONATION][APPLIED] area=DUNGEON ...`
- Companion `[DONATION][RESULT] ...`
