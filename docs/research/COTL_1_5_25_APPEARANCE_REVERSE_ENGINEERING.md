# COTL 1.5.25.1049 appearance reverse-engineering notes

Source inspected: the user-provided `Cult Of The Lamb_Data.zip`, especially `Managed/Assembly-CSharp.dll`.

## Confirmed data model

`SkinAndData` contains:

- `Title`
- `_dropLocation`
- `_hidden`
- `_invariant`
- `_lockColor`
- `TwitchPremium`
- `Skin`
- `SlotAndColours`

`SlotsAndColours` contains `SlotAndColours` plus `AllColor`. `SlotAndColor` contains `Slot` and `color`.

## Confirmed vanilla UI flow

The form picker does **not** read a static form icon Sprite from `SkinAndData`. `UIAppearanceMenuController_Form.Populate` obtains `UIManager.FollowerFormItemTemplate`; `IndoctrinationFormItem.Configure(SkinAndData)` then configures the follower graphic from `SkinAndData.Skin`.

`UIAppearanceMenuController_Variant.OnShowStarted` iterates `SkinAndData.Skin`; the persisted value is `FollowerBrainInfo.SkinVariation`, so the correct variant IDs are the list indexes.

`UIAppearanceMenuController_Colour.OnShowStarted` iterates `SkinAndData.SlotAndColours`; the persisted value is `FollowerBrainInfo.SkinColour`, so the correct colour IDs are the list indexes. Each colour entry's `SlotsAndColours.AllColor` is the base colour value used by that option.

## devbridge9o change

The mod now stops guessing `VariantIds`/`ColorIds` from member names and uses these exact structures. For form thumbnails it first instantiates the game's own `UIManager.FollowerFormItemTemplate`, calls the same `Configure(SkinAndData)` method as vanilla, and renders the tile to a transparent PNG locally. The PNG is cached in-memory for the game session and passed to the existing Companion/local-server preview cache pipeline. Static Sprite search is retained only as a fallback diagnostic path.

Expected log on success:

```text
[PREVIEW RENDER] form=Cat, source=UIManager.FollowerFormItemTemplate, pngBytes=..., cached=true
Follower forms: ... variantForms=..., variantOptions=..., colorForms=..., colorOptions=..., previewForms=...
```

If the runtime UI template cannot be rendered safely in a particular scene, the log contains `[PREVIEW RENDER] ... failed:` and the old fallback remains active.
