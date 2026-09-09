# LambLink 1.0.1 빌드·배포 기록

2026-09-10 KST. Windows x64, Release, 파일 버전 1.0.1.41.

사용자 요청에 따라 1.0.1 설치본과 구성 파일을 운영 CDN에 배포했다. 기존 1.0.0 설치기·manifest·Mod·Companion 주소는 유지했다. 공용 한글 폰트 ZIP은 1.0.0 manifest와 동일 해시임을 확인했다.

- 설치기: https://d1gvw9ccym1qvn.cloudfront.net/releases/LambLink-Setup-1.0.1.exe
- 설치 정보: https://d1gvw9ccym1qvn.cloudfront.net/releases/installer-manifest-1.0.1.json
- 로컬 설치기: `release-hosting/LambLink-Setup-1.0.1.exe`
- 전달용 전체 묶음: `dist/LambLink-v1.0.1-distribution.zip`

`build-distribution.ps1` 완료: 소스 지문, 브랜드 호환성, 정식 빌드의 개발 명령 제외, 바이너리 내 패치 진단 항목, 버전 및 구성 파일 해시 검사 통과. Mod·Companion 빌드 경고 0, 오류 0. 런타임을 포함한 단일 EXE 형식으로 게시했다.

1.0.1 버전에서 후원 14개 테스트 그룹과 Chrome 오버레이 복구 테스트 통과. 업로드 후 CDN에서 설치기·Mod·Companion·한글 폰트 ZIP을 실제 다운로드하여 모든 SHA-256이 로컬 빌드 및 manifest와 일치하는 것을 확인했다.

실제 게임·OBS·치지직 후원 통합 검증과 사용자의 PC에서 설치기를 실행하는 검증은 미완료다. 사용자 설치 디렉터리를 직접 변경하지 않았다. OBS 자동 복구는 `overlay file` 명령으로 확인한 로컬 HTML을 브라우저 소스의 로컬 파일로 지정해야 한다.

초기 자동 승인 검토는 일괄 소스 해시 갱신을 거부했다. 후원 변경 diff와 통과한 테스트를 검토한 뒤 불일치한 6개 파일의 명시적 고정 해시 갱신이 승인되었다. 무결성 검사를 제거하지 않았다. 빌드 스크립트는 Windows PowerShell의 한글 검증을 위해 UTF-8 BOM을 보존한다.

배포 패키지에 포함된 DONATION_PATCH_VALIDATION_KO.md는 배포 전 검증 스냅샷이다. 이후 배포 완료 사실은 이 문서를 기준으로 한다. 게임 적용 기록 v2 및 구버전 복원 제한은 기존 검증 문서에 따르며, 소프트웨어 배포가 실제 효과의 완전한 검증을 의미하지 않는다.
