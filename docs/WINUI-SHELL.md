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
Left navigation pane — the sections, grouped by job; the brand sits at the top of the pane
Work area — page line (page · persona) · the page · status strip
Notice row and calculator row — docked above the status strip, they push the page up rather than cover it
Pane footer — the Pomodoro clock (shown when the pane is fully open)
```

| Region | Role |
| --- | --- |
| **File** | New · Open · Open company file · Company projects · **Take-off from ShilpiDB** · Import / Export CAD · Undo last CAD import · Save · Save as · Save to company file · Close company file · Publish project status · **AORMS: Sign in · Online projects · Push project online · Pull latest · Sign out** · Exit |
| **Edit** | Project / Engineering / Cost settings · Bind Connect |
| **Help** | About |
| **Navigation pane** | One heading per section (Project, RCC steel, Civil BOQ, Schedule, Office, Contracts, Accounts, Stores, HR & resources, Outputs) and one item per page. Ctrl+1 to Ctrl+9 jump to a section's first page |
| **Status strip** | Calculator · Bind · Flush (the earlier hub flow, to be removed) · **AORMS chip** (`AORMS · <studio> · synced / 3 unsent / offline …`) · Sync chip (earlier hub flow) · company chip |
| **UI scale** | 1×. The shell follows the system text size and display scale (`UiScale` in `MainWindow.xaml.cs`) |
| **Narrow windows** | The navigation pane is fully open from 900 px, an icon rail from 560 px, and a menu button below that. The page line moves clear of the button. Page headers and toolbars wrap (`Hcw.Ui` `FlexBar`); the element page's section sketch gives its column back under 700 px. Page titles are level 2 headings and section headers level 3, under the level 1 page line |

O-Studio (the office app) has the same structure: a menu bar, a left pane grouped by job, and the page. Keep the two in step when either changes. The earlier ribbon, command strip, action dock and bottom taskbar were removed; New, Save and Publish are in the File menu.

Themes: `HcwTheme.xaml` · `HcwFluentBridge.xaml` · `FluentTokens.xaml`.

## Build / run (unpackaged)

```bat
msbuild BBSDesktop\BBSApp\BBSApp.csproj /p:Configuration=Release /p:Platform=x64 /p:WindowsPackageType=None
start BBSDesktop\BBSApp\bin\x64\Release\net8.0-windows10.0.19041.0\O-CoreOp.exe
```
