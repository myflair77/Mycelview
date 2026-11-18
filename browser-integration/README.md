# Mycelview 브라우저 탭 연결

크롬·엣지·웨일의 각 창 미리보기 카드 아래에 탭 아이콘과 제목을 표시합니다.
탭을 클릭하면 해당 브라우저 창과 탭으로 전환되고 엣지콜 미리보기가 닫힙니다.
아이콘을 제공하지 않는 내부 페이지 등은 브라우저 아이콘으로 표시합니다.

## 이 PC에서 남은 설치 단계

연결 프로그램은 이미 등록되어 있습니다. 사용하는 브라우저마다 한 번씩 다음 작업을 하세요.

1. 크롬은 chrome://extensions, 엣지는 edge://extensions, 웨일은 whale://extensions 를 엽니다.
2. 개발자 모드를 켜고 '압축해제된 확장 프로그램을 로드'를 선택합니다.
3. 이 프로젝트의 browser-extension 폴더를 선택합니다.
4. 확장 목록의 **Mycelview 탭 연결**이 활성화되었는지 확인합니다.

브라우저 프로필이 여러 개라면 사용하는 프로필마다 추가합니다.
Mycelview 실행 후 자동 연결됩니다. 확장 팝업의 다시 연결 버튼으로 재시도할 수 있습니다.
확장 설치 전에도 기존 엣지콜 창 미리보기와 창 전환은 사용할 수 있습니다.

## 연결과 정보 처리

- 브라우저의 공식 tabs/windows/nativeMessaging API를 사용합니다.
- 제목, 탭 ID, 창 ID, 브라우저에 캐시된 아이콘만 같은 Windows 사용자 계정의 Mycelview에 전달합니다.
- 방문 URL·페이지 본문은 Mycelview에 전송하지 않습니다. 외부 서버로 전송하지 않습니다.
- 탭 목록은 메모리에만 보관하며, 연결 종료 시 제거합니다.
- 시크릿/InPrivate 창은 지원하지 않습니다.
- 처음 연결할 때 제목이 같은 창이 여러 개이면 해당 창을 한 번 직접 선택하면 연결됩니다.
- 닫히거나 다른 창으로 이동한 탭은 전환 전에 다시 검증합니다.
- 웨일의 버전 하위 폴더 실행 경로도 원래 등록된 whale.exe와 같은 설치로 인식합니다.

## 다른 PC / 프로젝트 이동

.tools/dotnet SDK가 준비된 프로젝트에서 PowerShell로 다음을 실행합니다.

    ./browser-integration/install.ps1

설치 후 브라우저 확장을 위 방법으로 추가합니다. 별도 관리자 권한은 필요하지 않습니다.
프로젝트 위치를 옮기면 위 설치 명령을 다시 실행하고 확장도 새 폴더로 다시 로드하세요.

연결 프로그램 등록만 해제하려면:

    ./browser-integration/install.ps1 -Uninstall

브라우저 확장은 각 브라우저의 확장 관리에서 제거합니다.
확장 ID: mmfblbmbnbilignabccmdhhecfonmbbc

## 검증 범위

프로토콜·세 브라우저 유형별 창/탭 라우팅·닫힌 탭 거부·연결 종료·최소화 복원 명령·시크릿 제외·즐겨찾기 줄바꿈 자동 검사를 제공합니다.
확장이 실제 브라우저에 설치된 뒤의 최종 클릭 전환은 사용자 환경에서 확인해야 합니다.
개발 과정에서 사용자 브라우저 조작이나 컴퓨터 유즈는 사용하지 않았습니다.

공식 API:
- https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
- https://developer.chrome.com/docs/extensions/reference/api/tabs
- https://developer.chrome.com/docs/extensions/reference/api/windows
- https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging
- https://developers.whale.naver.com/api/extensions/