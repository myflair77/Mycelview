# PinShelf 아키텍처

## 경계

- `Models.cs`: 버전이 있는 저장 모델, 파일 항목, 변경 알림과 ICommand
- `Services.cs`: 실제 파일 열거/Windows 기본 연결 실행, JSON 저장소
- `CardViewModel.cs`: 카드별 상태·탐색 기록·취소 가능한 읽기·변경 감시
- `MainViewModel.cs`: 카드 수명, 등록/해제/복제, 저장 스케줄과 주기 갱신
- `MainWindow.xaml`: 통합 탐색기, 폴더 2열 영역, 파일 1열 목록과 등록 도구
- `CardView.xaml`: 카드 헤더, 탐색 도구, 가상화 DataGrid, 메모
- View code-behind: 포인터 이벤트, 정렬/스크롤 등 WPF 표시 상태와 VM 연결
- `App.xaml.cs`: 사용자별 단일 인스턴스 및 격리된 테스트 모드
- `SmokeTests.cs`: 테스트 전용 파일을 사용하는 opt-in WPF 통합 검증

폴더의 최초 경로와 현재 경로를 분리합니다. 탐색 기록은 카드별 메모리에 보관하며, 재실행 시 현재 위치와 최초 위치를 복원합니다. 뒤로/앞으로 스택 자체는 재실행 시 복원하지 않습니다.

## 저장 전략

MVP에서는 별도 패키지가 필요 없는 System.Text.Json을 사용합니다. UI 스레드에서 상태를 스냅샷으로 저장하며, 임시 파일 작성 후 File.Replace로 교체합니다. 기존 파일은 .bak에 남깁니다. 지연 저장과 종료 시 저장을 함께 사용합니다. 대량 카드에서 저장 비용이 커지면 SQLite 트랜잭션과 증분 저장으로 전환합니다.

## 파일 읽기

폴더 열거는 Task.Run으로 수행하며 카드별 취소 토큰을 사용합니다. 오래된 읽기 결과는 폐기합니다. UI 스레드에서 목록을 갱신합니다. FileSystemWatcher 알림은 600ms로 합치고, 45초마다 재확인합니다. 디렉터리 접근 실패는 카드 상태로 표시하며 재연결과 재시도를 제공합니다.

## MVP의 한계

DataGrid 행은 가상화합니다. ViewportCardHost는 경량 슬롯을 유지하면서 화면 근처에서만 CardView와 폴더 목록을 유지합니다. FolderListing은 점진 결과 표시와 동시 읽기 2개 제한을 제공하며, 결과 교체 알림은 배치 처리합니다. 화면에 보이는 초대형 폴더의 전체 메타데이터는 아직 메모리에 보관하므로 페이지 처리는 후속 최적화입니다. 저장은 소규모 MVP에 맞춰 UI 스레드에서 처리합니다. 아이콘은 ShellIcons가 SHGetFileInfo/USEFILEATTRIBUTES로 조회하고 확장자별로 캐시합니다. 이미지 동결 및 HICON 해제로 재사용하며 실제 파일 내용은 읽지 않습니다. 설치된 연결 프로그램이 없는 형식은 Windows 기본 아이콘으로 표시합니다. EXE별 사용자 지정 아이콘이나 바로가기 대상 아이콘까지 추적하지는 않습니다.

FolderGridPanel은 2열로 측정/배치하고 행마다 더 큰 카드 높이를 사용합니다. MainViewModel은 원래 Cards 저장 모델을 유지하면서 Folders/Favorites 컬렉션으로 UI를 분리합니다. ExplorerViewModel은 취소 가능한 비동기 목록 읽기와 지연 로딩 트리를 담당합니다. 파일은 가상화된 1열 ListBox로 표시합니다. 이전 자유 배치 X/Y/Width는 저장 호환성용이며 화면에서 사용하지 않습니다. 애니메이션 순서 변경과 작업공간 탭은 구현됐으며, 확대 모드는 후속 단계입니다.


## 순서 / 공통 높이 / 열 설정 / 로고

폴더와 파일 순서는 Folders/Favorites 컬렉션에서 Move하고, 저장 시 그 순서를 직렬화합니다. 경로가 같은 항목도 Guid로 구분합니다. 드래그에는 전용 PinShelf.ItemOrder 형식을 사용하여 파일 시스템 드롭 등록과 구분합니다. 대상의 앞/뒤 위치를 Adorner로 표시하고 가장자리 자동 스크롤을 지원합니다.

WorkspaceState의 FolderHeight, ShowModified/ShowSize/ShowType/ShowCreated/ShowExtension은 이전 설정에 없으면 기본값을 사용합니다. 높이는 펼친 카드 전체에 일괄 적용하며, 열은 DataGridColumn의 Visibility를 공유 VM에 바인딩합니다. 생성일은 비동기 파일 열거 시 함께 읽으며 유형은 Windows Shell 조회 결과를 확장자별 캐시합니다.

## 검색과 최근 기록

SearchQuery는 토큰화 및 재귀 하강 파서로 NOT/AND/OR, 암시적 AND, 괄호, 인용 문자열을 처리합니다. 깊이 64단계 및 입력 4096자 한도를 두며 오류 위치를 표시합니다. 파일 본문은 읽지 않습니다.

SearchService는 작업 스레드에서 드라이브를 순회합니다. 재분석 지점 디렉터리를 제외해 링크 순환을 피하고 권한 오류를 기록합니다. 최대 128개 일치 항목 또는 약 200ms 단위로 UI 배치를 await하여 Dispatcher 큐 폭증을 방지합니다. SearchViewModel의 세대 번호로 취소/재검색의 오래된 결과를 차단합니다. 결과 DataGrid는 행 가상화하며 결과 메타데이터는 메모리에 유지합니다. 광범위한 검색의 결과 수가 매우 클 때 메모리 사용량은 증가합니다. 현재 기본 검색은 영속 SQLite 인덱스와 메모리 캐시를 사용하는 LocalSearchIndex로 처리합니다.

FileService.FileOpened는 기본 연결 실행 요청이 성공한 파일에 한해 발생합니다. MainViewModel은 경로 기준 중복 제거 후 최근 100개를 저장합니다. VM Dispose 시 구독을 해제합니다. 최근 항목의 아이콘은 기존 ShellIcons 캐시를 사용하며 최근 파일 영역은 별도 가상화 ListBox입니다.

## 영속 인덱스와 실시간 검색 (현재 구현)

앞선 매번 파일 시스템을 순회한다는 설명은 초기 버전 기록입니다. 현재 사용자 검색 경로는 LocalSearchIndex → IndexDatabase(winsqlite3.dll)이며 SearchService는 제한된 범위 테스트/비교 용도로 남아 있습니다.

SQLite WAL을 사용하고 경로별 메타데이터·대문자 검색 키·한글 초성 키를 저장합니다. SQL은 SearchQuery의 동일 문법 트리에서 생성하며 값은 바인딩합니다. 생성/수정/삭제/이름변경 이벤트는 경로별로 합쳐 반영합니다. 새 폴더/이동된 폴더는 하위 내용을 수집합니다. 시작 시 기존 인덱스를 조회 가능하게 연 뒤 전체 메타데이터를 재확인하여 오프라인 변경을 보완하고, 달라진 DB 행만 갱신합니다.

SearchViewModel은 120ms DispatcherTimer, 이전 요청 취소, 세대 번호, 최초 200개 및 더 보기로 입력 지연과 UI 생성 비용을 제한합니다. 조회는 메타데이터 DB의 LIKE 조건을 사용하며 모든 부분문자열을 별도의 역색인으로 최적화한 구조는 아닙니다. 초대형 데이터의 응답 지연은 추가 측정 대상입니다.

PaneLayout은 왼쪽/오른쪽 폭, 트리 높이, 오른쪽 세로 분할 비율을 JSON에 저장합니다. GridSplitter DragCompleted와 정상 종료에서 저장하고 작은 창에서는 최소 폭을 보장하도록 보정합니다.


## 검색 인덱스 관리와 세대 교체
LocalSearchIndex는 활성 SearchIndexGeneration과 재구축 후보를 관리한다. 각 세대는 독립 SQLite DB, 검색 캐시, 감시 큐를 가진다. index-settings.json은 사용자가 저장한 범위, index-current.json은 완료된 활성 세대와 해당 범위를 기록한다. 새 세대의 최초 스캔 완료 전에는 활성 검색을 바꾸지 않는다. 포인터 저장 실패·취소·대상 드라이브 분리 시 기존 세대를 유지한다. 기존 세대의 조회 참조가 끝난 뒤에만 이전 생성 캐시를 삭제한다. 최초 레거시 DB는 호환성을 위해 유지한다.
관리 창은 모달이 아니며 메인 검색을 차단하지 않는다. 제외 경로는 스캔 및 FileSystemWatcher 큐에 동일하게 적용한다. 오류 경로는 이유와 함께 중복 제거해 최대 200개 표시하며 성공한 재확인 후 제거한다. UI는 500ms 간격으로 관리 상태를 갱신한다.


FolderListing의 초기 128개 미리보기와 이후 지수적으로 커지는 배치는 Dispatcher.Background로 전달하며 완료를 await한다. CancellationToken과 경로 확인으로 이전 폴더 결과가 새 목록에 섞이지 않게 한다. CardView는 목록 교체 전후에 선택 경로와 스크롤을 유지한다. 화면 밖 목록은 해제하되 CardState 및 소량의 선택 경로는 유지한다. 기존 FolderGridPanel의 모든 경량 슬롯을 유지하므로 2열 자동 배치와 드래그 슬롯 계산은 변하지 않는다.
