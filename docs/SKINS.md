# 디자인 스킨

상단 **디자인** 선택에서 즉시 변경합니다. 선택은 workspace.json의 `SkinId`에 저장됩니다. 이전 설정에 필드가 없거나 알 수 없는 ID인 경우 Windows 11로 돌아갑니다. 카드 구성·메모·배치는 유지됩니다.

## 제공 스킨

- `windows11`: 기본값. WPF의 공식 Fluent Light 리소스/컨트롤 템플릿 기반, Windows 11의 밝은 중립 배경과 파란 강조색.
- `vercel`: 사용자 제공 Style Reference를 데스크톱 작업공간에 맞게 적용. #fafafa 배경, #171717 글자/주요 버튼, #ebebeb 경계, 6 DIP 모서리, 보통 굵기의 제목과 고정폭 메타데이터. 웹 마케팅용 대형 히어로·로고는 적용하지 않습니다.

폰트는 설치되어 있는 Geist Sans/Geist Mono를 우선 사용하고, 없으면 Inter/Segoe UI 및 Consolas/맑은 고딕으로 대체합니다. 별도 폰트 다운로드나 설치는 하지 않습니다. Windows의 파일 선택 대화상자 등 운영체제 UI는 시스템 디자인을 사용합니다.

## 스킨 추가 방법

1. `src/Mycelview/Themes/windows11.xaml`을 새 ID의 XAML 파일로 복사합니다.
2. 색상 브러시, CardRadius/ButtonRadius, UiFont/MetadataFont, HeadingWeight를 정의합니다. 기존 키를 모두 유지합니다.
3. 필요하면 Button 등 컨트롤의 암시적 스타일을 추가합니다. 공통 BaseButton은 기능과 기본 간격을 제공합니다.
4. `SkinManager.Options`에 고유 ID와 표시 이름을 추가합니다. 파일 이름은 ID와 동일하게 지정합니다.
5. `build.ps1 -Test` 및 새 테마의 화면 검증을 실행합니다.

색상은 화면 XAML에 직접 추가하지 않고 DynamicResource를 사용합니다. 스킨 전환은 활성 ResourceDictionary만 교체하며 기능 모델을 재생성하지 않습니다. 현재는 빌드에 포함된 스킨만 지원합니다. 외부 XAML 파일을 사용자가 실행 시 임의로 불러오는 기능은 없습니다.

## 검증

기본값 및 알 수 없는 ID 처리, 실제 ComboBox 선택 바인딩, 실행 중 양방향 전환, 기존 카드 데이터 보존, 저장 후 새 프로세스에서 Vercel 복원을 확인했습니다. 두 스킨의 실제 WPF 렌더링을 PNG로 확인했습니다. 포인터 입력 자동화 대신 컨트롤 선택 속성과 바인딩을 사용한 통합 검증입니다.
