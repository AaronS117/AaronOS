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
            Title = "Choose your MyChart record export(s)",
            // A MyChart health-summary download is a .zip in IHE XDM layout, so that is the default.
            // Multiselect because a record spread across health systems means one export per system.
            Filter = "MyChart exports (*.zip;*.xml;*.ccda;*.cda)|*.zip;*.xml;*.ccda;*.cda|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.SetFiles(dialog.FileNames);
        }
    }
}
