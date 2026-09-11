# LambLink 1.0.3 빌드·배포 기록

2026-09-11 KST. Windows x64, Release, 설치기/Companion 파일 버전 `1.0.3.43`.

## 변경 및 적용

로컬 OBS 부트스트랩의 iframe에 내부 문서와 같은 `color-scheme: dark`를 지정해 검은 불투명 배경을 제거했다. 수정 전 Chromium 렌더링의 배경 픽셀은 `18,18,18,255`였고, 수정 후 알파값은 0이다. Companion 실행 시 로컬 HTML이 갱신되므로 업데이트 후 OBS 브라우저 소스를 한 번 새로고침한다.

## 검증

- 공식 `build-distribution.ps1` 완료. Mod/Companion 빌드 경고 0, 오류 0.
- 버전, 구성 파일 해시, 개발 명령 제외 및 브랜드 호환성 검사 통과.
- 1.0.3으로 다시 빌드한 OverlayRecoveryTests의 실제 Chrome 테스트 통과.
- 서버 연결 전/후, 종료 후, 후원 표시 중, 밝은/어두운 OS 테마에서 투명 픽셀 검사 통과.
- 서버 재시작, 실패/멈춘 상태 요청, 브라우저 재열기, 후원 카드 위치와 오프라인 후원 표시 복구 통과.
- 운영 CDN에서 설치기·Mod·Companion·공유 폰트를 실제 다운로드해 SHA-256 일치 확인.
- 실제 게임·치지직·OBS 전체 통합 수동 검증은 하지 않았다.

## 배포 파일

- 설치기: `https://d1gvw9ccym1qvn.cloudfront.net/releases/LambLink-Setup-1.0.3.exe`
- 매니페스트: `https://d1gvw9ccym1qvn.cloudfront.net/releases/installer-manifest-1.0.3.json`
- GitHub 릴리스: `https://github.com/ISAAC-SourceMany/LambLink/releases/tag/v1.0.3`

```text
13129b0811638f3812855c88a703a16b72bdb98dbcb3ba6fa5e688cbac51db72  LambLink-Setup-1.0.3.exe
21098af1631ce9e9004607bfc31334e2b31a3b9cdf7d2bd148c40af94c1ed108  LambLink-Mod-1.0.3.zip
8752baf3dd0aec8a38bf13c3a30e3f08dfead6e6798e13d76474779e30017586  LambLink-Companion-1.0.3-win-x64.zip
361eb6768cb718f86dc7e4bce1008a42b0b524d0171d4defe7650f10d315d2da  LambLink-v1.0.3-distribution.zip
```

공유 한글 폰트 ZIP은 기존 운영 파일 해시 `e3b374e76678ea4b039b992954feeb9990bf4618dcd7e83f1fd348288ae4d767`을 그대로 재사용한다. 빌드가 같은 URL의 ZIP을 새로 압축하지 않도록 보완했으며 이번 배포에서 공유 폰트 URL을 덮어쓰지 않았다.
