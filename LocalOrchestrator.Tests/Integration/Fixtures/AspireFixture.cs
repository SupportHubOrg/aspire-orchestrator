using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace LocalOrchestrator.Tests.Integration.Fixtures;

/// <summary>
/// Test fixture that sets up the Aspire Orchestrator environment for integration testing.
/// </summary>
public class AspireFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    
    public HttpClient GatewayClient { get; private set; } = null!;
    public HttpClient KeycloakClient { get; private set; } = null!;
    
    public async Task InitializeAsync()
    {
        //Initialize the testing builder to randomize ports and isolate the test environment, then build and start the distributed application
        var appBuilder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.LocalOrchestrator>();

        //Configure the application to run in test mode
        appBuilder.Configuration["RuntimeOptions:IsTestMode"] = "true";

        //Configure logging for the application services
        appBuilder.Services.AddLogging(logging => 
        {
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        //Build and start the distributed application
        App = await appBuilder.BuildAsync();
        await App.StartAsync();

        //Add a delay to allow the distributed application to start
        if (System.Diagnostics.Debugger.IsAttached)
        {
            await Task.Delay(5000); 
        }
        
        //Obtain pre-configured HttpClients that automatically route to the dynamically assigned endpoints
        GatewayClient = App.CreateHttpClient("api-gateway");
        KeycloakClient = App.CreateHttpClient("keycloak");
        
        //Wait for resources to reach a healthy state, preventing tests from hitting uninitialized containers
        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("api-gateway")
            .WaitAsync(TimeSpan.FromMinutes(5));
        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("keycloak")
            .WaitAsync(TimeSpan.FromMinutes(5));
        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("sql-server")
            .WaitAsync(TimeSpan.FromMinutes(5));
        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("organization-access")
            .WaitAsync(TimeSpan.FromMinutes(5));
        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("ticket-management")
            .WaitAsync(TimeSpan.FromMinutes(5));

        await Task.Delay(1500);
    }
    
    /// <summary>
    /// Retrieves a dynamically generated connection string for database setup/cleanup during testing.
    /// </summary>
    public async Task<string?> GetConnectionStringAsync(string resourceName)
    {
        return await App.GetConnectionStringAsync(resourceName);
    }
    
    /// <summary>
    /// Stops the distributed application and cleans up resources after tests have completed.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (App != null)
        {
            await App.StopAsync();
            await App.DisposeAsync();
        }
    }
}

/// <summary>
/// This class has no code, and is never created. Its purpose is simply
/// to be the place to apply [CollectionDefinition] and all the
/// ICollectionFixture<> interfaces so xUnit knows how to inject them.
/// </summary>
[CollectionDefinition("Aspire Integration Tests")]
public class AspireCollection : ICollectionFixture<AspireFixture>
{
}