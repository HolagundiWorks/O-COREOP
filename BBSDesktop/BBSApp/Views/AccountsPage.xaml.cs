// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using BBSApp.Services;
using BBSApp.Services.Company;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Office.Shared;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

public sealed partial class AccountsPage : Page
{
    public enum AccountsTab { Bills, Cash, Ledger }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly AccountsBook _book = ProjectStore.Current.Accounts;
    private readonly ObservableCollection<BillRow> _billRows = new();
    private readonly ObservableCollection<BillLineVm> _lineRows = new();
    private readonly ObservableCollection<TxnRowVm> _txnRows = new();
    private readonly ObservableCollection<LedgerRowVm> _ledgerRows = new();
    private readonly List<string> _contractIds = new();
    private readonly AccountsTab _initialTab;
    private RunningBill? _current;
    private bool _loading;

    public AccountsPage(AccountsTab tab = AccountsTab.Bills)
    {
        _initialTab = tab;
        InitializeComponent();
        _loading = true;
        PrefixBox.Text = _book.Prefix;
        OpeningCashBox.Value = _book.OpeningCash;
        OpeningBankBox.Value = _book.OpeningBank;

        BuildContractBox();
        foreach (var b in _book.Bills) _billRows.Add(new BillRow(_book, b, Company));
        BillList.ItemsSource = _billRows;
        LinesGrid.ItemsSource = _lineRows;

        foreach (var t in _book.Transactions) _txnRows.Add(new TxnRowVm(t, RefreshCash));
        TxnGrid.ItemsSource = _txnRows;
        LedgerGrid.ItemsSource = _ledgerRows;
        _loading = false;

        Loaded += (_, _) =>
        {
            MainPivot.SelectedIndex = _initialTab switch { AccountsTab.Cash => 1, AccountsTab.Ledger => 2, _ => 0 };
            if (_billRows.Count > 0) BillList.SelectedIndex = 0; else LoadBill(null);
            RefreshCash();
            RefreshLedgerParties();
            UpdateSummary();
        };
    }

    private string Company => ProjectStore.Current.Info.CompanyDisplay;
    private BillRow? RowFor(RunningBill b) => _billRows.FirstOrDefault(r => r.B == b);

    private void BuildContractBox()
    {
        _contractIds.Clear();
        var items = new List<string> { "(none)" };
        _contractIds.Add("");
        foreach (var c in ProjectStore.Current.ContractBook.Contracts)
        {
            string num = c.Finalized && !string.IsNullOrWhiteSpace(c.Number) ? c.Number : "(draft)";
            items.Add($"{num} · {(string.IsNullOrWhiteSpace(c.Title) ? Contract.KindDisplay(c.Kind) : c.Title)}");
            _contractIds.Add(c.Id);
        }
        ContractBox.ItemsSource = items;
    }

    private void UpdateSummary()
    {
        int cert = _book.Bills.Count(b => b.Certified);
        SummaryText.Text = $"{_book.Bills.Count} RA bill(s) · {cert} certified · {_book.Transactions.Count} cash entries · "
            + $"cash {_book.CashBalance:N0} · bank {_book.BankBalance:N0}.";
    }

    private void Pivot_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (MainPivot.SelectedIndex == 1) RefreshCash();
        else if (MainPivot.SelectedIndex == 2) RefreshLedgerParties();
    }

    // ---------------- Running bills ----------------
    private void BillList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveBill();
        _current = (BillList.SelectedItem as BillRow)?.B;
        LoadBill(_current);
    }

    private void LoadBill(RunningBill? b)
    {
        _loading = true;
        _current = b;
        EditorPanel.Opacity = b is null ? 0.5 : 1;
        _lineRows.Clear();
        if (b is null)
        {
            BillDateBox.Date = DateTimeOffset.Now;
            ContractBox.SelectedIndex = 0;
            PartyBox.Text = "";
            FillBillPhases("");
            RetentionBox.Value = 5; OtherDeductBox.Value = 0; AdvanceBox.Value = 0;
            GstBox.Value = 0; TdsBox.Value = 0; CessBox.Value = 0; GstTdsBox.Value = 0;
            NumberText.Text = "—";
            SetLocked(true);
            RefreshTotals();
            _loading = false;
            return;
        }
        BillDateBox.Date = new DateTimeOffset(b.Date);
        int ci = Math.Max(0, _contractIds.IndexOf(b.ContractId));
        ContractBox.SelectedIndex = ci;
        PartyBox.Text = b.Party;
        FillBillPhases(b.BillingPhase);
        RetentionBox.Value = b.RetentionPct;
        OtherDeductBox.Value = b.OtherDeductions;
        AdvanceBox.Value = b.AdvanceRecovery;
        GstBox.Value = b.GstPct;
        TdsBox.Value = b.TdsPct;
        CessBox.Value = b.CessPct;
        GstTdsBox.Value = b.GstTdsPct;
        foreach (var l in b.Lines) _lineRows.Add(new BillLineVm(l, RefreshTotals));
        UpdateNumberText();
        SetLocked(b.Certified);
        RefreshTotals();
        _loading = false;
    }

    private void SaveBill()
    {
        if (_current is null || _current.Certified) return;
        if (BillDateBox.Date is { } d) _current.Date = d.DateTime.Date;
        int ci = ContractBox.SelectedIndex;
        _current.ContractId = ci >= 0 && ci < _contractIds.Count ? _contractIds[ci] : "";
        _current.ContractLabel = ci > 0 && ContractBox.SelectedItem is string s ? s : "";
        _current.Party = PartyBox.Text ?? "";
        _current.BillingPhase = (BillPhaseBox.Text ?? "").Trim();
        _current.RetentionPct = double.IsNaN(RetentionBox.Value) ? 0 : RetentionBox.Value;
        _current.OtherDeductions = double.IsNaN(OtherDeductBox.Value) ? 0 : OtherDeductBox.Value;
        _current.AdvanceRecovery = double.IsNaN(AdvanceBox.Value) ? 0 : AdvanceBox.Value;
        _current.GstPct = double.IsNaN(GstBox.Value) ? 0 : GstBox.Value;
        _current.TdsPct = double.IsNaN(TdsBox.Value) ? 0 : TdsBox.Value;
        _current.CessPct = double.IsNaN(CessBox.Value) ? 0 : CessBox.Value;
        _current.GstTdsPct = double.IsNaN(GstTdsBox.Value) ? 0 : GstTdsBox.Value;
        RowFor(_current)?.Refresh();
    }

    private void SetLocked(bool locked)
    {
        bool en = !locked;
        foreach (var c in new Control[] { BillDateBox, ContractBox, PartyBox, BillPhaseBox, RetentionBox, OtherDeductBox, AdvanceBox,
                                          GstBox, TdsBox, CessBox, GstTdsBox })
            c.IsEnabled = en;
        LinesGrid.IsReadOnly = locked;
        LockBar.IsOpen = locked;
    }

    /// <summary>The running-bill phases to pick from (the defaults and any chosen in O-Draft), with this bill's own value kept.</summary>
    private void FillBillPhases(string current)
    {
        var names = ProjectStore.Current.PhaseNames(billing: true);
        if (!string.IsNullOrWhiteSpace(current) && !names.Contains(current, StringComparer.OrdinalIgnoreCase)) names.Add(current);
        BillPhaseBox.ItemsSource = names;
        BillPhaseBox.Text = current ?? "";
    }

    private void UpdateNumberText()
    {
        if (_current is null) { NumberText.Text = "—"; return; }
        NumberText.Text = _current.Certified && !string.IsNullOrWhiteSpace(_current.Number)
            ? $"{RevisionLabels.RunningBill(_current.Number, _current.Revision)} · RA {_current.BillNo}"
            : _book.PreviewBillNumber(_current, Company) + " (draft)";
    }

    private double Box(NumberBox b) => double.IsNaN(b.Value) ? 0 : b.Value;

    private void RefreshTotals()
    {
        double gross = _lineRows.Sum(v => v.L.Amount);
        // Reuse the model's arithmetic so the summary matches the certified bill / PDF exactly.
        var calc = new RunningBill
        {
            RetentionPct = Box(RetentionBox), OtherDeductions = Box(OtherDeductBox), AdvanceRecovery = Box(AdvanceBox),
            GstPct = Box(GstBox), TdsPct = Box(TdsBox), CessPct = Box(CessBox), GstTdsPct = Box(GstTdsBox)
        };
        foreach (var v in _lineRows) calc.Lines.Add(v.L);

        GrossText.Text = calc.GstPct > 0
            ? $"Gross: {gross.ToString("N2", Inv)}   +GST {calc.Gst.ToString("N2", Inv)}   = Invoice {calc.Invoice.ToString("N2", Inv)}"
            : "Gross: " + gross.ToString("N2", Inv);
        DeductText.Text = "Deductions: retention " + calc.Retention.ToString("N2", Inv)
            + (calc.TdsPct > 0 ? $" + TDS {calc.Tds:N2}" : "")
            + (calc.CessPct > 0 ? $" + cess {calc.Cess:N2}" : "")
            + (calc.GstTdsPct > 0 ? $" + GST-TDS {calc.GstTds:N2}" : "")
            + (calc.OtherDeductions != 0 ? $" + other {calc.OtherDeductions:N2}" : "")
            + (calc.AdvanceRecovery != 0 ? $" + advance {calc.AdvanceRecovery:N2}" : "");
        NetText.Text = "Net payable: " + calc.Net.ToString("N2", Inv);
        RowFor(_current!)?.Refresh();
    }

    private void Deduction_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        RefreshTotals();
    }

    private void ContractBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _current is null) return;
        int ci = ContractBox.SelectedIndex;
        if (ci > 0 && ci < _contractIds.Count)
        {
            var c = ProjectStore.Current.ContractBook.Contracts.FirstOrDefault(x => x.Id == _contractIds[ci]);
            if (c is not null)
            {
                if (string.IsNullOrWhiteSpace(PartyBox.Text)) PartyBox.Text = c.ContractorName;
                if (RetentionBox.Value == 0) RetentionBox.Value = c.RetentionPct;
            }
        }
        UpdateNumberText();
    }

    private void NewBill_Click(object sender, RoutedEventArgs e)
    {
        SaveBill();
        var b = new RunningBill
        {
            IssuedByRole = ProjectStore.Current.Parties.Active,
            // Typical Indian works-contract defaults; editable per bill.
            GstPct = 18, TdsPct = 1, CessPct = 1, GstTdsPct = 0
        };
        _book.Bills.Add(b);
        var row = new BillRow(_book, b, Company);
        _billRows.Add(row);
        ProjectStore.Current.Notify();
        BillList.SelectedItem = row;
        UpdateSummary();
    }

    private void SaveBill_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("Nothing to save", "Create an RA bill first."); return; }
        SaveBill();
        UpdateNumberText();
        ProjectStore.Current.Notify();
        RefreshLedgerParties();
        AppNotify.Success("Saved", "RA bill");
        var companyErr = CompanyService.TryPushProjectSummary(ProjectStore.Current);
        if (companyErr is not null)
            AppNotify.Warning("Company file", companyErr);
    }

    private void ShareDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("Nothing to share", "Select an RA bill first."); return; }
        SaveBill();
        var err = CompanyService.TryShareRaBillDraft(ProjectStore.Current, _current);
        if (err is not null)
            AppNotify.Warning("Share draft", err);
        else
            AppNotify.Success("Share draft", "Running bill draft sent to O-Studio (Practice → Approvals).");
    }

    private async void CertifyBill_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("Nothing to certify", "Create an RA bill first."); return; }
        SaveBill();
        if (_current.Certified) { AppNotify.Info("Already certified", _current.Number); return; }
        var dlg = new ContentDialog
        {
            Title = "Certify RA bill locally",
            Content = $"Assign local number {_book.PreviewBillNumber(_current, Company)} and lock this bill in O-CoreOp only? "
                      + $"Suite approval is in O-Studio (Practice → Approvals). Net payable ₹ {_current.Net:N2}.",
            PrimaryButtonText = "Certify locally",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        _book.CertifyBill(_current, Company);
        RowFor(_current)?.Refresh();
        LoadBill(_current);
        ProjectStore.Current.Notify();
        UpdateSummary();
        AppNotify.Success("Certified locally", _current.Number);
    }

    private async void ReviseBill_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("Nothing to revise", "Select a certified RA bill."); return; }
        if (!_current.Certified) { AppNotify.Info("Not certified", "Only a certified bill has a revision to make; edit this one directly."); return; }
        var dlg = new ContentDialog
        {
            Title = "Revise RA bill",
            Content = $"Open {_current.Number} as {RevisionLabels.Revision(RevisionLabels.Next(_current.Revision))}? It keeps its number and is unlocked until you certify it again.",
            PrimaryButtonText = "Revise",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;
        if (!_book.ReviseBill(_current)) return;
        RowFor(_current)?.Refresh();
        LoadBill(_current);
        ProjectStore.Current.Notify();
        UpdateSummary();
        AppNotify.Success("Revision opened", RevisionLabels.RunningBill(_current.Number, _current.Revision));
    }

    private void DeleteBill_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var b = _current;
        int idx = _billRows.IndexOf(RowFor(b)!);
        _book.Bills.Remove(b);
        var row = RowFor(b);
        if (row is not null) _billRows.Remove(row);
        ProjectStore.Current.Notify();
        UpdateSummary();
        if (_billRows.Count > 0) BillList.SelectedIndex = Math.Clamp(idx, 0, _billRows.Count - 1);
        else LoadBill(null);
    }

    private void Prefix_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _book.Prefix = (PrefixBox.Text ?? "").Trim();
        ProjectStore.Current.Notify();
        foreach (var r in _billRows) r.Refresh();
        UpdateNumberText();
    }

    private async void ExportBill_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("Nothing to export", "Create an RA bill first."); return; }
        SaveBill();
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = string.IsNullOrWhiteSpace(_current.Number)
            ? $"RA_{_current.Date:yyyyMMdd}" : _current.Number.Replace('/', '-');
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        if (PdfExport.ExportRunningBill(file.Path, ProjectStore.Current, _current, out var err))
            AppNotify.Success("RA bill exported", file.Name);
        else
            AppNotify.Error("Export failed", err ?? "Could not write PDF.");
    }

    private void LinesGrid_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e)
    {
        RefreshTotals();
        ProjectStore.Current.Notify();
    }

    private void AddLine_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Certified) return;
        var l = new BillLine { Description = "New item", Unit = "no", Rate = 0, Qty = 1 };
        _current.Lines.Add(l);
        _lineRows.Add(new BillLineVm(l, RefreshTotals));
        RefreshTotals();
        ProjectStore.Current.Notify();
    }

    private void ImportFromContract_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Certified) return;
        int ci = ContractBox.SelectedIndex;
        if (ci <= 0 || ci >= _contractIds.Count)
        {
            AppNotify.Info("No contract selected", "Choose a contract above, then import its priced items.");
            return;
        }
        var c = ProjectStore.Current.ContractBook.Contracts.FirstOrDefault(x => x.Id == _contractIds[ci]);
        if (c is null || c.Lines.Count == 0) { AppNotify.Info("No lines", "That contract has no priced items."); return; }
        foreach (var cl in c.Lines)
        {
            var l = new BillLine { Description = cl.Description, Unit = cl.Unit, Rate = cl.Rate, Qty = cl.Qty };
            _current.Lines.Add(l);
            _lineRows.Add(new BillLineVm(l, RefreshTotals));
        }
        RefreshTotals();
        ProjectStore.Current.Notify();
        AppNotify.Success("Imported", $"{c.Lines.Count} item(s) from {(string.IsNullOrWhiteSpace(c.Number) ? "contract" : c.Number)}");
    }

    private void DeleteLine_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _current.Certified) return;
        if (LinesGrid.SelectedItem is not BillLineVm vm) return;
        _current.Lines.Remove(vm.L);
        _lineRows.Remove(vm);
        RefreshTotals();
        ProjectStore.Current.Notify();
    }

    // ---------------- Cash book ----------------
    private void Opening_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        _book.OpeningCash = double.IsNaN(OpeningCashBox.Value) ? 0 : OpeningCashBox.Value;
        _book.OpeningBank = double.IsNaN(OpeningBankBox.Value) ? 0 : OpeningBankBox.Value;
        ProjectStore.Current.Notify();
        RefreshCash();
    }

    private void RefreshCash()
    {
        CashSummary.Text = $"Receipts {_book.TotalReceipts:N2} · Payments {_book.TotalPayments:N2}  |  "
            + $"Cash balance {_book.CashBalance:N2} · Bank balance {_book.BankBalance:N2}";
        UpdateSummary();
    }

    private void TxnGrid_CellEditEnded(object sender, DataGridCellEditEndedEventArgs e)
    {
        RefreshCash();
        ProjectStore.Current.Notify();
    }

    private void AddTxn(CashKind kind)
    {
        var t = new CashTxn
        {
            Kind = kind, Account = CashAccount.Bank, Date = DateTime.Today,
            IssuedByRole = ProjectStore.Current.Parties.Active
        };
        _book.Transactions.Add(t);
        _txnRows.Add(new TxnRowVm(t, RefreshCash));
        RefreshCash();
        ProjectStore.Current.Notify();
    }

    private void AddReceipt_Click(object sender, RoutedEventArgs e) => AddTxn(CashKind.Receipt);
    private void AddPayment_Click(object sender, RoutedEventArgs e) => AddTxn(CashKind.Payment);

    private void DeleteTxn_Click(object sender, RoutedEventArgs e)
    {
        if (TxnGrid.SelectedItem is not TxnRowVm vm) return;
        _book.Transactions.Remove(vm.T);
        _txnRows.Remove(vm);
        RefreshCash();
        ProjectStore.Current.Notify();
    }

    // ---------------- Ledger ----------------
    private void RefreshLedgerParties()
    {
        var sel = LedgerPartyBox.SelectedItem as string;
        var set = new SortedSet<string>(_book.Parties(), StringComparer.OrdinalIgnoreCase);
        foreach (var c in ProjectStore.Current.ContractBook.Contracts)
            if (!string.IsNullOrWhiteSpace(c.ContractorName)) set.Add(c.ContractorName.Trim());
        var parties = set.ToList();
        LedgerPartyBox.ItemsSource = parties;
        if (sel is not null && parties.Contains(sel)) LedgerPartyBox.SelectedItem = sel;
        else if (parties.Count > 0) LedgerPartyBox.SelectedIndex = 0;
        BuildLedger();
    }

    private void LedgerParty_Changed(object sender, SelectionChangedEventArgs e) => BuildLedger();
    private void LedgerRefresh_Click(object sender, RoutedEventArgs e) => RefreshLedgerParties();

    private void BuildLedger()
    {
        _ledgerRows.Clear();
        if (LedgerPartyBox.SelectedItem is not string party || string.IsNullOrWhiteSpace(party))
        {
            LedgerSummary.Text = "Select a party to see its statement.";
            return;
        }

        bool Match(string? s) => (s ?? "").Trim().Equals(party, StringComparison.OrdinalIgnoreCase);

        // Contract / work-order value awarded to this contractor.
        double orderValue = ProjectStore.Current.ContractBook.Contracts
            .Where(c => Match(c.ContractorName)).Sum(c => c.Value);

        // Dated events: certified bills raise what we owe (due); payments settle it.
        var events = new List<(DateTime date, string particular, double due, double paid)>();
        double certifiedNet = 0, paid = 0, retentionHeld = 0;

        foreach (var b in _book.Bills.Where(x => x.Certified && Match(x.Party)).OrderBy(x => x.Date))
        {
            string num = string.IsNullOrWhiteSpace(b.Number) ? "" : $" · {b.Number}";
            events.Add((b.Date, $"RA {b.BillNo} certified{num}", b.Net, 0));
            certifiedNet += b.Net;
            retentionHeld += b.Retention;
        }
        foreach (var t in _book.Transactions.Where(x => Match(x.Party)).OrderBy(x => x.Date))
        {
            string desc = string.IsNullOrWhiteSpace(t.Description)
                ? (string.IsNullOrWhiteSpace(t.Category) ? "Cash entry" : t.Category) : t.Description;
            if (t.Kind == CashKind.Payment)
            {
                events.Add((t.Date, "Paid: " + desc, 0, t.Amount));
                paid += t.Amount;
            }
            else
            {
                // Receipt from the contractor (e.g. refund / recovery) reduces what we owe.
                events.Add((t.Date, "Received: " + desc, -t.Amount, 0));
                certifiedNet -= t.Amount;
            }
        }

        double bal = 0;
        foreach (var ev in events.OrderBy(x => x.date))
        {
            bal += ev.due - ev.paid;
            _ledgerRows.Add(new LedgerRowVm(ev.date, ev.particular, ev.due, ev.paid, bal));
        }

        double outstanding = certifiedNet - paid;
        LedgerSummary.Text =
            $"{party}:  order value {orderValue:N0} · certified (net) {certifiedNet:N0} · paid {paid:N0} · "
            + $"retention held {retentionHeld:N0} · balance payable {outstanding:N0}";
    }

    private void IssueCertificate_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) { AppNotify.Info("No bill", "Select an RA bill first."); return; }
        SaveBill();
        var b = _current;
        if (!b.Certified)
        {
            AppNotify.Warning("Certify first", "Certify & number the RA bill before issuing a payment certificate.");
            return;
        }
        string billRef = string.IsNullOrWhiteSpace(b.Number) ? $"RA {b.BillNo}" : b.Number;
        var body = new System.Text.StringBuilder();
        body.AppendLine("This is to certify that the work covered by the above running-account bill has been measured");
        body.AppendLine("and checked, and the following amount is certified as due and payable to the contractor:");
        body.AppendLine();
        body.AppendLine($"Gross value of work done      : Rs. {b.Gross:N2}");
        if (b.GstPct != 0) body.AppendLine($"Add: GST @ {b.GstPct:0.#}%            : Rs. {b.Gst:N2}");
        body.AppendLine($"Less: Retention @ {b.RetentionPct:0.#}%     : Rs. {b.Retention:N2}");
        if (b.TdsPct != 0) body.AppendLine($"Less: TDS 194C @ {b.TdsPct:0.#}%       : Rs. {b.Tds:N2}");
        if (b.CessPct != 0) body.AppendLine($"Less: Labour cess @ {b.CessPct:0.#}%    : Rs. {b.Cess:N2}");
        if (b.GstTdsPct != 0) body.AppendLine($"Less: GST-TDS @ {b.GstTdsPct:0.#}%      : Rs. {b.GstTds:N2}");
        if (b.OtherDeductions != 0) body.AppendLine($"Less: Other deductions        : Rs. {b.OtherDeductions:N2}");
        if (b.AdvanceRecovery != 0) body.AppendLine($"Less: Advance recovery        : Rs. {b.AdvanceRecovery:N2}");
        body.AppendLine();
        body.AppendLine($"Net amount certified for payment : Rs. {b.Net:N2}");
        body.AppendLine();
        body.AppendLine("Payment may be released to the contractor accordingly.");

        var pm = ProjectStore.Current.Parties.Pm;
        var doc = new OfficeDocument
        {
            TypeCode = "IPC",
            IssuedByRole = PartyRole.PM,   // certification is a PM function
            IssueDate = DateTime.Today,
            ToName = b.Party,
            Subject = $"Interim Payment Certificate against RA Bill {(b.BillNo > 0 ? "No. " + b.BillNo + " " : "")}({billRef})",
            Body = body.ToString(),
            SignatoryName = pm.SignatoryName,
            SignatoryRole = string.IsNullOrWhiteSpace(pm.SignatoryRole) ? "Project Manager" : pm.SignatoryRole
        };
        ProjectStore.Current.Office.Documents.Add(doc);
        ProjectStore.Current.Notify();
        AppNotify.Success("Payment certificate created",
            $"IPC for {billRef} added under Office → Correspondence (draft). Open it there to review, then Finalize to assign a PM number.");
    }
}

// ---------------- view-models ----------------
public sealed class BillRow : INotifyPropertyChanged
{
    private readonly AccountsBook _book; private readonly string _company;
    public RunningBill B { get; }
    public BillRow(AccountsBook book, RunningBill b, string company) { _book = book; B = b; _company = company; }
    public string Header => !string.IsNullOrWhiteSpace(B.Number)
        ? $"RA {B.BillNo} · {RevisionLabels.RunningBill(B.Number, B.Revision)}{(B.Certified ? "" : " (revising)")}"
        : "RA bill · (draft)";
    public string Sub => $"{B.Date:dd MMM yyyy} · net {B.Net:N0}";
    public void Refresh() { OnP(nameof(Header)); OnP(nameof(Sub)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class BillLineVm : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly Action _changed;
    public BillLine L { get; }
    public BillLineVm(BillLine l, Action changed) { L = l; _changed = changed; }
    public string Description { get => L.Description; set { L.Description = value ?? ""; OnP(); } }
    public string Unit { get => L.Unit; set { L.Unit = value ?? ""; OnP(); } }
    public string RateText { get => L.Rate.ToString("0.##", Inv); set { if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) L.Rate = d; OnP(nameof(RateText)); OnP(nameof(AmountText)); _changed(); } }
    public string QtyText { get => L.Qty.ToString("0.###", Inv); set { if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) L.Qty = d; OnP(nameof(QtyText)); OnP(nameof(AmountText)); _changed(); } }
    public string AmountText => L.Amount.ToString("0.00", Inv);
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class TxnRowVm : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly Action _changed;
    public CashTxn T { get; }
    public TxnRowVm(CashTxn t, Action changed) { T = t; _changed = changed; }
    public string DateText
    {
        get => T.Date.ToString("dd-MM-yyyy", Inv);
        set { if (DateTime.TryParse(value, Inv, DateTimeStyles.None, out var d)) T.Date = d; OnP(); }
    }
    public string KindText
    {
        get => T.Kind == CashKind.Receipt ? "Receipt" : "Payment";
        set { T.Kind = (value ?? "").TrimStart().StartsWith("R", StringComparison.OrdinalIgnoreCase) ? CashKind.Receipt : CashKind.Payment; OnP(); _changed(); }
    }
    public string AccountText
    {
        get => T.Account == CashAccount.Cash ? "Cash" : "Bank";
        set { T.Account = (value ?? "").TrimStart().StartsWith("C", StringComparison.OrdinalIgnoreCase) ? CashAccount.Cash : CashAccount.Bank; OnP(); _changed(); }
    }
    public string Party { get => T.Party; set { T.Party = value ?? ""; OnP(); } }
    public string Category { get => T.Category; set { T.Category = value ?? ""; OnP(); } }
    public string Description { get => T.Description; set { T.Description = value ?? ""; OnP(); } }
    public string AmountText { get => T.Amount.ToString("0.##", Inv); set { if (double.TryParse(value, NumberStyles.Float, Inv, out var d)) T.Amount = d; OnP(); _changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnP([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class LedgerRowVm
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public LedgerRowVm(DateTime date, string particulars, double receipt, double payment, double balance)
    { DateText = date.ToString("dd-MM-yyyy", Inv); Particulars = particulars; ReceiptText = receipt == 0 ? "" : receipt.ToString("N2", Inv); PaymentText = payment == 0 ? "" : payment.ToString("N2", Inv); BalanceText = balance.ToString("N2", Inv); }
    public string DateText { get; }
    public string Particulars { get; }
    public string ReceiptText { get; }
    public string PaymentText { get; }
    public string BalanceText { get; }
}
