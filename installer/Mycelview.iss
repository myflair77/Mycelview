#ifndef AppVersion
#define AppVersion "0.1.0"
#endif
#ifndef StageDir
#define StageDir "..\artifacts\installer-stage"
#endif
[Setup]
AppId={{924475B5-7DC8-4E58-8391-6FB868B792F3}
AppName=Mycelview
AppVersion={#AppVersion}
AppPublisher=Mycelview
AppPublisherURL=https://github.com/myflair77/Mycelview
DefaultDirName={localappdata}\Programs\Mycelview
DefaultGroupName=Mycelview
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=..\artifacts\releases
OutputBaseFilename=Mycelview-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\Mycelview\Assets\Mycelview.ico
UninstallDisplayIcon={app}\Mycelview.exe
Compression=lzma2
SolidCompression=yes
LZMANumBlockThreads=2
WizardStyle=modern
CloseApplications=no
RestartApplications=no
DisableProgramGroupPage=yes
InfoAfterFile=finished.ko.txt
[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
[Tasks]
Name: "desktopicon"; Description: "바탕화면에 Mycelview 바로가기 만들기"; Flags: unchecked
[Files]
Source: "{#StageDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "{#StageDir}\browser-host\*"; DestDir: "{app}\browser-host"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"
Source: "..\browser-extension\manifest.json"; DestDir: "{app}\browser-extension"; Flags: ignoreversion
Source: "..\browser-extension\background.js"; DestDir: "{app}\browser-extension"; Flags: ignoreversion
Source: "..\browser-extension\popup.html"; DestDir: "{app}\browser-extension"; Flags: ignoreversion
Source: "..\browser-extension\popup.js"; DestDir: "{app}\browser-extension"; Flags: ignoreversion
Source: "..\browser-extension\extension-id.txt"; DestDir: "{app}\browser-host"; Flags: ignoreversion
Source: "browser-guide.html"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\Mycelview"; Filename: "{app}\Mycelview.exe"
Name: "{group}\브라우저 탭 연결 안내"; Filename: "{app}\browser-guide.html"
Name: "{autodesktop}\Mycelview"; Filename: "{app}\Mycelview.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\Mycelview.exe"; Description: "Mycelview 실행"; Flags: nowait postinstall skipifsilent
Filename: "{app}\browser-guide.html"; Description: "브라우저 확장 추가 안내 열기"; Flags: shellexec postinstall skipifsilent
Filename: "{win}\explorer.exe"; Parameters: """{app}\browser-extension"""; Description: "확장 프로그램 폴더 열기"; Flags: nowait postinstall skipifsilent unchecked
[UninstallRun]
Filename: "{app}\browser-host\Mycelview.BrowserHost.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "UnregisterBrowserHost"
[UninstallDelete]
Type: files; Name: "{app}\browser-host\native-host.json"
[Code]
var RegistrationFailed: Boolean;
function GetCustomSetupExitCode: Integer;
begin if RegistrationFailed then Result := 10 else Result := 0; end;
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if CheckForMutexes('Local\PinShelf-' + GetUserNameString) then
    Result := '실행 중인 Mycelview를 종료한 뒤 다시 시도해 주세요. 설정과 즐겨찾기는 유지됩니다.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    RegistrationFailed := True;
    if not Exec(ExpandConstant('{app}\browser-host\Mycelview.BrowserHost.exe'), '--register', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      RaiseException('브라우저 연결 프로그램을 실행하지 못했습니다. 설치를 다시 실행해 주세요.');
    if ExitCode <> 0 then
      RaiseException('브라우저 연결 등록에 실패했습니다. 오류 코드: ' + IntToStr(ExitCode));
    RegistrationFailed := False;
  end;
end;