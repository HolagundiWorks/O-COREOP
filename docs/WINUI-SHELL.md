# O-CoreOp WinUI shell (OSAS)

**Status:** compact density from O-UI · text-tab ribbons · **Updated:** 2026-10-29  
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
| **Main ribbon** | One text tab per section (Project, RCC steel, Civil BOQ, Schedule, Contracts, Stores, Outputs; Office, Accounts and HR are in O-Studio). Ctrl+1 to Ctrl+7 jump to a section's first page. The open tab is the colour of the sub ribbon, so it joins it |
| **Sub ribbon** | One text tab per page of the open section. The open tab is the colour of the work area, so it joins the page without a line. No icons |
| **Data model page** | Outputs > Data model: the work items and link rules as a graph, with an icon ribbon, a right-click *Add component* menu and a component and link inspector. See [DATA-MODEL-COMPONENTS.md](DATA-MODEL-COMPONENTS.md) |
| **Page tabs** | A page whose pivot tabs are the sub ribbon's own pages hides its pivot header (`Controls/PivotTabs.cs`) so each page is named once |
| **Take-off page bar** | A flat text-only bar (Source, Read and Annotated groups with dividers), the same look as O-Draft's take-off bar (Draw, Compute, Sync, Phases). No icons |
| **Status strip** | Calculator · theme switch · company chip |
| **UI scale** | 1×. The shell follows the system text size and display scale (`UiScale` in `MainWindow.xaml.cs`) |
| **Narrow windows** | Both ribbons scroll sideways when the window is narrower than their tabs. Page headers and toolbars wrap (`Hcw.Ui` `FlexBar`); the element page's section sketch gives its column back under 700 px. Page titles are level 2 headings and section headers level 3, under the level 1 page line |

O-Studio (the office app) has the same structure: a menu bar, a left pane grouped by job, and the page (O-CoreOp now uses two ribbons of text tabs instead of the left pane; the two apps are no longer the same here). Keep the two in step when either changes. The earlier ribbon, command strip, action dock and bottom taskbar were removed; New and Save are in the File menu.

Themes: `HcwTheme.xaml` · `HcwFluentBridge.xaml` · `FluentTokens.xaml`, over the suite scale in O-UI. **Sizes, type and corners follow the compact density in [UI-SIZING.md](UI-SIZING.md)** (26 controls, 28 tabs, 24 menu bar, 22 status strip, 12 / 13 / 18 type, 4 corners).

## Build / run (unpackaged)

```bat
msbuild BBSDesktop\BBSApp\BBSApp.csproj /p:Configuration=Release /p:Platform=x64 /p:WindowsPackageType=None
start BBSDesktop\BBSApp\bin\x64\Release\net8.0-windows10.0.19041.0\O-CoreOp.exe
```
