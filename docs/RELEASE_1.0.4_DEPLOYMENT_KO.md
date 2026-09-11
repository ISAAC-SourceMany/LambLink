# LambLink 1.0.4 빌드·배포 기록

2026-09-11 KST. Windows x64 Release, 설치기·Companion 파일 버전 `1.0.4.44`.

## 변경

후원 이벤트 49종의 기본 명칭과 Mod의 던전 지역 보정 명칭을 실제 효과·수치 중심으로 변경했다.
효과량, 지속시간, 큐 처리, 만료 로직은 변경하지 않았다.

## 검증

- 공식 `build-distribution.ps1` 완료: 소스 해시·브랜드·릴리스 식별자·개발 명령 제외·구성요소 SHA-256 검사 통과.
- Mod와 Companion 빌드 경고 0, 오류 0. 설치기 파일 버전 검증 통과.
- 최종 1.0.4 소스로 후원 회귀 테스트 15개 그룹 통과(49종 명칭 포함).
- 실제 게임·치지직·OBS 통합 수동 검증은 미실시.

## 파일 SHA-256

```text
bac636b0df087172d3183b115c28170d67840ddacfebabe8e1f001d69acfa4aa  LambLink-Setup-1.0.4.exe
5958f1419ceb0a1ac2d100014eb38811928df8d7f6eabd0fb334cf1bea14d394  LambLink-Mod-1.0.4.zip
782455aa80c30cba5046c23821bc465aa5257d05ecfaac0033d55c2aae12e9aa  LambLink-Companion-1.0.4-win-x64.zip
```

공유 한글 폰트 ZIP과 기존 버전 파일은 그대로 유지한다. AWS 서버 코드·인프라 변경은 없다.
릴리스 주소: https://github.com/ISAAC-SourceMany/LambLink/releases/tag/v1.0.4
