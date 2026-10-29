# Case study: the CPWD Delhi Schedule of Rates (DSR), its specifications, and how O-CoreOp follows them

This page records what was learned about how the CPWD DSR describes, specifies, measures and prices building work, and what O-CoreOp does with it. It is a working note for the data model, not a copy of the DSR.

## What was and was not verified

The official DSR 2023 and CPWD Specifications volumes were not available to read. This study rests on secondary guides, one training deck and an official letter, and these **disagree on chapter numbers**:

- One guide lists DSR 2023 civil sub-heads in two volumes as Earthwork, Mortars, Concrete Work (PCC), RCC Work, Brickwork, Stone Work, Marble/Granite Work, Wood Work, Steel & Iron Work, Flooring, Roofing, Finishing, Repairs, Dismantling, Aluminium Work, Water Proofing, Horticulture, Sanitary, Water Supply and Drainage, then roads and specialist works ([infralens DSR 2023 guide](https://infralens.in/knowledge/dsr-2023-complete-guide-civil-em-walkthrough)).
- A CPWD-based training deck numbers the *Specifications* as 2.0 Earth Work, 3.0 Mortars, 4.0 Concrete Work in Volume 1 and 13.0 Finishing, 14.0 Repairs, 15.0 Dismantling, 16.0 Road Work, 17.0 Sanitary, 18.0 Water Supply, 19.0 Drainage, 20.0 Pile Work, 21.0 Aluminium Work, 22.0 Water Proofing, 23.0 Horticulture in Volume 2 ([slides](https://www.slideshare.net/slideshow/water-proofing-treatement/40417602)).
- The Railway Board adopted the CPWD DSR 2023 and CPWD Specifications for building and road works in 2025 and names the DSR as Volumes I and II plus the Analysis of Rates volumes ([letter, 03.07.2025](https://indianrailways.gov.in/railwayboard/uploads/directorate/civil_engg/2025/2025_07_03%20Adoption%20of%20CPWD%20Delhi%20Schedule%20of%20Rates-2023%20(2).pdf)); a further instruction deals with the material specifications of DSR items ([letter, 27.11.2024](https://indianrailways.gov.in/railwayboard/uploads/directorate/civil_engg/2024/2024_11_27%20Instructions%20regarding%20the%20Specifications%20of%20materials%20of%20CPWD-DSR%20items%20realted%20to%20Building%20work%2C%20Road%20works%20%20Horticulture%20works.pdf)).

So O-CoreOp **keeps sub-head names but no chapter or item numbers**: those belong to the user's own copy of the DSR, which the rate book (below) carries. Nothing in the app quotes a DSR rate.

## How the DSR is built

| Layer | What it is | What it means for the app |
|---|---|---|
| **Sub-head (chapter)** | A family of work: earth work, concrete work, RCC, brick work, stone work, flooring, finishing, water proofing, wood/steel/aluminium work, and so on. Each is a numbered chapter. | Each component in the data model names the sub-head it is priced under. |
| **Item** | One priced description with a unit and a rate: what is provided, of what material and grade, where, how it is finished, and what is *included* or *paid separately*. | The rate book holds items by code, description, unit and rate; the estimate prices a component on its item. |
| **Specification** | The CPWD Specifications say how the item is executed and which materials, by Bureau of Indian Standards code, it uses. An item refers to its specification clause. | Each component lists its materials with the standard the specification names. |
| **Analysis of rates (AOR / DAR)** | How a rate is built up from materials at the schedule of material rates, labour at the schedule of labour rates, tools, plant and water charges, and contractor's profit and overheads (15% in the guide cited above). | The rate book stores the result (a rate), not the analysis. The analysis is a later step. |
| **Measurement** | Quantities are taken as IS 1200 sets out, in the unit the item carries, with stated deductions. | Each component states its unit and rule; the calculators implement them. |

Habits of DSR wording that matter for estimating: RCC items are for **concrete only**, with centering and shuttering and reinforcement paid as separate items (by m² and by weight); masonry above half a brick is by volume and a half-brick wall by area; finishing items are by area; skirting, DPC along a wall, coping and parapet by running length.

## How O-CoreOp follows it

| DSR idea | In O-CoreOp |
|---|---|
| Sub-head | `Office.Shared.DsrCatalog.SubHeads` (names only) |
| Item wording, unit, measurement rule | `DsrCatalog.All` per component |
| Material and its standard | `DsrCatalog` materials: cement IS 269 / IS 1489 (Part 1), aggregates IS 383, water IS 456, reinforcement IS 1786 (Fe 500D), bricks IS 1077, AAC blocks IS 2185 (Part 3), concrete blocks IS 2185 (Part 1), size stone IS 1597 (Part 1), vitrified tiles IS 15622, ceramic tiles IS 13755, integral waterproofing compound IS 2645; concrete to IS 456 |
| Priced item | Rate book (`RateBookStore`), looked up by the code `EstimateCalculator.CodeForElement` gives each trade (`PL-STD`, `MSN-BRICK-M3`, ...). Import the DSR item into the rate book under that code and the estimate prices on it |
| DSR-form abstract | `DsrEstimateFormat` (Sl. No., description, L, B, H, area, volume, unit, rate, amount) |
| Measurement | Masonry: m³, or m² for a wall up to 120 mm; openings under 0.1 m² not deducted (rule "IS1200 masonry"). Plaster and paint: area with openings deducted in full and reveals optionally added (rule "IS1200 plaster/paint"). Concrete in m³, formwork in m² of contact surface, steel by weight from the bar bending schedule |
| Mortar and concrete mixes | `SchemaExport` mortars 1:3, 1:4, 1:6 and PCC 1:4:8; concrete grades M15 to M40 |

On the Data model screen, click a component to see its sub-head, the rate-book item it prices on (code, description, rate), the item wording, the unit and measurement rule, its materials with standards, and its parameters.

The standards above are the ones the CPWD specifications are known to name for these materials. They are reference data and should be checked against the specification edition a contract cites; where a material has no single standard the panel says "as the specification clause says".

## What is not done

- Chapter and item numbers are not stored; they come from the user's rate book. A field for the DSR item number on a rate-book entry would let the estimate print it.
- No analysis of rates: a rate is a number, not materials + labour + overheads.
- Mortar, tiles, paint, door and window schedules are not components of their own; their materials appear under the work that uses them.
- Items the DSR has that the data model does not: roofing, wood and steel work beyond doors and windows, repairs, dismantling, sanitary, water supply, drainage, piles, horticulture.
- Measurement thresholds follow what the calculators already do (0.1 m² for masonry); thresholds IS 1200 gives for other items are not modelled.
