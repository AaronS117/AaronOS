using AaronOS.Core;
using AaronOS.Modules.Medical.Data;
using AaronOS.Modules.Medical.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace AaronOS.Modules.Medical.Views;

public sealed partial class VisitsPage : Page
{
    public VisitsViewModel ViewModel { get; }

    public VisitsPage()
    {
        ViewModel = AppServices.Provider.GetRequiredService<VisitsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    // File dialogs and shell launches are view concerns, so they stay in code-behind and hand the
    // ViewModel nothing but a path.
    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a document to attach",
            Filter = "Documents and images (*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.txt)|*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.txt|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            ViewModel.SetDocumentFile(dialog.FileName);
        }
    }

    private void OpenDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MedicalDocument document })
        {
            return;
        }

        try
        {
            // UseShellExecute so the file opens in whatever the user has associated with it.
            Process.Start(new ProcessStartInfo(document.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"Could not open that file: {ex.Message}";
        }
    }

    private void DeleteVisit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MedicalVisit item })
        {
            _ = ViewModel.DeleteVisitCommand.ExecuteAsync(item);
        }
    }

    private void DeleteProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Provider item })
        {
            _ = ViewModel.DeleteProviderCommand.ExecuteAsync(item);
        }
    }

    private void DeleteDocument_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MedicalDocument item })
        {
            _ = ViewModel.DeleteDocumentCommand.ExecuteAsync(item);
        }
    }
}
