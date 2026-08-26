# devbridge9 - Configuration providers and Lambda IAM

## Configuration providers

The My Lamb backend now has one configuration abstraction in `aws/backend/configuration.py`.

- `COTL_CONFIG_PROVIDER=local`: reads `CHZZK_CLIENT_ID`, `CHZZK_CLIENT_SECRET`, `MYLAMB_CHZZK_CLIENT_ID`, `MYLAMB_CHZZK_CLIENT_SECRET` from environment variables.
- `COTL_CONFIG_PROVIDER=aws`: reads Client IDs from SSM Parameter Store and Client Secrets from Secrets Manager.

Default AWS names:

- `/cotl/prod/chzzk/companion/client-id`
- `/cotl/prod/chzzk/mylamb/client-id`
- `/cotl/prod/chzzk/companion`
- `/cotl/prod/chzzk/mylamb`

Secret values may be either plain text or JSON such as `{ "clientSecret": "..." }`.

## Local smoke tests before Lambda

### Environment-provider test

```powershell
$env:CHZZK_CLIENT_ID='...'
$env:CHZZK_CLIENT_SECRET='...'
$env:MYLAMB_CHZZK_CLIENT_ID='...'
$env:MYLAMB_CHZZK_CLIENT_SECRET='...'
.\local-server\test-config-local.ps1
```

### Production-provider test from your PC

This tests the exact SSM + Secrets Manager provider before Lambda exists.

```powershell
py -m pip install boto3
aws configure
$env:AWS_DEFAULT_REGION='ap-northeast-2'
# Optional when using a named profile:
# $env:AWS_PROFILE='cotl-dev'
.\local-server\test-config-aws.ps1
```

Expected output ends with `[CONFIG] OK`. Secret values are never printed.

Then the local My Lamb web server can use the AWS provider directly:

```powershell
$env:COTL_CONFIG_PROVIDER='aws'
.\local-server\run-local-live.ps1
```

The server still runs at `127.0.0.1:17882`; only configuration retrieval comes from AWS.


## Companion local AWS credential provider

For local integration testing, the Companion can now load the streamer CHZZK Client ID/Secret directly from SSM + Secrets Manager through the caller's AWS CLI/SSO session.

Set `COTL_COMPANION_CONFIG_PROVIDER=aws-cli` or run `local-server\configure-companion-local-aws.ps1`.

This provider is strictly for development. The final distributed Companion must not receive the CHZZK client secret; production will use the AWS authentication gateway.
