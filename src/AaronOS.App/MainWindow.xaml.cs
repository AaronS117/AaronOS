using AaronOS.Core;
using Wpf.Ui.Controls;

namespace AaronOS_App;

public sealed partial class MainWindow : FluentWindow
{
    public MainWindow(IEnumerable<IAppModule> modules)
    {
        InitializeComponent();

        foreach (var module in modules)
        {
            NavView.MenuItems.Add(new NavigationViewItem
            {
                Content = module.DisplayName,
                Icon = new SymbolIcon { Symbol = Enum.Parse<SymbolRegular>(module.IconGlyph) },
                TargetPageType = module.HomePageType
            });
        }

        Loaded += (_, _) =>
        {
            if (NavView.MenuItems.Count > 0 && NavView.MenuItems[0] is NavigationViewItem { TargetPageType: { } firstPageType })
            {
                NavView.Navigate(firstPageType);
            }
        };
    }
}
