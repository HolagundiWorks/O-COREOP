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
    }
}
