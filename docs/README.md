# 프로젝트 문서 안내

이 디렉터리는 현재 사용되는 문서와 복원된 개발 기록을 구분해 보관합니다.

## 사용자 문서

- [한글 사용자 설명서](guides/USER_GUIDE_KO.md): 설치, 치지직 로그인, OBS, 시청자 링크와 문제 해결

## 개발 문서

- [개발 환경 설정](development/SETUP.md)
- [구조 설명](development/ARCHITECTURE.md)
- [AWS 시청자 외형 서비스](development/AWS_VIEWER_APPEARANCE.md)
- [설정 공급자와 IAM](development/CONFIGURATION_AND_IAM.md)

## 조사 자료

- [Cult of the Lamb 1.5.25 외형 역공학 기록](research/COTL_1_5_25_APPEARANCE_REVERSE_ENGINEERING.md)

조사 자료에는 게임 파일 자체가 아니라 개발 과정에서 작성한 기술 메모만 보관합니다.

## 한글 폰트 패치

- [COTL Korean Font Fix 빌드·사용 문서](../tools/COTL_KoreanFontFix/README.md)
- [4.3.0 변경 사항](../tools/COTL_KoreanFontFix/RELEASE_NOTES.md)
- [4.2.1 atlas 진단판 개발 기록](history/font-fix-4.2.1-atlas-trace.md)
- [4.3.0 배포판 개발 기록](history/font-fix-4.3.0.md)

RC35 자동 설치기는 검증된 4.2.1 배포 자산을 사용합니다. 도구 디렉터리의 4.3.0은 독립 패치의 최신 보존 소스이며 RC35 설치 패키지에 자동으로 대체 적용되지 않습니다.

## 릴리스 문서

- [RC35 검증 기록](releases/VALIDATION-RC35.md)
- [RC35 배포 안내](../DISTRIBUTION-RC35.md)

루트의 `DEPLOY-INSTALLER.md`, `DEPLOY-RELEASE.md`, `DISTRIBUTION-RC35.md`, `RELEASE-README.md`는 현재 RC35 빌드와 배포에 사용되므로 위치를 유지합니다.

## 역사 기록

- [복원된 개발 이력 안내](history/README.md)
- [Git 이력 복원 기록](../HISTORY_RECONSTRUCTION.md)

`history` 아래 문서는 현재 사용법이나 향후 계획이 아닙니다. 특정 개발 단계와 RC에서 실제로 사용된 기록을 보존한 것입니다.

## 관리 원칙

- 현재 사용법과 개발 절차는 `guides`, `development`, `releases`에서 갱신합니다.
- 버전이 지난 배포·검증 문서는 `history/releases`로 이동합니다.
- 단계별 조사, 테스트, 수정 기록은 `history/technical`에 보존합니다.
- 완료되거나 폐기된 계획은 `history/planning`에 보존하며 현재 로드맵으로 취급하지 않습니다.
- 토큰, 설정 파일, 사용자 데이터, EGG 및 게임 데이터는 문서나 Git 이력에 포함하지 않습니다.
