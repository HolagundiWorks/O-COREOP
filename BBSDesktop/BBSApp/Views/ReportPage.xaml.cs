// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using BBSApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

public sealed partial class ReportPage : Page
{
    public ReportPage()
    {
        InitializeComponent();
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = Sanitize(ProjectStore.Current.Name) + "_boq_report";
        picker.FileTypeChoices.Add("PDF", new List<string> { ".pdf" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        var annotated = await TakeoffAnnotatedPdf.TryCaptureAsync(ProjectStore.Current);
        var stored = await StoredAnnotatedPdf.RenderAllAsync(ProjectStore.Current);
        if (PdfExport.ExportProjectReport(file.Path, ProjectStore.Current, out var err, annotated, stored))
        {
            Info.Title = "PDF";
            Info.Message = annotated is null && stored.Count == 0
                ? "Report saved."
                : "Report saved (includes annotated drawing).";
            Info.Severity = InfoBarSeverity.Success;
        }
        else
        {
            Info.Title = "PDF";
            Info.Message = err ?? "Export failed";
            Info.Severity = InfoBarSeverity.Error;
        }
        Info.IsOpen = true;
    }

    private async void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.SuggestedFileName = Sanitize(ProjectStore.Current.Name);
        picker.FileTypeChoices.Add("HTML", new List<string> { ".html" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        if (EngineClient.ExportHtml(file.Path, ProjectStore.Current.ToJson(), out var err))
        {
            Info.Title = "HTML";
            Info.Message = "Report saved.";
            Info.Severity = InfoBarSeverity.Success;
        }
        else
        {
            Info.Title = "HTML";
            Info.Message = err ?? "Export failed";
            Info.Severity = InfoBarSeverity.Error;
        }
        Info.IsOpen = true;
    }

    private static string Sanitize(string name) =>
        string.IsNullOrWhiteSpace(name) ? "project" : name.Replace(' ', '_');
}
