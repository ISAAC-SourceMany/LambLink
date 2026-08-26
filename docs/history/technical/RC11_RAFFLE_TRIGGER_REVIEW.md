# RC11 raffle trigger review

The supplied Cult of the Lamb 1.5.25.1049 `Assembly-CSharp.dll` was inspected before changing the trigger.
The runtime assembly contains the vanilla `FollowerRecruit` lifecycle methods `ContinueRecruit` and `ContinueRecruitRoutine`, in addition to UI-layer names such as `ShowIndoctrinationMenu` and `OnShowStarted`.

RC11 therefore uses `FollowerRecruit.ContinueRecruit*` as the primary trigger and keeps UI hooks only as compatibility fallbacks. It deliberately does not use `SimpleNewRecruitRoutine` as the primary trigger because that can run when a recruit is created, before the streamer actually starts indoctrination.

Request state is now committed only after the localhost WebSocket send succeeds. A disconnected/failed send does not poison the recruit ID as already announced.
