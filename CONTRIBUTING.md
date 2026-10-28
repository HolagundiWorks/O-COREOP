# Contributing to AQC-Core

Thanks for your interest in improving **AQC-Core** (*Accelerated Quantity and Costing
Core*), maintained by **Human Centric Works, Hospet**. This document explains how to
contribute and the terms your contributions are made under.

By participating you agree to keep things respectful and constructive.

## Licensing of contributions (please read)

AQC-Core is **dual-licensed** — GNU AGPL v3 (Community Edition) or a commercial license
(see [LICENSING.md](LICENSING.md)). For that model to work, contributions are accepted on
**both** of the following terms:

1. **Inbound = outbound (AGPL).** Your contribution is licensed to the project and its
   users under the **GNU AGPL v3 or later**, the same license as the code you are
   modifying.

2. **Commercial re-licensing grant.** So the project can remain dual-licensed, you also
   grant **Human Centric Works** a perpetual, worldwide, non-exclusive, royalty-free,
   irrevocable license to use, reproduce, modify, sublicense and distribute your
   contribution **as part of AQC-Core under any license, including a commercial /
   proprietary license**. You retain copyright in your contribution.

You confirm points 1 and 2, and certify the origin of your work, by adding a
**`Signed-off-by`** line to every commit (the Developer Certificate of Origin, below).

> **Note:** the DCO alone certifies provenance and the AGPL grant; clause 2 above is the
> part that enables commercial re-licensing. If you are contributing on behalf of an
> employer, make sure you are authorised to grant these terms.

## Sign your commits (DCO)

Add a sign-off to each commit — Git does this for you with `-s`:

```bash
git commit -s -m "Fix retaining-wall bar length rounding"
```

This appends a line using your real name and email:

```
Signed-off-by: Jane Doe <jane@example.com>
```

Use a real name and a reachable email; anonymous or fake sign-offs are not accepted. To
sign off a series you already committed: `git rebase --signoff main`.

### Developer Certificate of Origin 1.1

```
Developer Certificate of Origin
Version 1.1

Copyright (C) 2004, 2006 The Linux Foundation and its contributors.

Everyone is permitted to copy and distribute verbatim copies of this
license document, but changing it is not allowed.


Developer's Certificate of Origin 1.1

By making a contribution to this project, I certify that:

(a) The contribution was created in whole or in part by me and I
    have the right to submit it under the open source license
    indicated in the file; or

(b) The contribution is based upon previous work that, to the best
    of my knowledge, is covered under an appropriate open source
    license and I have the right under that license to submit that
    work with modifications, whether created in whole or in part
    by me, under the same open source license (unless I am
    permitted to submit under a different license), as indicated
    in the file; or

(c) The contribution was provided directly to me by some other
    person who certified (a), (b) or (c) and I have not modified
    it.

(d) I understand and agree that this project and the contribution
    are public and that a record of the contribution (including all
    personal information I submit with it, including my sign-off) is
    maintained indefinitely and may be redistributed consistent with
    this project or the open source license(s) involved.
```

## Development setup

Requirements (see [README](README.md) for detail): Windows 10/11, .NET 8 SDK, the Windows
App SDK / WinUI workload, and CMake 3.20+ with MSVC for the C++ engine.

```bat
:: C++ engine
cd BBSDesktop
cmake -S . -B build -G Ninja
cmake --build build --config Release --target bbs_engine bbs_tests

:: WinUI app
cd BBSApp
dotnet build -c Release -p:Platform=x64
dotnet run   -c Release -p:Platform=x64
```

Please make sure the solution **builds cleanly** and the engine tests pass before opening
a pull request.

## Making changes

1. **Branch** off `main` (e.g. `feature/short-description` or `fix/short-description`).
2. Keep each PR focused; small, reviewable changes merge faster.
3. **Match the surrounding style** — the existing naming, comment density and formatting.
4. **New source files must carry the SPDX header** at the very top:

   ```csharp
   // SPDX-License-Identifier: AGPL-3.0-or-later
   // SPDX-FileCopyrightText: 2026 Your Name (or Human Centric Works, Hospet)
   ```

5. Update **[README.md](README.md)** and **[CHANGELOG.md](CHANGELOG.md)** when behaviour,
   the project file format, or the feature set changes. Add notes under `## [Unreleased]`.
6. Write clear commit messages (imperative mood, e.g. "Add…", "Fix…") and reference any
   related issue.

## Pull requests

- Open the PR against `main` with a short description of **what** changed and **why**.
- Ensure every commit is signed off (DCO). PRs without sign-off cannot be merged.
- Be ready to iterate on review feedback.

## Reporting bugs & requesting features

Open a GitHub issue at <https://github.com/HolagundiWorks/BBS/issues> with:

- what you did, what you expected, and what happened,
- your OS/version and the AQC-Core version (Help → About),
- a small sample `.bbsproj` or steps to reproduce, if possible.

Because AQC-Core produces quantities and cost figures, please include the specific numbers
or drawings involved when reporting a calculation issue.

## Security issues

**Do not** file security vulnerabilities as public issues. Email **support@hcworks.in**
privately with details and we will respond as soon as we can.

## Questions & commercial licensing

- General questions: open a GitHub issue or discussion.
- Support / commercial licensing: **support@hcworks.in** · <https://hcworks.in> (see
  [LICENSING.md](LICENSING.md)).

---

*This document is provided for convenience and is not legal advice. If the dual-license
grant above is important to your business, have it reviewed by a lawyer.*
