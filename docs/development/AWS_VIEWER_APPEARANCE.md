# AWS Viewer Appearance Service (devbridge5)

## Purpose

The website is for viewers, not the streamer. A viewer opens a normal HTTPS page, signs in with CHZZK, chooses one of the forms that the streamer's **currently running save** exposes, and stores that appearance for that streamer.

Production raffle flow:

1. Cult of the Lamb creates a normal pending recruit.
2. The Mod detects the existing recruit ID and sends `RAFFLE_REQUESTED`.
3. Companion opens the 30-second `!신도` raffle.
4. Winner is selected.
5. Companion asks AWS for `(streamerChannelId, viewerChannelId)` appearance.
6. Companion sends `APPLY_RECRUIT_IDENTITY` for the **existing** recruit ID.
7. Mod writes nickname + appearance to that recruit. It does **not** call `CreateNewRecruit`.

Multiple pending recruits are queued in Companion. Only one raffle is active at once.

## AWS resources

`aws/template.yaml` provisions:

- API Gateway HTTP API
- Lambda (Python 3.12)
- DynamoDB (PAY_PER_REQUEST)
- private S3 bucket
- CloudFront distribution with OAC

DynamoDB keys:

- catalog: `PK=STREAMER#<streamerId>`, `SK=CATALOG`
- viewer draft/appearance: `PK=STREAMER#<streamerId>`, `SK=DRAFT#<saveId>#<viewerId>`
- viewer follower state/history: `PK=STREAMER#<streamerId>`, `SK=STATE#<saveId>#<viewerId>`

## Isolated staging deploy

Requirements: AWS CLI + AWS SAM CLI, configured AWS account. Do not deploy untested files over the production stack or production S3 keys.

```powershell
cd aws
.\scripts\deploy-staging.ps1 -Profile <AWS_PROFILE>
```

The staging stack creates separate API Gateway, Lambda, DynamoDB, S3, CloudFront, and token-signing secret resources. It reads CHZZK credentials only from:

- `/cotl/staging/chzzk/companion/client-id` (SSM)
- `/cotl/staging/chzzk/mylamb/client-id` (SSM)
- `/cotl/staging/chzzk/companion` (Secrets Manager)
- `/cotl/staging/chzzk/mylamb` (Secrets Manager)

After the first infrastructure deploy, register the emitted Companion and Viewer callback URLs in two staging CHZZK applications. Then store their credentials without writing Client Secrets to the repository:

Upload the frontend:

```powershell
.\scripts\configure-staging-chzzk.ps1 `
  -Profile <AWS_PROFILE> `
  -CompanionClientId <STAGING_COMPANION_CLIENT_ID> `
  -ViewerClientId <STAGING_VIEWER_CLIENT_ID>
```

Run `deploy-staging.ps1` again after storing the credentials. Frontend preview assets are uploaded under a content-derived immutable path, so production and browser caches are not overwritten.

Upload the already-built installer components to a versioned staging-only path:

```powershell
.\scripts\deploy-release-staging.ps1 -Profile <AWS_PROFILE>
```

The script prints a `COTL_INSTALLER_MANIFEST_URL` value. Set it only in the test PowerShell process before running the local setup EXE. It does not overwrite production `/releases/` objects.

The two CHZZK callback URLs must match the stack outputs exactly:

```text
<ApiUrl>/auth/companion/callback
<ApiUrl>/auth/chzzk/callback
```

Production uses `/cotl/prod/...` and remains isolated from the staging table, bucket, distribution, and CHZZK applications.

## Connect Companion to AWS

The Release Companion is pinned to production by default. For an isolated staging run, use the
repository script instead of launching the EXE directly:

```powershell
cd aws
.\scripts\run-staging-companion.ps1 -Profile <AWS_PROFILE>
```

The script enables the explicit `COTL_STAGING_MODE=1` gate, reads the API/frontend URLs from the
staging CloudFormation stack, and stores all staging data under
`%LOCALAPPDATA%\LambLink-Staging`. The staging desktop shortcut also has a distinct
`(Staging)` name. Without the explicit gate, a Release Companion ignores endpoint overrides and
continues to use production.

Non-distribution development builds may still use `COTL_WEB_API_BASE`,
`COTL_WEB_FRONTEND_URL`, or the `Cloud` section of `%LOCALAPPDATA%\LambLink\settings.json`.

When Cloud is connected, Companion:

- validates its CHZZK access token with the AWS backend,
- uploads the current form catalog + streamer allow-list,
- prints the viewer setup URL,
- looks up the raffle winner's saved appearance before applying identity,
- reads the authenticated, paged follower-state collection for the loaded save,
- restores a missing local `viewer-followers.json` only from a newer cloud revision,
- sends a `Chzzk` nameplate marker only after the restored follower ID and normalized name match
  the live game roster.

Follower-state recovery uses
`GET /streamers/<streamerId>/follower-states?saveId=<saveId>`. The endpoint requires a Companion
session whose streamer identity matches the URL. It never accepts viewer sessions. Results are
paged and the server derives `viewerChannelId` from the DynamoDB sort key instead of trusting the
stored JSON payload.

If the local mapping is empty and cloud recovery is unavailable or returns unusable state,
Companion suppresses the destructive empty marker synchronization and retries on a later roster.
A successful authoritative empty response is distinct: it confirms that the save has no cloud
follower mappings, so an empty marker sync is then allowed. Existing non-empty local mappings remain
available as an offline fallback and are still validated by follower ID plus normalized name.

## Viewer URL

Companion prints a URL similar to:

```text
https://<cloudfront>/?streamer=<streamerChannelId>
```

Viewer flow:

1. Open URL.
2. Sign in with CHZZK.
3. Select Form / Variant / Color.
4. Save.
5. Use the always-visible `신도 히스토리 보기` button to inspect generations, death/resurrection events, and copy an older appearance.
6. During a raffle type `!신도`.

The deployed page uses `Follower.preview.json` plus a deployment-generated 4096px
`Follower.preview.png`. The browser no longer downloads the 8192px, roughly 25 MB source atlas.
Preview metadata groups every top-level Spine skin by its real numbered resources instead of
assuming three variants. This preserves single-variant and special forms and includes the known
two-, four-, and five-variant groups. The viewer still treats the runtime catalog's `variantIds`
as authoritative, so unused preview resources are never offered as selectable options.
Form and variant thumbnails are rendered only when they approach the viewport, and a color or
variant click updates only the selected buttons and the main preview instead of rebuilding every
picker. `deploy-frontend.ps1` validates that all RC39 catalog additions have preview metadata and
uploads immutable assets before publishing the HTML that references them.

If a viewer has no saved web appearance, Companion falls back to the existing local appearance store; if neither exists, the game's current/random appearance is kept.

## Security notes

- CHZZK Client Secret stays in Lambda environment/CloudFormation parameter, not in the viewer website.
- Viewer session and Companion session are HMAC-signed, short-lived tokens.
- Companion backend session is issued only after the backend validates the CHZZK access token with `/open/v1/users/me`.
- The distributed Companion does not contain CHZZK Client Secrets and does not use local AWS CLI/SSO credentials. Streamer OAuth and refresh go through the selected AWS auth gateway.
