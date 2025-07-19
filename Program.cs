using SignalBus.Services;
using Microsoft.Extensions.Configuration;

namespace SignalBus
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);
            
            // Explicitly add user secrets in development
            if (builder.Environment.IsDevelopment())
            {
                builder.Configuration.AddUserSecrets<Program>();
            }
            
            builder.Services.AddHostedService<Worker>();
            
            // Register HttpClient and services
            builder.Services.AddHttpClient<IAssistantService, AssistantService>();
            builder.Services.AddHttpClient<ISignalService, SignalService>();
            builder.Services.AddHttpClient<ISignalGroupService, SignalGroupService>();
            builder.Services.AddSingleton<IAuthorizationService, AuthorizationService>();
            
            // Register TimescaleDB service as both hosted service and injectable service
            builder.Services.AddSingleton<ITimescaleDbService, TimescaleDbService>();
            builder.Services.AddHostedService<TimescaleDbService>(provider => 
                (TimescaleDbService)provider.GetRequiredService<ITimescaleDbService>());

            var host = builder.Build();
            
            // Initialize the database before starting the host
            var timescaleDbService = host.Services.GetRequiredService<ITimescaleDbService>();
            // Ensure database exists first
            bool databaseExists = timescaleDbService.EnsureDatabaseExistsAsync().GetAwaiter().GetResult();
            
            if (!databaseExists)
            {
                try
                {
                    timescaleDbService.InitializeDatabaseAsync().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize database: {ex.Message}");
                    Environment.Exit(1);
                }
            }
            
            host.Run();
        }
    }
}
