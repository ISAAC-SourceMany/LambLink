# RC18 automatic viewer appearance unlock publishing

## Behavior

While a save is loaded, Companion requests the live appearance catalog every 30 seconds by
default. When the game reports a newly unlocked normal follower form, RC18 automatically adds
that form to the viewer allow-list and uploads the changed catalog. The viewer website polls the
catalog every 15 seconds, so the normal end-to-end update window is approximately 15–45 seconds.

Special forms remain opt-in, and modded forms are only discovered when `IncludeModdedForms` is
enabled. A streamer can explicitly control any known form with:

```text
form allow <id>
form deny <id>
```

## Persistent policy state

`viewer-appearances.json` now stores three separate concepts:

- `allowedFormIds`: forms currently exposed to viewers.
- `deniedFormIds`: forms explicitly denied by the streamer.
- `knownUnlockedFormIds`: unlocked forms already seen by the automatic policy.

Newly unlocked normal forms are auto-allowed only when they are not in `deniedFormIds`.
`form allow` removes the denial. `form deny` persists the denial across relaunches, save changes,
locking/unlocking, and future catalog refreshes.

The viewer frontend filters out any catalog entry that is still locked, even if a stale or
manually edited allow-list contains its ID. The cloud API applies the same unlock check when a
viewer saves an appearance, so a locked form cannot be submitted by calling the API directly.
Companion also intersects the persistent allow-list with the active save's unlocked catalog before
uploading it, preventing an unlock from one save slot from leaking into another slot.
If a viewer's previously saved selection is unavailable in the active save, Companion discards only
that stale appearance selection and still applies the raffle winner's identity using the game's
default recruit appearance.

## RC17 and older migration

Older state did not record explicit denials separately. On the first valid RC18 catalog:

- A non-empty legacy allow-list is preserved. Currently unlocked normal forms missing from that
  list are recorded as denied so past streamer filtering is not silently undone.
- An empty/uninitialized legacy list is repaired by allowing all currently unlocked normal forms,
  matching the previous recovery behavior.
- The current unlocked set becomes the migration baseline. Forms unlocked after that baseline are
  automatically allowed.

## Expected logs

When a new form is unlocked:

```text
[APPEARANCE] loaded <N> forms for save slot_0 (changed)
[APPEARANCE][AUTO-ALLOW] newly unlocked forms enabled for viewers: <FormId>
[CLOUD] catalog uploaded+verified: streamer=..., save=slot_0, forms=<N>, allowed=<M>
```

The viewer page then shows:

```text
외형 목록이 갱신되었습니다. 선택 가능 <M>개
```
