// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using System.Linq;
using BBSApp.Services;
using BBSApp.Services.Company;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Office.Shared;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BBSApp;

/// <summary>The company file shared with O-Studio: opening it, choosing a project in it, saving back to it.</summary>
public sealed partial class MainWindow
{
    DispatcherTimer? _companyTimer;
    bool _companyPolling;

    /// <summary>
    /// OSAS chooses the company and signs the person in once. Opens only with OSAS_COMPANY + one-time OSAS_TICKET.
    /// O-CoreOp never asks for a company password.
    /// </summary>
    async Task StartFromSuiteAsync()
    {
        var path = Environment.GetEnvironmentVariable("OSAS_COMPANY");
        var ticket = Environment.GetEnvironmentVariable("OSAS_TICKET");
        Environment.SetEnvironmentVariable("OSAS_TICKET", null);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !CompanyService.EngineAvailable)
            return;
        if (string.IsNullOrWhiteSpace(ticket))
        {
            AppNotify.Info("Company file", "Sign in to the company in OSAS, then open O-CoreOp from OSAS. This app does not ask for a password.");
            return;
        }
        await Task.Delay(800);
        try
        {
            await CompanyService.OpenWithTicketAsync(path, ticket);
            AppNotify.Success("Company file", $"{CompanyService.Current!.Name} is open, signed in as {CompanyService.Current.User}.");
            CompanyProjects_Click(this, new RoutedEventArgs());
        }
        catch (OfficeException ex)
        {
            AppNotify.Warning("Company file", ex.Message + " Open O-CoreOp again from OSAS while signed in.");
        }
    }

    void StartCompanyPolling()
    {
        CompanyService.Changed += () => DispatcherQueue.TryEnqueue(RefreshCompanyBadge);
        _companyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _companyTimer.Tick += async (_, _) => await CompanyPollAsync();
        _companyTimer.Start();
        RefreshCompanyBadge();
    }

    void RefreshCompanyBadge()
    {
        if (CompanyBadgeText is null) return;
        CompanyBadgeText.Text = CompanyService.StatusText;
        AutomationProperties.SetName(CompanyBadge, CompanyService.StatusText);
        ToolTipService.SetToolTip(CompanyBadge, CompanyService.IsOpen
            ? $"{CompanyService.Current!.Path}. Shared with O-Studio — tap for its projects."
            : "The company file shared with O-Studio, chosen in OSAS — tap to open it.");
    }

    async Task CompanyPollAsync()
    {
        if (_companyPolling || !CompanyService.IsOpen) return;
        _companyPolling = true;
        try
        {
            switch (await CompanyService.PollAsync())
            {
                case CompanyService.PollResult.ReloadNow:
                    var message = await CompanyService.ReloadOpenAsync();
                    AfterProjectLoaded();
                    AppNotify.Info("Updated from the other app", message, 5000);
                    break;
                case CompanyService.PollResult.ConflictPending:
                    AppNotify.Warning("Changed in another app", "This project was saved in another app and you have unsaved changes here. Saving will ask you to reload first, so nothing is overwritten.", 9000);
                    break;
            }
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("Company file", ex.Message);
            CompanyService.Close();
        }
        finally
        {
            _companyPolling = false;
        }
    }

    void CompanyBadge_Tapped(object sender, TappedRoutedEventArgs e) => CompanyProjects_Click(sender, e);

    bool EngineReady()
    {
        if (CompanyService.EngineAvailable) return true;
        AppNotify.Warning("Company file", CompanyService.MissingEngine);
        return false;
    }

    /// <summary>
    /// The company is chosen and signed in only in OSAS. This never asks for a password — it starts OSAS so the person can open O-CoreOp from there.
    /// </summary>
    async void CompanyOpen_Click(object sender, RoutedEventArgs e)
    {
        if (!EngineReady()) return;
        if (CompanyService.IsOpen)
        {
            AppNotify.Info("Company file", $"{CompanyService.Current!.Name} is already open.");
            return;
        }
        if (CompanyService.StartOsas())
            AppNotify.Info("Company file", "Sign in to the company in OSAS, then open O-CoreOp from OSAS. This app does not ask for a password.");
        else
            AppNotify.Warning("Company file", "OSAS, the suite app, is not installed. Sign-in happens only there.");
        await Task.CompletedTask;
    }

    void CompanyClose_Click(object sender, RoutedEventArgs e)
    {
        if (!CompanyService.IsOpen) return;
        ProjectStore.Current.Shared = null;
        CompanyService.Close();
        AppNotify.Info("Company file", "Closed. The open project is now only in this window; use Save as to keep a copy.");
    }

    async void CompanyProjects_Click(object sender, RoutedEventArgs e)
    {
        if (!EngineReady()) return;
        if (!CompanyService.IsOpen)
        {
            AppNotify.Info("Company file", "The company is chosen in OSAS: use File > Open the company OSAS chose.");
            return;
        }
        IReadOnlyList<SharedProject> projects;
        try
        {
            projects = await CompanyService.ProjectsAsync();
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("Company projects", ex.Message);
            return;
        }

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 320, MinWidth = 460 };
        AutomationProperties.SetName(list, "Projects in the company file");
        foreach (var p in projects)
        {
            var line = new StackPanel();
            line.Children.Add(new TextBlock { Text = p.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            var sub = string.Join(" · ", new[] { p.Summary, p.ClientName, p.SiteAddress }.Where(s => !string.IsNullOrWhiteSpace(s)));
            line.Children.Add(new TextBlock { Text = sub, FontSize = 12, Opacity = 0.8 });
            list.Items.Add(new ListViewItem { Content = line, Tag = p });
        }
        var empty = new TextBlock { Text = "The company file has no projects yet. Make one with New project, or in O-Studio.", TextWrapping = TextWrapping.Wrap, Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = CompanyService.Current!.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(empty);
        panel.Children.Add(list);
        var dialog = new ContentDialog
        {
            Title = "Company projects",
            Content = panel,
            PrimaryButtonText = "Open",
            SecondaryButtonText = "New project…",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            IsPrimaryButtonEnabled = false,
        };
        list.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = list.SelectedItem is not null;
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            await NewCompanyProjectAsync();
            return;
        }
        if (result != ContentDialogResult.Primary || (list.SelectedItem as ListViewItem)?.Tag is not SharedProject chosen) return;
        if (!await ConfirmLeaveProjectAsync()) return;
        try
        {
            var message = await CompanyService.LoadIntoAppAsync(chosen);
            AfterProjectLoaded();
            AppNotify.Success("Project opened", message);
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("Company projects", ex.Message);
        }
    }

    async Task<bool> ConfirmLeaveProjectAsync()
    {
        if (!ProjectStore.Current.IsDirty) return true;
        var dialog = new ContentDialog
        {
            Title = "Discard unsaved changes?",
            Content = "The project open now has changes that have not been saved.",
            PrimaryButtonText = "Discard",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    void AfterProjectLoaded()
    {
        RefreshWindowTitle();
        UpdatePersonaBadge();
        RefreshSyncBadge();
        SelectNavTag("dashboard");
    }

    async Task NewCompanyProjectAsync()
    {
        IReadOnlyList<SharedClient> clients;
        try
        {
            clients = await CompanyService.ClientsAsync();
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("New project", ex.Message);
            return;
        }
        var name = new TextBox { Header = "Project name" };
        var client = new ComboBox { Header = "Client", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Choose a client" };
        foreach (var c in clients)
            client.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Id });
        var newClient = new TextBox { Header = clients.Count == 0 ? "Client name" : "Or a new client's name" };
        var site = new TextBox { Header = "Site address (optional)" };
        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        panel.Children.Add(name);
        if (clients.Count > 0) panel.Children.Add(client);
        panel.Children.Add(newClient);
        panel.Children.Add(site);
        var dialog = new ContentDialog
        {
            Title = "New company project",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        long? clientId = (client.SelectedItem as ComboBoxItem)?.Tag as long?;
        if (name.Text.Trim().Length == 0 || (clientId is null && newClient.Text.Trim().Length == 0))
        {
            AppNotify.Warning("New project", "Enter a project name and choose or name a client.");
            return;
        }
        if (!await ConfirmLeaveProjectAsync()) return;
        try
        {
            var id = await CompanyService.CreateProjectAsync(name.Text.Trim(), clientId, newClient.Text.Trim(), site.Text.Trim());
            var created = (await CompanyService.ProjectsAsync()).First(p => p.Id == id);
            var message = await CompanyService.LoadIntoAppAsync(created);
            AfterProjectLoaded();
            AppNotify.Success("Project created", message);
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("New project", ex.Message);
        }
    }

    /// <summary>Save for a project that lives in the company file.</summary>
    async Task SaveToCompanyAsync()
    {
        try
        {
            await CompanyService.SaveCurrentAsync();
            RefreshWindowTitle();
            AppNotify.Success("Saved", $"{ProjectStore.Current.Name} · company file revision {ProjectStore.Current.Shared!.Revision}");
        }
        catch (OfficeException ex) when (ex.Message.Contains("changed in another app", StringComparison.OrdinalIgnoreCase))
        {
            await OfferReloadAsync();
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("Save failed", ex.Message);
        }
    }

    async Task OfferReloadAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Changed in another app",
            Content = "This project was saved in another app since you opened it, so saving now would overwrite that. Reload it to see the other change, or keep your version and save it to a project file with Save as.",
            PrimaryButtonText = "Reload (discard my changes)",
            CloseButtonText = "Keep mine",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var link = ProjectStore.Current.Shared;
        if (link is null) return;
        try
        {
            var project = (await CompanyService.ProjectsAsync()).FirstOrDefault(p => p.Id == link.ProjectId);
            if (project is null)
            {
                AppNotify.Error("Reload", "That project is no longer in the company file.");
                return;
            }
            var message = await CompanyService.LoadIntoAppAsync(project);
            AfterProjectLoaded();
            AppNotify.Success("Reloaded", message);
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("Reload", ex.Message);
        }
    }

    /// <summary>Puts the open project (for example one opened from a project file) into the company file, as a project's document.</summary>
    async void CompanySaveHere_Click(object sender, RoutedEventArgs e)
    {
        if (!EngineReady()) return;
        if (!CompanyService.IsOpen)
        {
            AppNotify.Info("Company file", "The company is chosen in OSAS: use File > Open the company OSAS chose.");
            return;
        }
        IReadOnlyList<SharedProject> projects;
        try
        {
            projects = await CompanyService.ProjectsAsync();
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("Save to company file", ex.Message);
            return;
        }
        var choice = new ComboBox { Header = "Save it as", HorizontalAlignment = HorizontalAlignment.Stretch };
        choice.Items.Add(new ComboBoxItem { Content = $"A new company project named “{ProjectStore.Current.Name}”", Tag = 0L });
        foreach (var p in projects)
            choice.Items.Add(new ComboBoxItem { Content = $"The project {p.Name} ({p.ClientName})", Tag = p.Id });
        choice.SelectedIndex = 0;
        var client = new TextBox { Header = "Client name (for a new project)", Text = ProjectStore.Current.Info.ClientName };
        var panel = new StackPanel { Spacing = 10, MinWidth = 400 };
        panel.Children.Add(new TextBlock { Text = "Saving to an existing project replaces what the company file holds for it, and keeps the earlier revisions.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(choice);
        panel.Children.Add(client);
        var dialog = new ContentDialog
        {
            Title = "Save to company file",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var target = (choice.SelectedItem as ComboBoxItem)?.Tag as long? ?? 0;
            if (target == 0)
            {
                var clientName = client.Text.Trim();
                if (clientName.Length == 0)
                {
                    AppNotify.Warning("Save to company file", "Enter the client's name for the new project.");
                    return;
                }
                var existing = (await CompanyService.ClientsAsync()).FirstOrDefault(c => string.Equals(c.Name, clientName, StringComparison.OrdinalIgnoreCase));
                target = await CompanyService.CreateProjectAsync(ProjectStore.Current.Name, existing?.Id, clientName, ProjectStore.Current.Info.Location);
            }
            await CompanyService.SaveCurrentAsync(target);
            RefreshWindowTitle();
            AppNotify.Success("Saved", $"{ProjectStore.Current.Name} is in the company file. It is now saved there with Save.");
        }
        catch (OfficeException ex)
        {
            AppNotify.Error("Save to company file", ex.Message);
        }
    }
}
