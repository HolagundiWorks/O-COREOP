# O-CoreOp WinUI shell (OSAS)

**Status:** HCW neumorphic · staff geography · **Updated:** 2026-08-10  
**App:** `BBSDesktop/BBSApp` (assembly `O-CoreOp` · display **O-CoreOp**)  
**Canon:** esti [`DESKTOP-WINUI-UX.md`](https://github.com/HolagundiWorks/aorms/blob/main/docs/esti/DESKTOP-WINUI-UX.md) · [`HCW-UI-UX-PRINCIPLES.md`](https://github.com/HolagundiWorks/aorms/blob/main/docs/esti/HCW-UI-UX-PRINCIPLES.md)

## Branding

| Field | Value |
| --- | --- |
| App name | **O-CoreOp** |
| For | Quantities, estimates and billing |
| Suite | OSAS, the Open Source Architecture Suit |
| Operator | Human Centric Works · Hospet (HCW) |

## Structure (the same as O-Studio)

```text
Caption — document title only (OS drag)
MenuBar row — File · Edit · Help
Main ribbon — a text tab for each section
Sub ribbon — a text tab for each page of the open section
Work area — page line (page · persona) · the page · status strip
Notice row and calculator row — docked above the status strip, they push the page up rather than cover it
```

| Region | Role |
| --- | --- |
| **File** | New · Open · Open company from OSAS · Company projects · Close company · Save · Save as · Save to company file · **Take-off from ShilpiDB** · Import / Export CAD · Undo last CAD import · Exit |
| **Edit** | Project / Engineering / Cost settings |
| **Help** | About |
| **Main ribbon** | One text tab per section (Project, RCC steel, Civil BOQ, Schedule, Office, Contracts, Accounts, Stores, HR & resources, Outputs). Ctrl+1 to Ctrl+9 jump to a section's first page. The open tab is the colour of the sub ribbon, so it joins it |
| **Sub ribbon** | One text tab per page of the open section. The open tab is the colour of the work area, so it joins the page without a line. No icons |
| **Status strip** | Calculator · theme switch · company chip |
| **UI scale** | 1×. The shell follows the system text size and display scale (`UiScale` in `MainWindow.xaml.cs`) |
| **Narrow windows** | Both ribbons scroll sideways when the window is narrower than their tabs. Page headers and toolbars wrap (`Hcw.Ui` `FlexBar`); the element page's section sketch gives its column back under 700 px. Page titles are level 2 headings and section headers level 3, under the level 1 page line |

O-Studio (the office app) has the same structure: a menu bar, a left pane grouped by job, and the page (O-CoreOp now uses two ribbons of text tabs instead of the left pane; the two apps are no longer the same here). Keep the two in step when either changes. The earlier ribbon, command strip, action dock and bottom taskbar were removed; New and Save are in the File menu.

Themes: `HcwTheme.xaml` · `HcwFluentBridge.xaml` · `FluentTokens.xaml`.

## Build / run (unpackaged)

```bat
msbuild BBSDesktop\BBSApp\BBSApp.csproj /p:Configuration=Release /p:Platform=x64 /p:WindowsPackageType=None
start BBSDesktop\BBSApp\bin\x64\Release\net8.0-windows10.0.19041.0\O-CoreOp.exe
```
