using AaronOS.Core;
using AaronOS_App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS_App.Pages;

/// <summary>A module's settings block: its name, and the control it supplied.</summary>
public record ModuleSettingsSection(string Title, FrameworkElement Content);

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    /// <summary>Populated from every registered module that declares a SettingsContentType. Kept on
    /// the page rather than the ViewModel because instantiating controls is a view concern.</summary>
    public List<ModuleSettingsSection> ModuleSections { get; } = [];

    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        BuildModuleSections();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void BuildModuleSections()
    {
        foreach (var module in App.Services.GetServices<IAppModule>())
        {
            if (module.SettingsContentType is not { } contentType)
            {
                continue;
            }

            if (Activator.CreateInstance(contentType) is FrameworkElement content)
            {
                ModuleSections.Add(new ModuleSettingsSection(module.DisplayName.ToUpperInvariant(), content));
            }
        }
    }
}
