# UI sizing

O-CoreOp follows the suite's compact density, set in the shared UI library **O-UI** (`O.Ui`, `Themes/SuiteStyles.xaml` and `SuiteScale`) and taken from O-Draft's shell. O-Studio, O-Draft and O-CoreOp use the same numbers. This page lists them and says where O-CoreOp reads them from. When the library changes, change these lists to match; do not set a size in a page when a token exists.

## The scale

All values are DIPs.

| Token (O-UI) | Value | Used for |
|---|---|---|
| `SuiteControlMinHeight` / `SuiteToolTile` | 26 | Buttons, text boxes, combo boxes, number boxes, text tools |
| `SuiteControlHeightCompact` | 22 | Menu and list rows, dense toolbar buttons, port rows on the data-model canvas |
| `SuiteMenuBarHeight` / `SuiteMenuItemHeight` | 24 / 22 | Menu bar, menu items |
| `SuiteTabHeight` | 28 | Ribbon tabs (main and sub) |
| `SuiteStatusBarHeight` | 22 | Status strip |
| `SuiteHeaderHeight` | 32 | Caption (title) bar |
| `SuitePanelHeaderHeight` / `SuitePropertyRowHeight` | 24 / 26 | Panel headers; table rows and column headers (`ResultTable`) |
| `SuiteFontCaption` / `SuiteFontBody` / `SuiteFontSection` / `SuiteFontTitle` | 11 / 12 / 13 / 18 | Captions and group names; body, controls, tabs and table cells; section headings and the page line; page titles |
| `SuiteIconNav` / `SuiteIconInline` | 16 | Icons beside text |
| `Space1` to `Space6` | 4, 8, 12, 16, 20, 24 | Spacing, on a 4 px base |
| `SuiteStackGap` / `SuiteFieldGap` / `SuiteCardGap` / `SuiteSectionGap` | 8 / 8 / 12 / 16 | Gaps inside a stack, between fields, between cards, between sections |
| `SuiteContentPadding` / `SuiteCardPadding` / `SuiteControlPadding` / `SuiteDialogPadding` | 12,8 / 12,8 / 8,2 / 12 | Content, cards, controls, dialogs |
| `PageMargin` | 16,12,16,16 | Page margin |
| `SuiteCornerControl` / `Button` / `Card` / `Chip` | 4 | Every corner |
| `SuitePaneWidth` / `SuiteRailWidth` | 240 / 56 | Side panes (the data-model inspector is wider, 300, because it holds a specification) |

## Where O-CoreOp reads them

| What | How |
|---|---|
| Resources | `App.xaml` merges `O.Ui/Themes/SuiteStyles.xaml`, then the app's `OUiTheme.xaml`, `OUiFluentBridge.xaml` and `FluentTokens.xaml`. The last two re-state the suite values under the app's older key names (`ControlMinHeight`, `ControlFontSize`, `BodyFontSize`, `TitleFontSize`, `SectionFontSize`, `Radius*`, `PagePadding`, `CardPadding`, `HitTaskbar`, `HitDock` and so on) so pages written against them follow the scale. They must hold the same numbers as the table above |
| Styles | Implicit `Button`, `TextBox`, `ComboBox`, `NumberBox`, `MenuFlyoutItem`, `MenuBar`, `NavigationViewItem` and `ComboBoxItem` styles in `FluentTokens.xaml`; the legacy `Hcw*` button, input and card styles in `OUiTheme.xaml` (brush keys kept for compatibility) |
| Code-built UI | `O.Ui.SuiteScale` (`FontBody`, `FontCaption`, `FontSection`, `ControlMinHeight`, `TabHeight`, `PropertyRowHeight`, `CornerControl`). Used by the ribbons (`MainWindow.xaml.cs`), the take-off bar (`TakeoffPage.cs`), the tables (`ResultTable.cs`) and the data-model screen (`DataModelPage.cs`) |
| Shell | `MainWindow.xaml` uses `SuiteMenuBarHeight`, `SuiteFontBody`, `SuiteFontSection` and `SuiteStatusBarHeight` |

## What changed from the earlier O-CoreOp sizes

| | Before | Now |
|---|---|---|
| Controls (buttons, inputs) | 35 to 44 | 26 |
| Primary and dock buttons | 44 | 26 and 32 |
| Menu bar | 36 | 24 |
| Ribbon tabs | 30 to 35 | 28 |
| Status strip | 44 | 22 |
| Table rows / headers | 32 / 36 | 26 / 26 |
| Body / caption / section / title type | 13 / 12 / 15 / 22 | 12 / 11 / 13 / 18 |
| Corner radius | 2 | 4 |
| Page padding / card padding | 12,10 / 12,10 | 16,12,16,16 / 12,8 |

## Components

| Component | Rule |
|---|---|
| Ribbon tab | Text only, 28 high, 12 type, 12 side padding; the open tab is one pixel taller and the colour of what it joins |
| Text tool (take-off bar) | Flat, 26 high, 12 type, 4 corners |
| Icon ribbon button (data model) | Icon over a caption of 11 type, 52 high, 90 wide; the caption trims with an ellipsis and the tooltip gives the full name |
| Table | Rows and headers 26, text 12, grid lines on |
| Canvas node (data model) | 214 wide; header 30; one 22 row per parameter; port dots 12 across; names 11 type |
| Menus | Text only, 22 per item |

## Not covered

- Canvas geometry (node width, gaps) is drawing, not chrome, and is not scaled.
- Screens not opened since the change (element pages, estimate, settings) were not checked for clipping at the smaller sizes; the Dashboard, Stores, Quantities and Data model screens were.
