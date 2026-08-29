# Architecture

```text
CHZZK Open API
  OAuth + CHAT / DONATION / SUBSCRIPTION
                 |
                 v
LambLink.Companion
  - RaffleManager
  - DonationRuleEngine
  - ViewerFollowerRepository
  - AppearanceStore
  - localhost WebSocket bridge
                 |
                 | bidirectional protocol
                 v
Cult of the Lamb process
  BepInEx / Harmony
  LambLink.Mod
  - network receiver
  - ConcurrentQueue<GameCommand>
  - Unity main-thread Update()
  - FollowerService
  - FollowerAppearanceService
  - DonationEffectService
  - GameSaveService
```

## Main-thread boundary

CHZZK/network callbacks never call Unity/Cult of the Lamb APIs directly. Network messages enter a `ConcurrentQueue<GameCommandEnvelope>` and are executed from the BepInEx plugin `Update()` loop.

## Viewer/follower identity

Display name is not identity.

```text
CHZZK senderChannelId
        +
streamerChannelId
        +
saveId
        <->
FollowerInfo.ID
```

Nickname is stored only as `LastKnownNickname`.

## Appearance flow

```text
Game runtime catalog
    -> FollowerAppearanceService
    -> FOLLOWER_APPEARANCE_CATALOG
    -> Companion allow-list
    -> viewer selection
    -> raffle winner
    -> validate against current catalog
    -> CreateNewRecruit
    -> apply name + appearance
```

The runtime adapter deliberately avoids a hardcoded species list so game updates/DLC can appear automatically when the game exposes them.

## Security boundary

The game mod never receives CHZZK access tokens or client secrets. Public distribution still requires an auth gateway because the CHZZK authorization-code exchange uses a client secret.
