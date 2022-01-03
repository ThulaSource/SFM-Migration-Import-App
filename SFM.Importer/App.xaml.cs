using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SFM.Importer.Configuration;
using SFM.Importer.Services;
using System;
using System.Reflection;
using System.Windows;

namespace SFM.Importer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public IServiceProvider ServiceProvider { get; private set; }
        public IConfiguration Configuration { get; private set; }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An unhandled exception just occurred: {e.Exception.Message}", "Exception", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
        
        private void OnStartup(object sender, StartupEventArgs e)
        {
            try
            {
                var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyFileVersionAttribute>().Version;
               
                ServiceCollection services = new ServiceCollection();
                var builder = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", true, false);
                Configuration = builder.Build();

                Log.Logger = new LoggerConfiguration()
                    .Enrich.FromLogContext()
                    .ReadFrom.Configuration(Configuration)
                    .WriteTo.File(Configuration["AppSettings:LogFile"] ?? @"c:\SFM.Importer.log")
                    .CreateLogger();

                services
                    .AddLogging(loggingBuilder => loggingBuilder.AddSerilog())
                    .Configure<AppSettings>(Configuration.GetSection(nameof(AppSettings)))
                    .AddSingleton<MainWindow>()
                    .AddSingleton<ImportLedgerHandler>()
                    .AddSingleton<UploadScheduler>();

                // The program has to be called with a HelseId Auth Token
                if (e != null && e.Args.Length > 0)
                {
                    services.PostConfigure<AppSettings>(
                        options => { options.Token = e.Args[0]; });

                    ServiceProvider = services.BuildServiceProvider();
                    var mainWindow = ServiceProvider.GetService<MainWindow>();
                    if (version != null)
                    {
                        mainWindow.Title += $" - {version}";
                    }
                    mainWindow.Show();
                }
                else
                {
                    MessageBox.Show("HelseId authorization token missing on the command line", "Info",
                        MessageBoxButton.OK, MessageBoxImage.Exclamation);
                    Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading application. {ex.Message} {ex.StackTrace}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown();
            }
        }
        
    }
}
