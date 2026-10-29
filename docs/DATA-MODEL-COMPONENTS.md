# Data model: components, parameters and rules

The Data model tab (Outputs > Data model) draws the work items of a project and how one item's quantity follows from another's. This page is the reference for what is on that screen. The code that enforces it is `BBSDesktop/Office.Shared/ComponentCatalog.cs` (the catalogue and `FlowRules`) and `BBSApp/Views/DataModelPage.cs` (the screen); tests are in `Office.Shared.Tests/ComponentCatalogTests.cs`.

## How it works

- A **main component** is a family of work (RCC structure, Masonry, Finishes, ...). A **sub component** is one work item in it (Columns, Masonry walls, Plastering, ...). Right-click the canvas > *Add component* > main component > sub component, or use the ribbon: both are text menus.
- Every component has **input parameters on its left** and **output parameters on its right**. A parameter carries one measure, shown by its colour:

| Colour | Measure | Unit | Link rule bases it supplies |
|---|---|---|---|
| Yellow | Linear | m | Length, Perimeter |
| Blue | Area | m² | Area |
| Red | Volume | m³ | Volume |
| Grey | Count | nos | Count (doors and windows only) |

- A **connection** joins one output to an input of the same colour on another component. It is stored as a link rule: *target quantity = factor × the chosen measure of the source*. Which output was used is the rule's measure, so one component can feed different items from different outputs. Example: brick masonry takes **volume (red)** in and gives out all three: its **length (yellow)** feeds skirting and DPC, its **area (blue)**, both faces, feeds plaster and paint, and its **volume (red)** prices the bricks.
- The connection is refused, with the reason in the status line, when the colours differ, the target has no input of that colour, the hierarchy forbids it, it would make an item depend on itself, or the two items are already linked. A link already in a project that breaks the hierarchy is kept but drawn red and dashed with a warning, so the estimate does not change under the user.
- Edits go straight into the project's link rules (the Item links page lists the same ones). The layout is not saved.

## Components

Ports: **in** is what the component takes; **out** is what it gives. "Measured" means its quantities come from the drawing or its own page, not from another item.

### RCC structure: measured on its own pages; a source in link rules, never a target

The derivation engine reads each member (by mark and level) from its page: **Volume** is the concrete volume, **Area** is the formwork contact area, **Length** is the member's run (beam span, column clear height, slab span, footing length, wall length). Each gives **out: Length (yellow), Area (blue), Volume (red)** and takes nothing. Pedestals, lintels and stairs give only what their pages measure (formwork area; no concrete volume yet).

| Sub component | Page | Materials (concrete and steel) |
|---|---|---|
| Columns | RCC steel > Columns | cement, sand, coarse aggregate, water, reinforcement steel |
| Beams | RCC steel > Beams | same |
| Pedestals | RCC steel > Pedestals | same |
| Lintels | RCC steel > Lintels | same |
| Slabs | RCC steel > Slabs | same |
| Footings | RCC steel > Footings | same |
| Retaining walls | RCC steel > Retaining walls | same |
| Stairs | RCC steel > Stairs | same |

### Formwork: follows the RCC automatically

| Sub component | In | Out | Notes |
|---|---|---|---|
| Shuttering | Area (blue) | Area (blue) | Always rebuilt from the RCC pages. Shown as a faint dotted link from each RCC area output; it is not a rule and cannot be drawn by hand. |

### Masonry: feeds finishes and protection

| Sub component | In | Out | Materials |
|---|---|---|---|
| Masonry walls (brick, AAC or block: chosen by the wall build) | Volume (red) | Length (yellow), Area (blue), Volume (red) | bricks, AAC blocks, cement blocks; mortar 1:6 (cement, sand) |
| Size stone masonry | Volume (red) | Length, Area, Volume | size stone |

### Earthwork and bed

| Sub component | In | Out | Materials |
|---|---|---|---|
| Earthwork | none (measured) | Area (blue), Volume (red) | none |
| PCC bed | Area (blue) | Area, Volume | PCC 1:4:8 (cement, sand, coarse aggregate) |

### Finishes: applied to surfaces

| Sub component | In | Out | Materials |
|---|---|---|---|
| Plastering | Area (blue) | Area | mortar 1:3 (cement, sand) |
| Painting | Area (blue) | Area | none recorded yet |
| Flooring | Area (blue) | Area, Perimeter (yellow) | none recorded yet |
| Screed | Area (blue) | Area | none recorded yet |
| Skirting | Length (yellow) | Length | none recorded yet |
| VDF | Area (blue) | Area | none recorded yet |

### Protection and waterproofing: the end of the line

| Sub component | In | Out |
|---|---|---|
| Waterproofing | Area (blue) | Area |
| DPC | Length (yellow) | Length |
| Coping | Length (yellow) | Length |
| Parapet | Length (yellow) | Length |
| Plinth protection | Area (blue) | Area |

### Openings: counted, not linked

| Sub component | In | Out |
|---|---|---|
| Doors | none | Count (grey) |
| Windows | none | Count (grey) |

## Which main component may feed which

Rows feed columns. ✓ allowed (and then only if the colours match); blank is refused.

| From \ To | RCC | Formwork | Masonry | Earthwork | Finishes | Protection | Openings |
|---|---|---|---|---|---|---|---|
| RCC structure | | automatic only | | footings → PCC bed only | ✓ (plaster, paint, flooring, screed, VDF) | ✓ (waterproofing only) | |
| Formwork | | | | | | | |
| Masonry | | | | | ✓ | ✓ | |
| Earthwork and bed | | | | ✓ (chain below) | | | |
| Finishes | | | | | ✓ (chains below) | ✓ | |
| Protection | | | | | | | |
| Openings | | | | | | | |

Inside a main component only these chains exist:

- Earthwork → PCC bed (area)
- Plastering → Painting (area)
- Flooring → Screed (area)
- Flooring → Skirting (perimeter)

### What cannot be connected, and why

| Connection | Reason shown |
|---|---|
| RCC ↔ masonry (either way) | Separate trades measured on their own; neither is derived from the other. |
| anything → RCC | RCC is measured from the drawing; nothing can feed it. |
| RCC → skirting, DPC, coping, parapet, plinth protection | Those are applied to a wall or run along one, not to a concrete member. |
| RCC → PCC bed from anything but footings | A PCC bed sits under footings only. |
| RCC → Formwork by hand | Formwork follows the RCC automatically; no link is needed. |
| a finish → masonry, earthwork or openings | Those are measured on their own. |
| a finish or protection item → an earlier step (Painting → Plastering, Skirting → Flooring) | A finish follows the surface it is applied to, in the order the work is done. |
| Protection and waterproofing → anything | The end of the line. |
| Openings → anything | Counted, not linked. |
| a colour that the target does not take (masonry length → painting) | The target takes area only. |

## Standard relationships (the project starts with these)

| Rule | Source output | Target input | Factor |
|---|---|---|---|
| Plaster from masonry (both faces) | Masonry: Area (blue) | Plastering: Area | ×2 |
| Painting from plaster | Plastering: Area | Painting: Area | ×1 |
| Skirting from flooring perimeter | Flooring: Perimeter (yellow) | Skirting: Length | ×1 |
| Screed bedding under flooring | Flooring: Area | Screed: Area | ×1 (off by default) |
| DPC along masonry base | Masonry: Length (yellow) | DPC: Length | ×1 (off by default) |

A new connection from masonry's area to plaster or paint starts at ×2 (a wall has two faces); every other new connection starts at ×1.

## Components that stay unlinked

- **Shuttering** takes nothing by hand: the estimate rebuilds it from the RCC pages.
- **Doors and Windows** only count; they have no input and nothing in the estimate derives from a count yet.
- **Protection items** (waterproofing, DPC, coping, parapet, plinth protection) take a link but give out nothing that any item accepts: they end the line.
- **Earthwork** takes nothing in (measured); it feeds the PCC bed.

## Not done yet

- Pedestals, lintels and stairs have no concrete volume in the concrete BOQ yet, so a volume link from them derives nothing.
- Materials are listed per component from `SchemaExport` (cement, sand and so on); finishes other than plaster have no material recipe yet.
- The Item links page does not yet apply the hierarchy; only this screen does.
- Layout is not saved with the project.
