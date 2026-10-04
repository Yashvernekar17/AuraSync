; AuraSync Windows installer

!include "MUI2.nsh"
!include "nsDialogs.nsh"
!include "LogicLib.nsh"
!include "WinMessages.nsh"

!define APP_NAME "AuraSync"
!define APP_PUBLISHER "AuraSync"
!define APP_VERSION "0.1.0"
!define APP_EXE "AuraSync.exe"
!define DEFAULT_INSTALL_DIR "$PROGRAMFILES64\${APP_NAME}"

Name "${APP_NAME} v${APP_VERSION}"
OutFile "release\AuraSync-Setup-v${APP_VERSION}-x64.exe"
InstallDir "${DEFAULT_INSTALL_DIR}"
RequestExecutionLevel admin
ShowInstDetails nevershow
ShowUninstDetails nevershow
SetCompressor lzma

!define MUI_ICON "aurasync-icon.ico"
!define MUI_UNICON "aurasync-icon.ico"

!insertmacro MUI_PAGE_WELCOME
Page custom LegalNoticePageCreate LegalNoticePageLeave
!insertmacro MUI_PAGE_DIRECTORY
Page custom ShortcutPageCreate ShortcutPageLeave
!insertmacro MUI_PAGE_INSTFILES
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_TEXT "Launch ${APP_NAME}"
!define MUI_FINISHPAGE_RUN_FUNCTION "LaunchApp"
!define MUI_FINISHPAGE_TEXT "Installation complete. You can launch ${APP_NAME} now."
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH
!insertmacro MUI_LANGUAGE "English"

Var CreateDesktopShortcut
Var ShortcutCheckbox
Var LegalNoticeText
Var LegalNoticeCheckbox
Var LegalNoticeAccepted

Function LaunchApp
    Exec '"$WINDIR\explorer.exe" "$INSTDIR\${APP_EXE}"'
FunctionEnd

Function .onInit
    SetRegView 64
    SetShellVarContext all
    StrCpy $LegalNoticeAccepted ${BST_UNCHECKED}
    ReadRegStr $0 HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "InstallLocation"
    ${If} $0 != ""
        StrCpy $INSTDIR $0
    ${EndIf}
FunctionEnd

Function LegalNoticePageCreate
    !insertmacro MUI_HEADER_TEXT "Legal notice" "Review the notice and acknowledge before continuing."
    nsDialogs::Create 1018
    Pop $0
    ${If} $0 == error
        Abort
    ${EndIf}

    nsDialogs::CreateControl EDIT ${DEFAULT_STYLES}|${WS_TABSTOP}|${ES_MULTILINE}|${ES_AUTOVSCROLL}|${ES_READONLY}|${WS_VSCROLL} ${WS_EX_WINDOWEDGE}|${WS_EX_CLIENTEDGE} 0 0 100% 78% ""
    Pop $LegalNoticeText
    ${NSD_SetText} $LegalNoticeText "AURASYNC LEGAL NOTICE$\r$\n$\r$\nThis page is an informational acknowledgment, not an AuraSync license agreement. The repository supplies no project-wide license; installing this application does not grant rights to use, modify, or redistribute AuraSync project code.$\r$\n$\r$\nThird-party software remains subject to its own licenses and terms. Review THIRD-PARTY-NOTICES.md and the license files installed with the application before use or redistribution.$\r$\n$\r$\nYou are responsible for your use of the software and for reviewing applicable laws, regulations, licenses, and service terms. To the maximum extent permitted by applicable law, the software is provided as is and AuraSync does not accept responsibility for damages, losses, or other consequences arising from use or misuse. Nothing here excludes a right or liability that cannot lawfully be excluded or limited."

    ${NSD_CreateCheckbox} 0 80% 100% 20u "I acknowledge that I have read this notice. This acknowledgment does not grant an AuraSync license."
    Pop $LegalNoticeCheckbox
    ${NSD_AddStyle} $LegalNoticeCheckbox ${BS_MULTILINE}
    ${If} $LegalNoticeAccepted == ${BST_CHECKED}
        ${NSD_Check} $LegalNoticeCheckbox
    ${EndIf}
    ${NSD_OnClick} $LegalNoticeCheckbox LegalNoticePageToggle

    GetDlgItem $0 $HWNDPARENT 1
    EnableWindow $0 $LegalNoticeAccepted
    nsDialogs::Show
FunctionEnd

Function LegalNoticePageToggle
    ${NSD_GetState} $LegalNoticeCheckbox $LegalNoticeAccepted
    GetDlgItem $0 $HWNDPARENT 1
    ${If} $LegalNoticeAccepted == ${BST_CHECKED}
        EnableWindow $0 1
    ${Else}
        EnableWindow $0 0
    ${EndIf}
FunctionEnd

Function LegalNoticePageLeave
    ${NSD_GetState} $LegalNoticeCheckbox $LegalNoticeAccepted
    ${If} $LegalNoticeAccepted != ${BST_CHECKED}
        Abort
    ${EndIf}
FunctionEnd

Function ShortcutPageCreate
    !insertmacro MUI_HEADER_TEXT "Shortcut Options" "Choose whether to create a desktop shortcut."
    nsDialogs::Create 1018
    Pop $0
    ${If} $0 == error
        Abort
    ${EndIf}

    ${NSD_CreateCheckbox} 0 0 100% 12u "Create a Desktop shortcut"
    Pop $ShortcutCheckbox
    ${NSD_Check} $ShortcutCheckbox
    nsDialogs::Show
FunctionEnd

Function ShortcutPageLeave
    ${NSD_GetState} $ShortcutCheckbox $CreateDesktopShortcut
FunctionEnd

Section "Install"
    SetRegView 64
    SetShellVarContext all

    nsExec::ExecToLog 'taskkill /IM "${APP_EXE}" /T /F'
    Sleep 1000

    SetOutPath "$INSTDIR"
    File /r "release\AuraSync\*"

    CreateDirectory "$SMPROGRAMS\${APP_NAME}"
    CreateShortCut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

    ${If} $CreateDesktopShortcut == ${BST_CHECKED}
        CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
    ${EndIf}

    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayName" "${APP_NAME}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "Publisher" "${APP_PUBLISHER}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "InstallLocation" "$INSTDIR"
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "UninstallString" '"$INSTDIR\Uninstall-${APP_NAME}.exe"'
    WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "DisplayIcon" "$INSTDIR\${APP_EXE},0"
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoModify" 1
    WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}" "NoRepair" 1

    WriteUninstaller "$INSTDIR\Uninstall-${APP_NAME}.exe"
SectionEnd

Section "Uninstall"
    SetRegView 64
    SetShellVarContext all

    nsExec::ExecToLog 'taskkill /IM "${APP_EXE}" /T /F'
    Sleep 1000

    DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_NAME}"
    Delete "$DESKTOP\${APP_NAME}.lnk"
    Delete "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk"
    RMDir "$SMPROGRAMS\${APP_NAME}"

    ; User profiles are stored in %LOCALAPPDATA%\LedSync and intentionally retained.
    RMDir /r "$INSTDIR"
SectionEnd
