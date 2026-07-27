using AaronOS.Core;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

public sealed partial class ImportPage : Page
{
    public ImportViewModel ViewModel { get; }

    public ImportPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<ImportViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    // File dialogs are a view concern, so the ViewModel only ever receives the chosen path.
    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a MyChart record export",
            Filter = "Health records (*.xml;*.ccda;*.cda)|*.xml;*.ccda;*.cda|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.SetFile(dialog.FileName);
        }
    }
}
