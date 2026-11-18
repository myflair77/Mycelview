# Windows 설치 파일

## 빌드

    ./installer/build.ps1 -Version 0.1.0

출력: artifacts/releases/Mycelview-Setup-0.1.0-win-x64.exe 및 SHA256 파일.

필요 도구:
- .tools/dotnet: .NET 10 SDK
- .tools/inno643/tools/ISCC.exe: Inno Setup 6.4.3
  NuGet Tools.InnoSetup 6.4.3 패키지를 .tools/inno643에 풀어서 사용할 수 있습니다.
  실행 전에 ISCC.exe의 Authenticode 서명이 Valid인지 확인합니다.

Inno Setup 6.4.3은 상업적 용도를 포함한 사용이 허용되는 라이선스입니다.
컴파일러는 설치 파일에 포함되지 않습니다. .NET 실행 환경과 고지 파일은 포함됩니다.

## 동작

- Windows 10 2004 이상 / Windows 11, x64.
- 현재 사용자 계정의 LocalAppData/Programs/Mycelview에 설치. 관리자 권한 불필요.
- 앱과 브라우저 호스트는 self-contained 배포로 SDK/.NET 별도 설치가 필요하지 않음.
- Chrome/Edge/Whale native messaging host를 HKCU에 등록.
- 기존 LocalAppData/PinShelf 데이터 경로 유지. 설정·즐겨찾기·인덱스를 설치/제거하지 않음.
- 실행 중인 Mycelview가 있으면 종료 안내. 강제 종료하지 않음.
- 완료 직후 한국어 확장 설치 안내 표시. 시작 메뉴에서도 안내 다시 열기 가능.
- 확장은 스토어 미등록 상태이므로 사용자가 각 브라우저에서 직접 개발자 모드로 추가.
- 업데이트 후 확장 관리 화면에서 새로고침. 예전 개발 폴더 확장은 설치 폴더로 다시 등록.
- 제거 시 자기 설치 폴더를 가리키는 native messaging 등록만 제거. 사용자 데이터 유지.
- 다른 프로그램이 나중에 등록 경로를 바꾸었다면 해당 등록을 제거하지 않음.

## 검증

    ./installer/verify.ps1 -Version 0.1.0

정상 종료한 개발용 Mycelview를 시험 종료 후 다시 실행합니다.
임시 경로에서 설치/재설치/제거, 3개 브라우저 등록, 독립 실행을 검사합니다.
기존 설정 파일의 SHA256 동일 여부를 확인하고 이전 브라우저 호스트 등록을 복원합니다.
이미 정식 설치본이 있는 경우 검사를 중단하여 기존 설치를 보호합니다.
실제 사용자 창/키보드/마우스는 자동 조작하지 않습니다.

현재 배포 파일은 코드 서명 인증서로 서명하지 않았습니다. 배포 전 신뢰할 수 있는 코드 서명을 추가할 수 있습니다.