
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Identity.Api.Messaging.Consumers;
using Identity.Api.Models;
using Identity.Api.Services;
using MassTransit;
using MassTransit.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using StackExchange.Redis;
using Swashbuckle.AspNetCore.Swagger;
using System;

namespace Identity.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            // Init Serilog configuration
            Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger();
            Configuration = configuration;
        }

        public IContainer ApplicationContainer { get; private set; }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();

            // Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen(c => { c.SwaggerDoc("v1", new Info {Title = "Identity API", Version = "v1"}); });

            //By connecting here we are making sure that our service
            //cannot start until redis is ready. This might slow down startup,
            //but given that there is a delay on resolving the ip address
            //and then creating the connection it seems reasonable to move
            //that cost to startup instead of having the first request pay the
            //penalty.

            try
            {
                var redisCon = Configuration["RedisHost"];
                if (!string.IsNullOrWhiteSpace(redisCon))
                {
                    Log.Logger.Debug("Using Redis Instance : {redisCon}.");

                    services.AddSingleton(sp =>
                    {
                        var configuration = new ConfigurationOptions {ResolveDns = true};
                        //configuration.EndPoints.Add(Configuration["RedisHost"]);
                        configuration.EndPoints.Add(redisCon);

                        return ConnectionMultiplexer.Connect(configuration);
                    });
                }

                Log.Logger.Debug("Redis Instance not found : {redisCon}. Attempting to continue.");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error setting up Redis Instance. Exception.", ex);
                var error = ex.ToString();
            }

            services.AddTransient<IIdentityRepository, IdentityRepository>();
            var builder = new ContainerBuilder();

            try
            {
                var rabbitMq = Configuration["RabbitMqHost"];
                if (!string.IsNullOrWhiteSpace(rabbitMq))
                {
                    var rabbitMqInstance = $"rabbitmq://{rabbitMq}:15672/";

                    Log.Logger.Debug("Using RabbitMq Instance : {rabbitMqInstance}.");

                    // register a specific consumer
                    builder.RegisterType<ApplicantAppliedEventConsumer>();

                    builder.Register(context =>
                        {
                            var busControl = Bus.Factory.CreateUsingRabbitMq(cfg =>
                            {
                                //var host = cfg.Host(new Uri("rabbitmq://localhost/"), h =>
                                var host = cfg.Host(new Uri(rabbitMqInstance), h =>
                                {
                                    h.Username("guest");
                                    h.Password("guest");
                                });

                                // https://stackoverflow.com/questions/39573721/disable-round-robin-pattern-and-use-fanout-on-masstransit
                                cfg.ReceiveEndpoint(host, "dotnetgigs" + Guid.NewGuid().ToString(), e =>
                                {
                                    e.LoadFrom(context);
                                    //e.Consumer<ApplicantAppliedConsumer>();
                                });
                            });

                            return busControl;
                        })
                        .SingleInstance()
                        .As<IBusControl>()
                        .As<IBus>();

                    Log.Logger.Debug("RabbitMq setup complete : {rabbitMqInstance}.");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error setting up RabbitMq Instance. Exception.", ex);
            }

            builder.Populate(services);
            ApplicationContainer = builder.Build();
            return new AutofacServiceProvider(ApplicationContainer);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public async void Configure(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider serviceProvider,
            IApplicationLifetime lifetime, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            // logging
            loggerFactory.AddSerilog();

            app.UseMvc();
            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.), 
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "Identity API V1"); });

            try
            {
                // stash an applicant's user data in redis for test purposes...this would simulate establishing auth/session in the real world
                var identityRepository = serviceProvider.GetService<IIdentityRepository>();
                await identityRepository.UpdateUserAsync(new User {Id = "1", Email = "josh903902@gmail.com", Name = "Josh Dillinger"});
                await identityRepository.UpdateUserAsync(new User {Id = "2", Email = "fred@yabbayabbado.com", Name = "Fred Flintstone"});
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error in Redis Instance. Exception.", ex);
            }

            try
            {
                var bus = ApplicationContainer.Resolve<IBusControl>();
                var busHandle = TaskUtil.Await(() => bus.StartAsync());
                lifetime.ApplicationStopping.Register(() => busHandle.Stop());
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error in RabbitMq Instance. Exception.", ex);
            }
        }
    }
}