namespace PictureRenamerWithHangfire
{
    using Microsoft.AspNetCore;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;

    using Serilog;
    using Serilog.Events;

    public class Program
    {
        public static IWebHostBuilder CreateHostBuilder(string[] args) =>
            WebHost
                .CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder =>
                    {
                        builder.AddEnvironmentVariables();
                        builder.AddJsonFile("appsettings.json");
                    })
                .UseStartup<Startup>()
                .UseSerilog();

        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Debug().WriteTo.Console()
                .CreateLogger();

            Log.Information("Creating Host Builder and starting it...");
            CreateHostBuilder(args).Build().Run();
        }
    }
}