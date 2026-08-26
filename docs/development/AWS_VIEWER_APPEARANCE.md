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
- viewer appearance: `PK=STREAMER#<streamerId>`, `SK=VIEWER#<viewerId>`

## Deploy

Requirements: AWS CLI + AWS SAM CLI, configured AWS account.

```powershell
cd aws
sam build
sam deploy --guided
```

Provide:

- `MyLambChzzkClientId`
- `MyLambChzzkClientSecret`
- a long random `TokenSigningSecret`

After deployment, copy `ApiUrl`, `FrontendBucketName`, and `FrontendUrl` from stack outputs.

Upload the frontend:

```powershell
.\scripts\deploy-frontend.ps1 -Bucket <FrontendBucketName> -ApiBaseUrl <ApiUrl>
```

In CHZZK developer console, add the AWS OAuth callback URL exactly:

```text
<ApiUrl>/auth/chzzk/callback
```

The existing local Companion OAuth callback is still used by the development Companion itself.

## Connect Companion to AWS

For a one-run development test:

```powershell
$env:COTL_WEB_API_BASE="https://YOUR_API.execute-api.ap-northeast-2.amazonaws.com"
$env:COTL_WEB_FRONTEND_URL="https://YOUR_DISTRIBUTION.cloudfront.net/"
.\ChzzkOfTheLamb.Companion.exe
```

Or set `Cloud.Enabled`, `Cloud.ApiBaseUrl`, and `Cloud.FrontendUrl` in `%LOCALAPPDATA%\ChzzkOfTheLamb\settings.json`.

When Cloud is connected, Companion:

- validates its CHZZK access token with the AWS backend,
- uploads the current form catalog + streamer allow-list,
- prints the viewer setup URL,
- looks up the raffle winner's saved appearance before applying identity.

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
5. During a raffle type `!신도`.

If a viewer has no saved web appearance, Companion falls back to the existing local appearance store; if neither exists, the game's current/random appearance is kept.

## Security notes

- CHZZK Client Secret stays in Lambda environment/CloudFormation parameter, not in the viewer website.
- Viewer session and Companion session are HMAC-signed, short-lived tokens.
- Companion backend session is issued only after the backend validates the CHZZK access token with `/open/v1/users/me`.
- The prototype still uses the Client Secret locally for the streamer's direct Companion OAuth. Before public distribution, move streamer OAuth/token refresh completely behind AWS so the distributed executable does not contain or require the Client Secret.
