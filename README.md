# ChzzkOfTheLamb v1.0.0 RC1 source release

외부 배포를 위한 release candidate 소스입니다.

- 사용자 안내: `RELEASE-README.md`
- 배포자 체크리스트: `DEPLOY-RELEASE.md`
- Release ZIP 생성: `build-release.ps1`
- 사용자 설치: `Install-ChzzkOfTheLamb.ps1`

Release Companion은 AWS CLI/SSO를 사용하지 않으며 CHZZK Client Secret을 포함하지 않습니다.
Streamer OAuth는 AWS Auth Gateway를 통해 처리합니다.
