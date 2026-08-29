# LambLink 1.0.0-rc36 배포 안내

## 주요 변경

- 래플 당첨자 반영 시 전체 신도 외형 카탈로그를 다시 생성하지 않습니다.
- 동일한 `Chzzk` 이름표 마커 재전송은 UI 갱신을 생략합니다.
- 이름표는 Harmony 패치가 이미 확인한 대상 인스턴스만 갱신합니다.
- 신도 전환 안정화 중 이름과 외형이 실제로 달라진 경우에만 다시 적용합니다.
- 신도 로스터의 주기 fallback을 5초에서 30초로 완화했습니다.
- 외형 카탈로그는 저장 슬롯, 해금 목록 및 필터가 같으면 캐시를 재사용합니다.
- 다른 PC에서 Companion을 실행해도 클라우드 신도 상태를 복구한 뒤 이름표를 동기화합니다.
- 시청자 웹의 신도 히스토리와 게임 리소스 기반 외형 미리보기를 포함합니다.

## 빌드

Windows와 .NET 8 SDK 환경에서 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\build-distribution.ps1
```

완료 후 다음 산출물이 생성됩니다.

- `dist\LambLink-v1.0.0-rc36-distribution.zip`
- `dist\LambLink-v1.0.0-rc36-distribution\USER-DOWNLOAD\LambLink-Setup-1.0.0-rc36.exe`
- `dist\LambLink-v1.0.0-rc36-distribution\CDN-UPLOAD\installer-manifest-1.0.0-rc36.json`

## 진단 로그

Companion 로그:

```text
%LOCALAPPDATA%\LambLink\companion-rc36.log
```

스테이징 실행은 `%LOCALAPPDATA%\LambLink-Staging`을 사용하며 첫 화면에
`RELEASE / CHZZK LIVE / STAGING`이 표시되어야 합니다.

## 성능 검증 기준

동일 PC와 동일 저장 슬롯에서 래플을 반복하고 다음을 확인합니다.

1. `APPLY_RECRUIT_IDENTITY`가 기존 기준인 438.4ms보다 감소해야 합니다.
2. 동일 마커 재전송은 `UI refresh skipped`로 기록되어야 합니다.
3. 새 신도 이름 옆에 초록색 `Chzzk`가 유지되어야 합니다.
4. 선택한 형상·색·종류와 시청자 정보가 그대로 적용되어야 합니다.
5. 신도 생성 뒤 이름·외형이 교리화 완료 과정에서 되돌아가지 않아야 합니다.

## 배포

먼저 스테이징 불변 경로에 업로드합니다.

```powershell
cd .\aws
.\scripts\deploy-release-staging.ps1 -Profile cotl-staging
```

운영 `/releases/` 경로는 위 성능 및 기능 검증이 끝난 산출물만 사용합니다. 설치기 기본 운영
매니페스트는 `installer-manifest-1.0.0-rc36.json`입니다.
