﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using CFT.NanoFabric.Core;
using CFT.NanoFabric.AspNetCore;
using Microsoft.Extensions.Options;
using CFT.NanoFabric.RegistryHost.ConsulRegistry;
using System.IO;
using Microsoft.Extensions.PlatformAbstractions;
using Winton.Extensions.Configuration.Consul;
using System.Threading;

namespace SampleService.Kestrel
{
    public class Startup
    {
        private readonly CancellationTokenSource _consulConfigCancellationTokenSource = new CancellationTokenSource();

        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                //.AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                //.AddEnvironmentVariables()
                .AddConsul(
                    $"sampleservicesettings.json",
                    _consulConfigCancellationTokenSource.Token,
                    options => {
                        options.ConsulConfigurationOptions = (cco) => {
                            cco.Address = new Uri("http://10.125.32.121:8500");
                        };
                        options.Optional = true;
                        options.ReloadOnChange = true;
                        options.OnLoadException = (exceptionContext) => {
                            exceptionContext.Ignore = true;
                        };
                    })
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        /// <summary>
        /// 系统配置
        /// </summary>
        public IConfigurationRoot Configuration { get; }

        /// This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            var appSettings = new AppSettings();
            Configuration.Bind(appSettings);
            var consulConfig = new ConsulRegistryHostConfiguration
            {
                HostName = appSettings.Consul.HostName,
                Port = appSettings.Consul.Port
            };
            services.AddNanoFabric(() => new ConsulRegistryHost(consulConfig));
            services.AddMvc();
            services.AddOptions();
            services.AddSwaggerGen();
            services.ConfigureSwaggerGen(options =>
            {
                options.SingleApiVersion(new Swashbuckle.Swagger.Model.Info
                {
                    Version = "v1",
                    Title = "Sample Web ",
                    Description = "RESTful API for My Web Application",
                    TermsOfService = "None"
                });
                options.IncludeXmlComments(Path.Combine(PlatformServices.Default.Application.ApplicationBasePath,
                    "SampleService.Kestrel.xml"));
                options.DescribeAllEnumsAsStrings();
            });
        }

        /// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, IApplicationLifetime applicationLifetime)
        {
            var log = loggerFactory
                      .AddNLog()
                      //.AddConsole()
                      //.AddDebug()
                      .CreateLogger<Startup>();

            loggerFactory.ConfigureNLog("NLog.config");

            app.UseMvc(routes =>
             {
                 routes.MapRoute(
                     name: "default",
                     template: "{controller=Home}/{action=Index}/{id?}");
             });


            app.UseSwagger((httpRequest, swaggerDoc) =>
            {
                swaggerDoc.Host = httpRequest.Host.Value;
            });
            app.UseSwaggerUi();



            // add tenant & health check
            var localAddress = DnsHelper.GetIpAddressAsync().Result;
            var uri = new Uri($"http://{localAddress}:{Program.PORT}/");
            log.LogInformation("Registering tenant at ${uri}");
            var registryInformation = app.AddTenant("values", "1.0.0-pre", uri, tags: new[] { "urlprefix-/values" });
            log.LogInformation("Registering additional health check");
            //var checkId = app.AddHealthCheck(registryInformation, new Uri(uri, "status"), TimeSpan.FromSeconds(15), "status");

            // prepare checkId for options injection
            //app.ApplicationServices.GetService<IOptions<HealthCheckOptions>>().Value.HealthCheckId = checkId;

            // register service & health check cleanup
            applicationLifetime.ApplicationStopping.Register(() =>
            {
                log.LogInformation("Removing tenant & additional health check");
                //app.RemoveHealthCheck(checkId);
                app.RemoveTenant(registryInformation.Id);
                _consulConfigCancellationTokenSource.Cancel();
            });
        }
    }
}
