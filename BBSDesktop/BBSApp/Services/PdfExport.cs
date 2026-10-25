// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BBSApp.Services;

/// <summary>PDF reports and purchase orders (QuestPDF).</summary>
public static class PdfExport
{
    static PdfExport()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static bool ExportProjectReport(
        string path,
        ProjectStore store,
        out string? error,
        AnnotatedDrawing? annotatedDrawing = null,
        IReadOnlyList<RenderedAnnotatedPdf>? storedAnnotated = null)
    {
        error = null;
        try
        {
            var sections = BuildReportSections(store);
            bool hasSteel = store.Columns.Count + store.Pedestals.Count + store.Beams.Count + store.Lintels.Count > 0;
            bool hasDrawing = annotatedDrawing is not null
                              || !string.IsNullOrWhiteSpace(store.Takeoff.PdfPath);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Calibri));

                    page.Header().Element(c => ReportHeader(c, store, "Project report"));
                    page.Footer().Element(c => ReportFooter(c, store));

                    page.Content().Column(col =>
                    {
                        col.Spacing(14);
                        if (sections.Count == 0 && !hasDrawing && storedAnnotated is not { Count: > 0 })
                        {
                            col.Item().Text("No element data in this project. Add RCC or civil BOQ items, then export again.");
                            return;
                        }

                        foreach (var sec in sections)
                            col.Item().Element(c => SectionBlock(c, sec));

                        if (hasSteel)
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => SketchPdf.DrawSteelArrangementSketches(c, store));
                        }

                        if (hasDrawing)
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => TakeoffAnnotatedPdf.Draw(c, store, annotatedDrawing));
                        }

                        // annotated drawings O-Draft generated and O-CoreOp stored (quantity lines, measurements, hatches)
                        if (storedAnnotated is { Count: > 0 })
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => StoredAnnotatedPdf.Draw(c, storedAnnotated));
                        }
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ExportPurchaseOrder(
        string path,
        ProjectStore store,
        IReadOnlySet<string> levels,
        IReadOnlyList<PoLine> steel,
        IReadOnlyList<PoLine> concreteByGrade,
        IReadOnlyList<ConcreteLine> concreteDetail,
        IReadOnlyList<PoLine> materials,
        bool rmcMode,
        out string? error)
    {
        error = null;
        try
        {
            var levelLabel = levels.Count == 0
                ? "none"
                : levels.Count == store.Levels.Count && store.Levels.Count > 0
                    ? "all storeys"
                    : string.Join(", ", levels.OrderBy(x => x));

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Calibri));

                    page.Header().Element(c => ReportHeader(c, store,
                        $"Purchase order · {levelLabel}" + (rmcMode ? " · RMC" : " · site batch")));
                    page.Footer().Element(c => ReportFooter(c, store));

                    page.Content().Column(col =>
                    {
                        col.Spacing(14);

                        col.Item().Element(c => TableSection(c, "Steel purchase order",
                            new[] { "Category", "Item", "Unit", "Qty", "Notes" },
                            steel.Select(p => new[]
                            {
                                p.Category, p.Item, p.Unit, p.Qty.ToString("0.##"), p.Notes
                            }).ToList()));

                        col.Item().Element(c => TableSection(c,
                            rmcMode ? "Concrete by grade (RMC m³)" : "Concrete by grade (m³)",
                            new[] { "Category", "Item", "Unit", "Qty", "Notes" },
                            concreteByGrade.Select(p => new[]
                            {
                                p.Category, p.Item, p.Unit, p.Qty.ToString("0.###"), p.Notes
                            }).ToList()));

                        if (!rmcMode)
                        {
                            col.Item().Element(c => TableSection(c, "Concrete BOQ detail (by element)",
                                new[] { "Storey", "Element", "Mark", "Grade", "Vol m³", "Cement bags", "Sand m³", "Agg m³" },
                                concreteDetail.Select(p => new[]
                                {
                                    p.Level, p.Element, p.Mark, p.Grade,
                                    p.VolumeM3.ToString("0.###"),
                                    p.CementBags.ToString("0.##"),
                                    p.SandM3.ToString("0.###"),
                                    p.AggregateM3.ToString("0.###")
                                }).ToList()));
                        }
                        else
                        {
                            col.Item().Element(c => TableSection(c, "Concrete detail by element (volume only)",
                                new[] { "Storey", "Element", "Mark", "Grade", "Vol m³" },
                                concreteDetail.Select(p => new[]
                                {
                                    p.Level, p.Element, p.Mark, p.Grade, p.VolumeM3.ToString("0.###")
                                }).ToList()));
                        }

                        col.Item().Element(c => TableSection(c,
                            rmcMode
                                ? "Other materials (civil — bricks, mortar, etc.; no RCC batching)"
                                : "Other materials (RCC batching + civil)",
                            new[] { "Category", "Item", "Unit", "Qty", "Notes" },
                            materials.Select(p => new[]
                            {
                                p.Category, p.Item, p.Unit, p.Qty.ToString("0.###"), p.Notes
                            }).ToList()));
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ExportEstimate(
        string path,
        ProjectStore store,
        EstimateResult result,
        IReadOnlySet<string> levels,
        out string? error,
        AnnotatedDrawing? annotatedDrawing = null,
        IReadOnlyList<RenderedAnnotatedPdf>? storedAnnotated = null)
    {
        error = null;
        try
        {
            var levelLabel = levels.Count == 0
                ? "none"
                : levels.Count == store.Levels.Count && store.Levels.Count > 0
                    ? "all storeys"
                    : string.Join(", ", levels.OrderBy(x => x));

            bool hasSteel = store.Columns.Count + store.Pedestals.Count + store.Beams.Count + store.Lintels.Count > 0;
            bool hasDrawing = annotatedDrawing is not null
                              || !string.IsNullOrWhiteSpace(store.Takeoff.PdfPath);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(7).FontFamily(Fonts.Calibri));

                    page.Header().Element(c => ReportHeader(c, store,
                        $"Abstract of cost (DSR) · {result.RateBookVersionName} · {levelLabel} · ₹ {result.GrandTotal:N2}"));
                    page.Footer().Element(c => ReportFooter(c, store));

                    page.Content().Column(col =>
                    {
                        col.Spacing(12);

                        var headers = DsrEstimateFormat.Headers;
                        int sl = 1;
                        void AddSection(string title, IReadOnlyList<EstimateLine> lines)
                        {
                            var rows = DsrEstimateFormat.ToStringRows(lines, sl);
                            sl += rows.Count;
                            col.Item().Element(c => TableSection(c, title, headers, rows));
                        }

                        AddSection("I. Civil / finishes / doors / windows", result.Civil);
                        AddSection("II. Materials", result.Materials);
                        AddSection("III. Steel reinforcement", result.Steel);

                        var mk = result.Markups;
                        col.Item().PaddingTop(4).Text($"Base total (₹) : {mk.BaseTotal:N2}").FontSize(9);
                        col.Item().Text($"Electrical {mk.ElectricalPct:0.##}% : ₹ {mk.ElectricalAmount:N2}").FontSize(8);
                        col.Item().Text($"Plumbing {mk.PlumbingPct:0.##}% : ₹ {mk.PlumbingAmount:N2}").FontSize(8);
                        col.Item().Text($"Escalation {mk.EscalationPct:0.##}% : ₹ {mk.EscalationAmount:N2}").FontSize(8);
                        col.Item().Text($"Consulting / PMC fees {mk.ConsultingFeePct:0.##}% : ₹ {mk.ConsultingFeeAmount:N2}").FontSize(8);
                        col.Item().PaddingTop(4).Text($"Grand total (₹) : {result.GrandTotal:N2}").Bold().FontSize(11);
                        if (result.MissingCodes.Count > 0)
                            col.Item().Text("Missing rates: " + string.Join(", ", result.MissingCodes)).FontColor(Colors.Red.Medium);

                        if (hasSteel)
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => SketchPdf.DrawSteelArrangementSketches(c, store));
                        }

                        if (hasDrawing)
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => TakeoffAnnotatedPdf.Draw(c, store, annotatedDrawing));
                        }

                        // annotated drawings O-Draft generated and O-CoreOp stored (quantity lines, measurements, hatches)
                        if (storedAnnotated is { Count: > 0 })
                        {
                            col.Item().PageBreak();
                            col.Item().Element(c => StoredAnnotatedPdf.Draw(c, storedAnnotated));
                        }
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ExportSchedule(string path, ProjectStore store, out string? error)
    {
        error = null;
        try
        {
            var schedule = store.Schedule;
            var result = ScheduleCalculator.Compute(schedule);
            var headers = new[] { "#", "Activity", "WBS", "Dur (d)", "Start", "Finish", "Total float", "Critical", "Preds" };
            var rows = new List<string[]>();
            for (int i = 0; i < schedule.Activities.Count; i++)
            {
                var a = schedule.Activities[i];
                string preds = string.Join(", ", a.Links
                    .Select(l => schedule.Find(l.PredecessorId))
                    .Where(p => p is not null)
                    .Select(p => schedule.IndexOf(p!).ToString()));
                rows.Add(new[]
                {
                    (i + 1).ToString(),
                    a.Name,
                    a.Wbs,
                    a.DurationDays.ToString("0.#"),
                    schedule.DateForOffset(a.EarlyStart).ToString("dd MMM yyyy"),
                    schedule.DateForOffset(a.EarlyFinish).ToString("dd MMM yyyy"),
                    a.InCycle ? "—" : a.TotalFloat.ToString("0.#"),
                    a.InCycle ? "cycle" : a.IsCritical ? "Yes" : "",
                    preds
                });
            }

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Calibri));
                    page.Header().Element(c => ReportHeader(c, store,
                        $"Project schedule (CPM) · {result.ProjectDurationDays:0.#} working days · finish {result.FinishDate:dd MMM yyyy}"));
                    page.Footer().Element(c => ReportFooter(c, store));
                    page.Content().Column(col =>
                    {
                        col.Spacing(12);
                        col.Item().Text(
                            $"Start {schedule.StartDate:dd MMM yyyy} · {schedule.WorkingDaysPerWeek}-day week · "
                            + $"{result.ActivityCount} activities · {result.CriticalCount} on critical path"
                            + (result.HasCycle ? "  ·  WARNING: circular dependency present" : "")).FontSize(9);
                        col.Item().Element(c => TableSection(c, "Activity schedule", headers, rows));
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ExportOfficeDocument(string path, ProjectStore store, OfficeDocument doc, out string? error)
    {
        error = null;
        try
        {
            var info = store.Info;
            var party = store.Parties.For(doc.IssuedByRole);
            string company = party.CompanyDisplay(info.CompanyDisplay);
            string address = string.IsNullOrWhiteSpace(party.Address) ? info.Address : party.Address;
            string phone = string.IsNullOrWhiteSpace(party.Phone) ? info.ContactPhone : party.Phone;
            string email = string.IsNullOrWhiteSpace(party.Email) ? info.ContactEmail : party.Email;
            string regLine = string.IsNullOrWhiteSpace(party.RegistrationLine) ? info.RegistrationLine : party.RegistrationLine;
            string? logo = party.ResolvedLogoPath ?? info.ResolvedLogoPath;
            string number = string.IsNullOrWhiteSpace(doc.Number)
                ? store.Office.PreviewNumber(doc, info.CompanyDisplay) + "  (draft)"
                : doc.Number;
            string typeName = DocTypeInfo.DisplayFor(doc.TypeCode);
            string signName = !string.IsNullOrWhiteSpace(doc.SignatoryName) ? doc.SignatoryName
                : !string.IsNullOrWhiteSpace(party.SignatoryName) ? party.SignatoryName : info.PreparedByName;
            string signRole = !string.IsNullOrWhiteSpace(doc.SignatoryRole) ? doc.SignatoryRole
                : !string.IsNullOrWhiteSpace(party.SignatoryRole) ? party.SignatoryRole : info.PreparedByRole;
            bool hasTo = DocTypeInfo.HasRecipient(doc.TypeCode) && !string.IsNullOrWhiteSpace(doc.ToName);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Calibri));
                    page.Footer().Element(c => ReportFooter(c, store));
                    page.Content().Column(col =>
                    {
                        col.Spacing(10);

                        // Letterhead
                        col.Item().Row(row =>
                        {
                            if (logo is not null)
                                row.ConstantItem(64).Height(52).PaddingRight(10).AlignMiddle().Image(logo).FitArea();
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text(company).SemiBold().FontSize(16);
                                if (!string.IsNullOrWhiteSpace(address))
                                    c.Item().Text(address).FontSize(8).FontColor(Colors.Grey.Darken2);
                                var bits = new List<string>();
                                if (!string.IsNullOrWhiteSpace(phone)) bits.Add(phone);
                                if (!string.IsNullOrWhiteSpace(email)) bits.Add(email);
                                if (bits.Count > 0) c.Item().Text(string.Join("  ·  ", bits)).FontSize(8).FontColor(Colors.Grey.Darken2);
                                if (!string.IsNullOrWhiteSpace(regLine))
                                    c.Item().Text(regLine).FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                        });
                        col.Item().PaddingVertical(2).LineHorizontal(1.5f).LineColor(Colors.Grey.Darken1);

                        // Number / date
                        col.Item().PaddingTop(6).Row(row =>
                        {
                            row.RelativeItem().Text(t => { t.Span("No: ").SemiBold(); t.Span(number); });
                            row.RelativeItem().AlignRight().Text(t =>
                            {
                                t.Span("Date: ").SemiBold();
                                t.Span(doc.IssueDate.ToString("dd MMM yyyy"));
                            });
                        });

                        // Recipient
                        if (hasTo)
                        {
                            col.Item().PaddingTop(6).Text("To,").SemiBold();
                            col.Item().Text(doc.ToName);
                            foreach (var line in SplitLines(doc.ToAddress))
                                col.Item().Text(line);
                        }

                        // Subject / title
                        col.Item().PaddingTop(10).AlignCenter().Text($"{typeName}".ToUpperInvariant())
                            .SemiBold().FontSize(12).FontColor(Colors.Grey.Darken2);
                        if (!string.IsNullOrWhiteSpace(doc.Subject))
                            col.Item().PaddingTop(2).Text(t =>
                            {
                                t.Span("Subject: ").SemiBold();
                                t.Span(doc.Subject).SemiBold();
                            });

                        // Body
                        col.Item().PaddingTop(10);
                        foreach (var line in SplitLines(doc.Body))
                        {
                            if (line.Length == 0) col.Item().Height(6);
                            else col.Item().Text(line).LineHeight(1.35f);
                        }

                        // Signature
                        col.Item().PaddingTop(28).AlignRight().Column(c =>
                        {
                            c.Item().Text($"For {company}").SemiBold();
                            c.Item().Height(34);
                            if (!string.IsNullOrWhiteSpace(signName)) c.Item().Text(signName).SemiBold();
                            if (!string.IsNullOrWhiteSpace(signRole)) c.Item().Text(signRole).FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ExportContract(string path, ProjectStore store, Contract c, out string? error)
    {
        error = null;
        try
        {
            var info = store.Info;
            var party = store.Parties.For(c.IssuedByRole);
            string company = party.CompanyDisplay(info.CompanyDisplay);
            string number = string.IsNullOrWhiteSpace(c.Number)
                ? store.ContractBook.PreviewNumber(c, info.CompanyDisplay) + "  (draft)"
                : c.Number;
            string kind = Contract.KindDisplay(c.Kind);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(36);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Calibri));
                    page.Header().Element(cc => PartyHeader(cc, store, party, kind));
                    page.Footer().Element(cc => ReportFooter(cc, store));
                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(t => { t.Span("No: ").SemiBold(); t.Span(number); });
                            r.RelativeItem().AlignRight().Text(t => { t.Span("Award date: ").SemiBold(); t.Span(c.AwardDate.ToString("dd MMM yyyy")); });
                        });
                        col.Item().Text(t => { t.Span("Completion by: ").SemiBold(); t.Span(c.CompletionDate.ToString("dd MMM yyyy")); });
                        if (!string.IsNullOrWhiteSpace(c.Title))
                            col.Item().PaddingTop(4).Text(c.Title).SemiBold().FontSize(12);

                        col.Item().PaddingTop(6).Text("To (Contractor):").SemiBold();
                        col.Item().Text(string.IsNullOrWhiteSpace(c.ContractorName) ? "—" : c.ContractorName);
                        foreach (var line in SplitLines(c.ContractorAddress)) if (line.Length > 0) col.Item().Text(line);

                        if (!string.IsNullOrWhiteSpace(c.Scope))
                        {
                            col.Item().PaddingTop(6).Text("Scope of work:").SemiBold();
                            foreach (var line in SplitLines(c.Scope)) col.Item().Text(line.Length == 0 ? " " : line);
                        }

                        if (c.IsItemRate && c.Lines.Count > 0)
                        {
                            var headers = new[] { "#", "Description", "Unit", "Qty", "Rate", "Amount" };
                            var rows = new List<string[]>();
                            int i = 1;
                            foreach (var l in c.Lines)
                                rows.Add(new[] { (i++).ToString(), l.Description, l.Unit,
                                    l.Qty.ToString("0.##"), l.Rate.ToString("0.##"), l.Amount.ToString("0.##") });
                            col.Item().PaddingTop(8).Element(cc => TableSection(cc, "Schedule of quantities & rates", headers, rows));
                            col.Item().AlignRight().Text(t => { t.Span("Contract value: ").SemiBold(); t.Span("Rs. " + c.Value.ToString("N2")); });
                        }
                        else
                        {
                            col.Item().PaddingTop(8).Text(t => { t.Span("Contract value (lump sum): ").SemiBold(); t.Span("Rs. " + c.Value.ToString("N2")); });
                        }
                        col.Item().Text(t => { t.Span("Retention: ").SemiBold(); t.Span(c.RetentionPct.ToString("0.#") + " %"); });

                        if (c.Terms.Count > 0)
                        {
                            col.Item().PaddingTop(8).Text("Terms & conditions:").SemiBold();
                            int n = 1;
                            foreach (var term in c.Terms)
                                col.Item().Text($"{n++}. {term}").LineHeight(1.3f);
                        }

                        col.Item().PaddingTop(30).Row(r =>
                        {
                            r.RelativeItem().Column(cc =>
                            {
                                cc.Item().Text("For " + company).SemiBold();
                                cc.Item().Height(30);
                                cc.Item().Text("Authorised signatory").FontSize(9).FontColor(Colors.Grey.Darken2);
                            });
                            r.RelativeItem().AlignRight().Column(cc =>
                            {
                                cc.Item().Text("Accepted — " + (string.IsNullOrWhiteSpace(c.ContractorName) ? "Contractor" : c.ContractorName)).SemiBold();
                                cc.Item().Height(30);
                                cc.Item().Text("Contractor signature").FontSize(9).FontColor(Colors.Grey.Darken2);
                            });
                        });
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ExportRunningBill(string path, ProjectStore store, RunningBill b, out string? error)
    {
        error = null;
        try
        {
            var info = store.Info;
            var party = store.Parties.For(b.IssuedByRole);
            var certParty = store.Parties.Pm;   // the PM certifies the contractor's bill
            string measuredBy = !string.IsNullOrWhiteSpace(party.SignatoryName) ? party.SignatoryName : info.PreparedByName;
            string number = string.IsNullOrWhiteSpace(b.Number)
                ? store.Accounts.PreviewBillNumber(b, info.CompanyDisplay) + "  (draft)"
                : b.Number;
            string raNo = b.BillNo > 0 ? $"RA Bill No. {b.BillNo}" : "RA Bill (draft)";

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(32);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Calibri));
                    page.Header().Element(cc => PartyHeader(cc, store, party, raNo));
                    page.Footer().Element(cc => ReportFooter(cc, store));
                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(t => { t.Span("No: ").SemiBold(); t.Span(number); });
                            r.RelativeItem().AlignRight().Text(t => { t.Span("Date: ").SemiBold(); t.Span(b.Date.ToString("dd MMM yyyy")); });
                        });
                        if (!string.IsNullOrWhiteSpace(b.ContractLabel))
                            col.Item().Text(t => { t.Span("Against: ").SemiBold(); t.Span(b.ContractLabel); });
                        if (!string.IsNullOrWhiteSpace(b.Party))
                            col.Item().Text(t => { t.Span("Contractor: ").SemiBold(); t.Span(b.Party); });

                        var headers = new[] { "#", "Description", "Unit", "Rate", "Qty", "Amount" };
                        var rows = new List<string[]>();
                        int i = 1;
                        foreach (var l in b.Lines)
                            rows.Add(new[] { (i++).ToString(), l.Description, l.Unit,
                                l.Rate.ToString("0.##"), l.Qty.ToString("0.###"), l.Amount.ToString("0.##") });
                        col.Item().PaddingTop(6).Element(cc => TableSection(cc, "Measured work", headers, rows));

                        col.Item().PaddingTop(6).AlignRight().Column(c =>
                        {
                            void Line(string label, double val, bool strong = false)
                            {
                                c.Item().Row(r =>
                                {
                                    var lab = r.ConstantItem(220).Text(label);
                                    var amt = r.ConstantItem(120).AlignRight().Text("Rs. " + val.ToString("N2"));
                                    if (strong) { lab.SemiBold(); amt.SemiBold(); }
                                });
                            }
                            Line("Gross value of work done", b.Gross);
                            if (b.GstPct != 0) Line($"Add: GST @ {b.GstPct:0.#}%", b.Gst);
                            if (b.GstPct != 0) Line("Invoice total (incl. GST)", b.Invoice, strong: true);
                            Line($"Less: Retention @ {b.RetentionPct:0.#}%", -b.Retention);
                            if (b.TdsPct != 0) Line($"Less: TDS (194C) @ {b.TdsPct:0.#}%", -b.Tds);
                            if (b.CessPct != 0) Line($"Less: Labour cess @ {b.CessPct:0.#}%", -b.Cess);
                            if (b.GstTdsPct != 0) Line($"Less: GST-TDS @ {b.GstTdsPct:0.#}%", -b.GstTds);
                            if (b.OtherDeductions != 0) Line("Less: Other deductions", -b.OtherDeductions);
                            if (b.AdvanceRecovery != 0) Line("Less: Advance recovery", -b.AdvanceRecovery);
                            c.Item().PaddingVertical(2).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            Line("Net amount payable", b.Net, strong: true);
                        });

                        col.Item().PaddingTop(30).Row(r =>
                        {
                            r.RelativeItem().Column(cc =>
                            {
                                cc.Item().Text("Prepared / measured by").FontSize(9).FontColor(Colors.Grey.Darken2);
                                cc.Item().Height(28);
                                cc.Item().Text(measuredBy).FontSize(9);
                            });
                            r.RelativeItem().AlignRight().Column(cc =>
                            {
                                cc.Item().Text("Certified for payment — for " + certParty.CompanyDisplay(info.CompanyDisplay)).SemiBold();
                                cc.Item().Height(28);
                                cc.Item().Text("Authorised signatory").FontSize(9).FontColor(Colors.Grey.Darken2);
                            });
                        });
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ExportStorePurchaseOrder(string path, ProjectStore store, PurchaseOrder po, out string? error)
    {
        error = null;
        try
        {
            var info = store.Info;
            string number = string.IsNullOrWhiteSpace(po.Number)
                ? store.Stores.Preview("PO", po.Date, info.CompanyDisplay) + "  (draft)"
                : po.Number;
            string wh = store.Stores.WarehouseName(po.WarehouseId);

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(32);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Calibri));
                    page.Header().Element(cc => ReportHeader(cc, store, "Purchase order"));
                    page.Footer().Element(cc => ReportFooter(cc, store));
                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(t => { t.Span("PO No: ").SemiBold(); t.Span(number); });
                            r.RelativeItem().AlignRight().Text(t => { t.Span("Date: ").SemiBold(); t.Span(po.Date.ToString("dd MMM yyyy")); });
                        });
                        col.Item().Text(t => { t.Span("To (Supplier): ").SemiBold(); t.Span(string.IsNullOrWhiteSpace(po.SupplierName) ? "—" : po.SupplierName); });
                        col.Item().Text(t => { t.Span("Deliver to: ").SemiBold(); t.Span(wh); });

                        var headers = new[] { "#", "Material", "Unit", "Qty", "Rate", "Amount" };
                        var rows = new List<string[]>();
                        int i = 1;
                        foreach (var l in po.Lines)
                            rows.Add(new[] { (i++).ToString(), l.Material, l.Unit,
                                l.Qty.ToString("0.###"), l.Rate.ToString("0.##"), l.Amount.ToString("0.##") });
                        col.Item().PaddingTop(6).Element(cc => TableSection(cc, "Ordered materials", headers, rows));
                        col.Item().AlignRight().Text(t => { t.Span("Order total: ").SemiBold(); t.Span("Rs. " + po.Total.ToString("N2")); });

                        if (!string.IsNullOrWhiteSpace(po.Notes))
                        {
                            col.Item().PaddingTop(6).Text("Notes / terms:").SemiBold();
                            foreach (var line in SplitLines(po.Notes)) col.Item().Text(line.Length == 0 ? " " : line);
                        }

                        col.Item().PaddingTop(34).AlignRight().Column(cc =>
                        {
                            cc.Item().Text("For " + info.CompanyDisplay).SemiBold();
                            cc.Item().Height(30);
                            cc.Item().Text("Authorised signatory").FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool ExportPayroll(string path, ProjectStore store, string month, out string? error)
    {
        error = null;
        try
        {
            var org = store.Org;
            string monthLabel = DateTime.TryParseExact(month + "-01", "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var md)
                ? md.ToString("MMMM yyyy") : month;

            var headers = new[] { "#", "Code", "Name", "Designation", "Site", "Days", "Gross", "Advance", "Net" };
            var rows = new List<string[]>();
            double tg = 0, ta = 0, tn = 0;
            int i = 1;
            foreach (var emp in org.Employees.Where(e => e.Active))
            {
                var rec = org.GetPayroll(emp.Id, month);
                double gross = org.Gross(emp, rec.DaysPresent);
                double net = gross - rec.Advance;
                tg += gross; ta += rec.Advance; tn += net;
                rows.Add(new[]
                {
                    (i++).ToString(), emp.Code, emp.Name, emp.Designation, org.SiteName(emp.SiteId),
                    rec.DaysPresent.ToString("0.#"), gross.ToString("N2"), rec.Advance.ToString("N2"), net.ToString("N2")
                });
            }
            rows.Add(new[] { "", "", "Total", "", "", "", tg.ToString("N2"), ta.ToString("N2"), tn.ToString("N2") });

            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Calibri));
                    page.Header().Element(cc => ReportHeader(cc, store, $"Payroll register · {monthLabel}"));
                    page.Footer().Element(cc => ReportFooter(cc, store));
                    page.Content().Column(col =>
                    {
                        col.Spacing(10);
                        col.Item().Text($"Working days basis: {org.WorkingDays:0.#} · {org.Employees.Count(e => e.Active)} active employee(s)").FontSize(9);
                        col.Item().Element(cc => TableSection(cc, $"Wages & salaries — {monthLabel}", headers, rows));
                    });
                });
            }).GeneratePdf(path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static IEnumerable<string> SplitLines(string? s) =>
        (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private sealed class ReportSection
    {
        public string Title { get; init; } = "";
        public string? Note { get; init; }
        public List<(string Title, GenTable Table)> Tables { get; } = new();
    }

    private static List<ReportSection> BuildReportSections(ProjectStore store)
    {
        var list = new List<ReportSection>();
        void addSteel(string kind, string title, IEnumerable<Dictionary<string, string>> rows)
        {
            var rowList = rows.ToList();
            if (rowList.Count == 0) return;
            var expanded = MemberSheetHelper.ExpandForGenerate(kind, rowList);
            var res = EngineClient.Generate(MemberSheetHelper.EngineKind(kind), store.SettingsJson(), expanded);
            var sec = new ReportSection
            {
                Title = title,
                Note = res.Ok ? null : (res.Error ?? "Generate failed")
            };
            if (res.Ok)
            {
                if (res.Bbs.Headers.Count > 0)
                    sec.Tables.Add(("Bar bending schedule", res.Bbs));
                if (res.Summary.Headers.Count > 0)
                    sec.Tables.Add(("Steel summary", res.Summary));
                if (res.Checks.Headers.Count > 0 && res.Checks.Rows.Count > 0)
                    sec.Tables.Add(("Checks", res.Checks));
            }
            list.Add(sec);
        }

        void addCivil(string kind, string title, IEnumerable<Dictionary<string, string>> rows)
        {
            var rowList = rows.ToList();
            if (rowList.Count == 0) return;
            var res = CivilBoqCalculator.Generate(kind, rowList);
            var sec = new ReportSection { Title = title };
            if (res.Bbs.Headers.Count > 0)
                sec.Tables.Add(("Quantity take-off", res.Bbs));
            if (res.Summary.Headers.Count > 0)
                sec.Tables.Add(("Summary / materials", res.Summary));
            list.Add(sec);
        }

        addSteel("columns", "Columns", store.Columns);
        addSteel("beams", "Beams", store.Beams);
        addSteel("pedestals", "Pedestals", store.Pedestals);
        addSteel("lintels", "Lintels", store.Lintels);
        addSteel("slabs", "Slabs", store.Slabs);
        addSteel("footings", "Footings", store.Footings);
        addSteel("walls", "Retaining walls", store.Walls);
        addSteel("stairs", "Stairs", store.Stairs);
        addCivil("waterproofing", "Waterproofing", store.Waterproofing);
        addCivil("dpc", "Damp-proof course", store.Dpc);
        addCivil("coping", "Coping", store.Coping);
        addCivil("screed", "Screed", store.Screed);
        addCivil("vdf", "VDF flooring", store.Vdf);
        addCivil("skirting", "Skirting", store.Skirting);
        addCivil("parapet", "Parapet", store.Parapet);
        addCivil("plinth_protection", "Plinth protection", store.PlinthProtection);

        addCivil("masonry", "Masonry walls", store.MasonryWalls);
        addCivil("plaster", "Plastering", store.Plaster);
        addCivil("pcc", "PCC bed", store.PccBeds);
        addCivil("earthwork", "Earthwork", store.Earthwork);
        addCivil("ssm", "Size stone masonry", store.SizeStone);
        addCivil("shuttering", "Shuttering / formwork", store.Shuttering);
        addCivil("flooring", "Flooring", store.Flooring);
        FinishSurfacesCalculator.SyncPaintingFromPlaster(store);
        addCivil("painting", "Painting", store.Painting);
        addCivil("doors", "Doors", store.Doors);
        addCivil("windows", "Windows", store.Windows);

        // Project-level concrete by grade + materials
        var concrete = MaterialsCalculator.BuildConcreteBoq(store);
        var civil = CivilBoqCalculator.BuildAll(store);
        if (concrete.Count > 0 || civil.Count > 0)
        {
            var rmc = store.ConcreteFromRmc;
            var byGrade = MaterialsCalculator.ConcreteByGrade(concrete, rmc);
            var mats = MaterialsCalculator.MaterialPurchaseOrder(concrete, includeConcreteSplit: !rmc)
                .Concat(CivilBoqCalculator.MaterialPurchaseOrder(civil))
                .ToList();

            var overview = new ReportSection { Title = "Project materials overview" };
            if (byGrade.Count > 0)
            {
                overview.Tables.Add((rmc ? "Concrete by grade (RMC m³)" : "Concrete by grade (m³)",
                    new GenTable
                    {
                        Headers = new List<string> { "Category", "Item", "Unit", "Qty", "Notes" },
                        Rows = byGrade.Select(p => new List<string>
                        {
                            p.Category, p.Item, p.Unit, p.Qty.ToString("0.###"), p.Notes
                        }).ToList()
                    }));
            }
            if (mats.Count > 0)
            {
                overview.Tables.Add((rmc ? "Other materials (civil only)" : "Other materials (RCC batch + civil)",
                    new GenTable
                    {
                        Headers = new List<string> { "Category", "Item", "Unit", "Qty", "Notes" },
                        Rows = mats.Select(p => new List<string>
                        {
                            p.Category, p.Item, p.Unit, p.Qty.ToString("0.###"), p.Notes
                        }).ToList()
                    }));
            }
            if (overview.Tables.Count > 0)
                list.Add(overview);
        }

        return list;
    }

    private static void ReportHeader(IContainer container, ProjectStore store, string subtitle)
    {
        var info = store.Info;
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                var logoPath = info.ResolvedLogoPath;
                if (logoPath is not null)
                {
                    row.ConstantItem(52).Height(40).PaddingRight(8)
                        .AlignMiddle().Image(logoPath).FitArea();
                }

                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(info.CompanyDisplay).SemiBold().FontSize(12);
                    c.Item().Text($"{Branding.AppName} · {subtitle}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    if (!string.IsNullOrWhiteSpace(info.ContactPhone) || !string.IsNullOrWhiteSpace(info.ContactEmail))
                    {
                        var bits = new List<string>();
                        if (!string.IsNullOrWhiteSpace(info.ContactPhone)) bits.Add(info.ContactPhone);
                        if (!string.IsNullOrWhiteSpace(info.ContactEmail)) bits.Add(info.ContactEmail);
                        c.Item().Text(string.Join(" · ", bits)).FontSize(7).FontColor(Colors.Grey.Darken1);
                    }
                    if (!string.IsNullOrWhiteSpace(info.Address))
                        c.Item().Text(info.Address).FontSize(7).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(info.RegistrationLine))
                        c.Item().Text(info.RegistrationLine).FontSize(7).FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(200).AlignRight().Column(c => ProjectHeaderColumn(c, info, includePreparedBy: true));
            });
            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    /// <summary>Report header branded with a specific persona (issuing party) instead of the project company.</summary>
    private static void PartyHeader(IContainer container, ProjectStore store, Party party, string subtitle)
    {
        var info = store.Info;
        string company = party.CompanyDisplay(info.CompanyDisplay);
        string address = string.IsNullOrWhiteSpace(party.Address) ? info.Address : party.Address;
        string phone = string.IsNullOrWhiteSpace(party.Phone) ? info.ContactPhone : party.Phone;
        string email = string.IsNullOrWhiteSpace(party.Email) ? info.ContactEmail : party.Email;
        string reg = string.IsNullOrWhiteSpace(party.RegistrationLine) ? info.RegistrationLine : party.RegistrationLine;
        string? logoPath = party.ResolvedLogoPath ?? info.ResolvedLogoPath;
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                if (logoPath is not null)
                    row.ConstantItem(52).Height(40).PaddingRight(8).AlignMiddle().Image(logoPath).FitArea();
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text(company).SemiBold().FontSize(12);
                    c.Item().Text($"{party.Role.Display()} · {subtitle}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    var bits = new List<string>();
                    if (!string.IsNullOrWhiteSpace(phone)) bits.Add(phone);
                    if (!string.IsNullOrWhiteSpace(email)) bits.Add(email);
                    if (bits.Count > 0)
                        c.Item().Text(string.Join(" · ", bits)).FontSize(7).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(address))
                        c.Item().Text(address).FontSize(7).FontColor(Colors.Grey.Darken1);
                    if (!string.IsNullOrWhiteSpace(reg))
                        c.Item().Text(reg).FontSize(7).FontColor(Colors.Grey.Darken1);
                });
                row.ConstantItem(200).AlignRight().Column(c => ProjectHeaderColumn(c, info, includePreparedBy: false));
            });
            col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
        });
    }

    private static void ProjectHeaderColumn(ColumnDescriptor c, ProjectInfo info, bool includePreparedBy)
    {
        c.Item().Text(info.Name).SemiBold().FontSize(10);
        if (!string.IsNullOrWhiteSpace(info.Summary))
            c.Item().Text(info.Summary.Trim()).FontSize(8).FontColor(Colors.Grey.Darken1);
        if (!string.IsNullOrWhiteSpace(info.Location))
            c.Item().Text(info.Location).FontSize(8).FontColor(Colors.Grey.Darken1);
        if (!string.IsNullOrWhiteSpace(info.ClientName))
            c.Item().Text($"Client: {info.ClientName}").FontSize(8).FontColor(Colors.Grey.Darken1);
        if (includePreparedBy)
            c.Item().Text(info.PreparedByLine).FontSize(8).FontColor(Colors.Grey.Darken1);
        c.Item().Text(DateTime.Now.ToString("dd MMM yyyy HH:mm")).FontSize(7).FontColor(Colors.Grey.Darken1);
    }

    private static void ReportFooter(IContainer container, ProjectStore store)
    {
        var company = store.Info.CompanyDisplay;
        container.AlignCenter().Text(t =>
        {
            t.Span($"{Branding.AppName} · {company}  ·  ").FontSize(8).FontColor(Colors.Grey.Darken1);
            t.Span("Page ").FontSize(8).FontColor(Colors.Grey.Darken1);
            t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
            t.Span(" / ").FontSize(8).FontColor(Colors.Grey.Darken1);
            t.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void SectionBlock(IContainer container, ReportSection sec)
    {
        container.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text(sec.Title).SemiBold().FontSize(12);
            if (!string.IsNullOrWhiteSpace(sec.Note))
                col.Item().Text(sec.Note).FontColor(Colors.Red.Medium).FontSize(9);

            foreach (var (title, table) in sec.Tables)
            {
                col.Item().Text(title).SemiBold().FontSize(10).FontColor(Colors.Grey.Darken2);
                col.Item().Element(c => DataTable(c, table.Headers, table.Rows));
            }
        });
    }

    private static void TableSection(IContainer container, string title, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        container.Column(col =>
        {
            col.Spacing(4);
            col.Item().Text(title).SemiBold().FontSize(11);
            col.Item().Element(c => DataTable(c, headers, rows.Select(r => (IReadOnlyList<string>)r).ToList()));
        });
    }

    private static void DataTable(IContainer container, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (headers.Count == 0)
        {
            container.Text("— no data —").FontColor(Colors.Grey.Medium);
            return;
        }

        container.Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                for (var i = 0; i < headers.Count; i++)
                    cols.RelativeColumn();
            });

            table.Header(header =>
            {
                foreach (var h in headers)
                    header.Cell().Element(CellHeader).Text(h).SemiBold().FontSize(8);
            });

            if (rows.Count == 0)
            {
                table.Cell().ColumnSpan((uint)headers.Count).Element(CellBody)
                    .Text("No lines").FontColor(Colors.Grey.Medium);
                return;
            }

            var alt = false;
            foreach (var row in rows)
            {
                for (var i = 0; i < headers.Count; i++)
                {
                    var cell = i < row.Count ? row[i] : "";
                    var bg = alt ? Colors.Grey.Lighten4 : Colors.White;
                    table.Cell().Element(c => CellBody(c, bg)).Text(cell ?? "").FontSize(8);
                }
                alt = !alt;
            }
        });
    }

    private static IContainer CellHeader(IContainer c) =>
        c.BorderBottom(1).BorderColor(Colors.Grey.Darken1)
            .Background(Colors.Grey.Lighten3)
            .PaddingVertical(4).PaddingHorizontal(3);

    private static IContainer CellBody(IContainer c) => CellBody(c, Colors.White);

    private static IContainer CellBody(IContainer c, string background) =>
        c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
            .Background(background)
            .PaddingVertical(3).PaddingHorizontal(3);
}
