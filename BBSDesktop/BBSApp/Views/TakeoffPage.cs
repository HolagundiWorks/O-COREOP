// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Aqc.CadImport;
using BBSApp.Services;
using BBSApp.Services.Shilpi;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp.Views;

/// <summary>
/// Take-off. Measuring a drawing or a PDF is done in O-Draft, which records the take-off in ShilpiDB; this page reads it from there
/// and hands it to the same import preview as File > Import from CAD (nothing is written before the user accepts).
/// </summary>
public sealed class TakeoffPage : Page
{
    readonly TextBlock _source = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    readonly TextBox _gateway = new() { Header = "Gateway address", Text = ShilpiTakeoffService.DefaultGateway, MinWidth = 320 };
    readonly Button _read = new() { Content = "Read take-off" };
    readonly Button _readGateway = new() { Content = "Read from gateway" };
    bool _busy;

    public TakeoffPage()
    {
        var title = new TextBlock { Text = "Take-off", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);

        var choose = new Button { Content = "Choose take-off database…" };
        choose.Click += async (_, _) => await ChooseAsync();
        _read.Click += async (_, _) => await ReadVdbAsync();
        _readGateway.Click += async (_, _) => await ReadGatewayAsync();

        Content = new ScrollViewer
        {
            Padding = new Thickness(24),
            Content = new StackPanel
            {
                Spacing = 14, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left,
                Children =
                {
                    title,
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = "Drawings and PDFs are measured in O-Draft, which records the take-off in ShilpiDB. O-CoreOp reads it from there: "
                             + "pick the take-off database for this project, read it, review what would change, then import. Importing again updates "
                             + "the same rows and never duplicates them. Plaster, painting, skirting and shuttering are derived here, not imported.",
                    },
                    new TextBlock { Text = "Take-off database for this project", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    _source,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { choose, _read } },
                    new TextBlock { Text = "Or a gateway that is already running", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Children = { _gateway, _readGateway } },
                    _status,
                },
            },
        };
        Loaded += (_, _) => Refresh();
    }

    void Refresh()
    {
        var path = ProjectStore.Current.Info.TakeoffVdbPath;
        _source.Text = string.IsNullOrWhiteSpace(path) ? "None chosen yet." : path;
        _read.IsEnabled = !_busy && !string.IsNullOrWhiteSpace(path);
        _readGateway.IsEnabled = !_busy;
    }

    async Task ChooseAsync()
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow!));
        picker.FileTypeFilter.Add(".vdb");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        ProjectStore.Current.Info.TakeoffVdbPath = file.Path;
        ProjectStore.Current.Notify();
        Refresh();
        await ReadVdbAsync();
    }

    Task ReadVdbAsync() => RunAsync(p => ShilpiTakeoffService.ReadVdbAsync(ProjectStore.Current.Info.TakeoffVdbPath, p));
    Task ReadGatewayAsync() => RunAsync(p => ShilpiTakeoffService.ReadGatewayAsync(_gateway.Text.Trim(), p));

    async Task RunAsync(Func<IProgress<string>, Task<ShilpiTakeoff.Result>> read)
    {
        if (_busy) return;
        _busy = true; Refresh();
        var progress = new Progress<string>(s => _status.Text = s);
        try
        {
            _status.Text = "Reading…";
            var result = await read(progress);
            var prep = CadImportService.Prepare(result.File);
            _status.Text = $"{result.Records} take-off records read from drawing {result.File.Drawing}"
                           + (result.Skipped.Count > 0 ? $" ({result.Skipped.Count} could not be used: {string.Join("; ", result.Skipped.Take(3))})" : "")
                           + ". " + prep.Plan.Summary + ".";
            await App.MainWindow!.ShowCadImportPreviewAsync(prep, "Import take-off from ShilpiDB");
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
            AppNotify.Error("Take-off", ex.Message);
        }
        finally { _busy = false; Refresh(); }
    }
}
