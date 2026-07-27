using AaronOS_App.ViewModels;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.BodyMeasurements;
using AaronOS.Modules.Finance;
using AaronOS.Modules.Nutrition;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Windows;

namespace AaronOS_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private Window? _window;

    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        // A modular app should not die because one module threw. Without this, an exception in any
        // page's load path silently closed the whole window with no message, which is very hard to
        // diagnose from the outside. Show what happened and keep running where possible.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                args.Exception.ToString(),
                "AaronOS hit an unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AaronOS", "aaronos.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // The full list of registered modules. Adding a module means adding a project
        // reference plus one line here — see docs/MODULE_GUIDELINES.md.
        IAppModule[] modules = [new BodyMeasurementsModule(), new FinanceModule(), new NutritionModule()];

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddDbContextFactory<AaronOsDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
                services.AddTransient<SettingsViewModel>();

                foreach (var module in modules)
                {
                    services.AddSingleton(module);
                    module.RegisterServices(services);
                }
            })
            .Build();

        Services = _host.Services;
        AppServices.Provider = _host.Services;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        using (var scope = Services.CreateScope())
        {
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AaronOsDbContext>>();
            await using var db = await dbContextFactory.CreateDbContextAsync();
            // Creates the database on first run AND adds tables for modules registered after it
            // already existed — EnsureCreatedAsync alone silently skips those. See SchemaBootstrapper.
            await SchemaBootstrapper.EnsureSchemaAsync(db);
        }

        _window = new MainWindow(Services.GetServices<IAppModule>());
        _window.Show();
    }
}
