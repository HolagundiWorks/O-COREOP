# O-Metra WinUI shell (OSAS)

**Status:** HCW neumorphic · staff geography · **Updated:** 2026-08-10  
**App:** `BBSDesktop/BBSApp` (assembly `AQCCore` · display **AQC Core**)  
**Canon:** esti [`DESKTOP-WINUI-UX.md`](https://github.com/HolagundiWorks/aorms/blob/main/docs/esti/DESKTOP-WINUI-UX.md) · [`HCW-UI-UX-PRINCIPLES.md`](https://github.com/HolagundiWorks/aorms/blob/main/docs/esti/HCW-UI-UX-PRINCIPLES.md)

## Branding

| Field | Value |
| --- | --- |
| App name | **O-Metra** (formerly AQC Core) |
| Expansion | Accelerated Quantity and Costing |
| Suite | OSAS, the Open Source Architecture Suit |
| Operator | Human Centric Works · Hospet (HCW) |

## Structure (the same as AOS)

```text
Caption — document title only (OS drag)
MenuBar row — File · Edit · Help
Left navigation pane — the sections, grouped by job; the brand sits at the top of the pane
Work area — page line (page · persona) · the page · status strip
Floating widgets — toast, calculator, Pomodoro clock
```

| Region | Role |
| --- | --- |
| **File** | New · Open · Import / Export CAD · Save · Save as · Publish project status · AORMS sign-in · Exit |
| **Edit** | Project / Engineering / Cost settings · Bind Connect |
| **Help** | About |
| **Navigation pane** | One heading per section (Project, RCC steel, Civil BOQ, Schedule, Office, Contracts, Accounts, Stores, HR & resources, Outputs) and one item per page. Ctrl+1 to Ctrl+9 jump to a section's first page |
| **Status strip** | Calculator · Bind · Flush · AORMS chip · Sync chip |
| **UI scale** | Shell `ScaleHost` **0.96×** (caption 1×) |

AOS (the office app) has the same structure: a menu bar, a left pane grouped by job, and the page. Keep the two in step when either changes. The earlier ribbon, command strip, action dock and bottom taskbar were removed; New, Save and Publish are in the File menu.

Themes: `HcwTheme.xaml` · `HcwFluentBridge.xaml` · `FluentTokens.xaml`.

## Build / run (unpackaged)

```bat
msbuild BBSDesktop\BBSApp\BBSApp.csproj /p:Configuration=Release /p:Platform=x64 /p:WindowsPackageType=None
start BBSDesktop\BBSApp\bin\x64\Release\net8.0-windows10.0.19041.0\AQCCore.exe
```
