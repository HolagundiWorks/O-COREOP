// SPDX-License-Identifier: AGPL-3.0-or-later
// SPDX-FileCopyrightText: 2026 Human Centric Works, Hospet

using BBSApp.Services;
using BBSApp.Services.Company;
using BBSApp.Services.Odp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Office.Shared;

namespace BBSApp;

/// <summary>
/// The company file belongs to O-Studio. O-CoreOp reads firm and project identity from it and mirrors estimate and
/// running-bill abstracts back (<c>aqc.project</c>) when the open <c>.osp</c> is linked to a Studio project.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Opens the company file read-only when Studio launched this app with a ticket. O-CoreOp never asks for a company password.</summary>
    async Task StartFromSuiteAsync()
    {
        var path = Environment.GetEnvironmentVariable(SuiteWire.EnvCompany);
        var ticket = Environment.GetEnvironmentVariable(SuiteWire.EnvTicket);
        Environment.SetEnvironmentVariable(SuiteWire.EnvTicket, null);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !CompanyService.EngineAvailable)
            return;
        if (string.IsNullOrWhiteSpace(ticket))
        {
            AppNotify.Info("Company", "Sign in to the company in Studio, then open O-CoreOp from its card to read the company's details.");
            return;
        }
        await Task.Delay(800);
        try
        {
            await CompanyService.OpenWithTicketAsync(path, ticket);
            AppNotify.Success("Company", $"{CompanyService.Current!.Name}: details are read from Studio's company file.");
            await LinkOpenProjectToStudioAsync();
        }
        catch (OfficeException ex)
        {
            AppNotify.Warning("Company", ex.Message + " Open O-CoreOp again from Studio while signed in.");
        }
    }

    void StartCompanyPolling()
    {
        CompanyService.Changed += () => DispatcherQueue.TryEnqueue(RefreshCompanyBadge);
        RefreshCompanyBadge();
    }

    void RefreshCompanyBadge()
    {
        if (CompanyBadgeText is null) return;
        CompanyBadgeText.Text = CompanyService.StatusText;
        AutomationProperties.SetName(CompanyBadge, CompanyService.StatusText);
        ToolTipService.SetToolTip(CompanyBadge, CompanyService.IsOpen
            ? $"{CompanyService.Current!.Path}. Identity is read from Studio; estimate and RA abstracts are mirrored when this project is linked."
            : "Company details come from Studio: open O-CoreOp from its card after signing in.");
    }

    /// <summary>
    /// Take the open project's details from Studio. The project is the one the take-off header names (company and project id); a header
    /// of another company, or none, leaves the details as they are.
    /// </summary>
    internal async Task LinkOpenProjectToStudioAsync(string? companyUuid = null, long? projectId = null)
    {
        if (!CompanyService.IsOpen) return;
        if (companyUuid is null || projectId is null)
        {
            var path = ProjectStore.Current.FilePath;
            if (!ProjectFiles.IsOdp(path) || !File.Exists(path)) return;
            TakeoffSource source = new TakeoffSource.Project(path!);
            (string? Company, long? ProjectId) head;
            try { head = await OdpTakeoffService.FirstProjectRefAsync(source); }
            catch (Exception) { return; }
            (companyUuid, projectId) = (head.Company, head.ProjectId);
        }
        if (projectId is null) return;
        try
        {
            if (await CompanyService.LinkProjectAsync(companyUuid, projectId.Value))
            {
                RefreshWindowTitle();
                UpdatePersonaBadge();
            }
        }
        catch (OfficeException ex)
        {
            AppNotify.Warning("Company", ex.Message);
        }
    }

    /// <summary>The company badge only tells where the details come from.</summary>
    async void CompanyBadge_Click(object sender, RoutedEventArgs e)
    {
        var text = CompanyService.IsOpen
            ? $"{CompanyService.Current!.Name}\nSigned in as {CompanyService.Current.User}\n{CompanyService.Current.Path}\n\n"
              + (ProjectStore.Current.Shared is { } link ? $"This project is Studio project {link.ProjectId}. Its details are read from the company file." : "This project is not linked to a Studio project (the take-off header of a Studio project's drawing links it).")
            : "Company details are read from O-Studio's company file. Sign in in Studio and open O-CoreOp from its card.";
        await new ContentDialog { Title = "Company", Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, CloseButtonText = "Close", XamlRoot = Content.XamlRoot }.ShowAsync();
    }
}
