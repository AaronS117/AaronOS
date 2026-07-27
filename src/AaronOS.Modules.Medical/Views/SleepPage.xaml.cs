using AaronOS.Core;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

public sealed partial class SleepPage : Page
{
    public SleepViewModel ViewModel { get; }

    public SleepPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<SleepViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// PasswordBox has no bindable Password property by design, so the secret is pushed across here
    /// rather than two-way bound. It stays out of the visual tree's value as a result.
    /// </summary>
    private void SecretBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            ViewModel.ClientSecret = box.Password;
        }
    }
}
