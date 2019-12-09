namespace PictureRenamerWithHangfire
{
    using System;

    using Hangfire;
    using Hangfire.MemoryStorage;

    using LiteDB;

    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Options;
    using Microsoft.OpenApi.Models;

    using Serilog;

    public class Startup
    {
        private readonly IConfiguration config;

        public Startup(IConfiguration config)
        {
            this.config = config;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IHostApplicationLifetime lifetime)
        {
            var serviceProvider = app.ApplicationServices.GetRequiredService<IServiceProvider>();
            GlobalConfiguration.Configuration.UseActivator(new ContainerJobActivator(serviceProvider));

            lifetime.ApplicationStopping.Register(
                () =>
                    {
                        var db = app.ApplicationServices.GetRequiredService<LiteDatabase>();
                        Log.Information("Disposing LiteDatabase..");
                        db.Dispose();
                    });

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseSerilogRequestLogging();

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1"); });

            app.UseHangfireDashboard();

            app.UseRouting();
            app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<PathConfig>(this.config.GetSection("Paths"));

            services.AddHangfire(
                configuration =>
                    {
                        configuration.UseMemoryStorage();
                        configuration.UseSerilogLogProvider();
                    });
            services.AddHangfireServer();

            services.AddHostedService<CustomWorker>();

            services.AddControllers();

            services.Configure<ScanOptions>(
                opts =>
                    {
                        opts.Input = @"Y:\Import-Queue";
                        opts.Output = @"Y:\Kalender 2020";
                        opts.RecycleBin = @"Y:\Duplicates";
                    });

            services.AddSingleton(this.LiteDatabaseFactory);

            // Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen(c => { c.SwaggerDoc("v1", new OpenApiInfo { Title = "My API", Version = "v1" }); });
        }

        private LiteDatabase LiteDatabaseFactory(IServiceProvider arg)
        {
            var optionsProvider = arg.GetRequiredService<IOptions<ScanOptions>>();
            var options = optionsProvider.Value;
            return new LiteDatabase(options.ScanDatabasePath);
        }
    }
}