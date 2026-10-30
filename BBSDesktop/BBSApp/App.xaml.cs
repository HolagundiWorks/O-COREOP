// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using Microsoft.UI.Xaml;

namespace BBSApp;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; set; }

    /// <summary>App-wide navigation command surface (implemented by the main window).</summary>
    public static Services.Ai.IAppCommandBus? CommandBus => MainWindow;

    public App()
    {
        InitializeComponent();
        RequestedTheme = Hcw.Ui.SuiteAppearance.IsDark ? ApplicationTheme.Dark : ApplicationTheme.Light;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services.RateBookStore.Current.EnsureLoaded();
        MainWindow = new MainWindow();
        MainWindow.Activate();
        // A project file given on the command line (double-click on a .osp, "Open with O-CoreOp") is opened once the window is up.
        var file = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(a => Services.ProjectFiles.IsOdp(a) && System.IO.File.Exists(a));
        if (file is not null)
            MainWindow.DispatcherQueue.TryEnqueue(() => MainWindow.OpenProjectFile(System.IO.Path.GetFullPath(file)));
    }
}
