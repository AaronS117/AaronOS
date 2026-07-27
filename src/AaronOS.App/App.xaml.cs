using AaronOS_App.ViewModels;
using AaronOS.Core;
using AaronOS.Core.Data;
using AaronOS.Modules.BodyMeasurements;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;

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

        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AaronOS", "aaronos.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        // The full list of registered modules. Adding a module means adding a project
        // reference plus one line here — see docs/MODULE_GUIDELINES.md.
        IAppModule[] modules = [new BodyMeasurementsModule()];

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

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        using (var scope = Services.CreateScope())
        {
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AaronOsDbContext>>();
            await using var db = await dbContextFactory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
        }

        _window = new MainWindow(Services.GetServices<IAppModule>());
        _window.Activate();
    }
}
