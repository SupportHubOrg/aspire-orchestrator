using Microsoft.Extensions.DependencyInjection;
using Aspire.Hosting.ApplicationModel;
using Xunit;

using LocalOrchestrator.Tests.Integration.Fixtures;
using LocalOrchestrator.Tests.Integration.Helpers;
using LocalOrchestrator.Tests.Integration.Models;

namespace LocalOrchestrator.Tests;

/// <summary>
/// End-to-end test class that uses the AspireFixture to test the main flow of the application.
/// </summary>
[Collection("Aspire Integration Tests")]
public class EndToEndTest : IAsyncLifetime
{
    private readonly AspireFixture _fixture;
    private readonly HttpClientHelper _apiClient;
    private readonly KeycloakHelper _keycloakHelper;
    private DatabaseHelper _dbHelper = null!;
    private KafkaHelper _kafkaHelper = null!;
    
    public EndToEndTest(AspireFixture fixture)
    {
        _fixture = fixture;
        _apiClient = new HttpClientHelper(fixture.GatewayClient);
        _keycloakHelper = new KeycloakHelper(fixture.KeycloakClient);
    }
    
    /// <summary>
    /// Sets up the test environment by initializing helpers and databases.
    /// </summary>
    /// <returns></returns>
    public async Task InitializeAsync()
    {
        //Initialize helpers dynamically using Aspire's resolved connection strings
        _dbHelper = new DatabaseHelper(await _fixture.GetConnectionStringAsync("OrgDb"), await _fixture.GetConnectionStringAsync("TicketDb"));
        _kafkaHelper = new KafkaHelper(await _fixture.GetConnectionStringAsync("kafka"), 
            ["ticket-management-events", "organization-access-events", "ai-triage-events"]);
        _kafkaHelper.StartConsuming();
    }

    public async Task DisposeAsync()
    {
        //Clean up unmanaged resources and databases
        _kafkaHelper?.Dispose();
        
        //If the database helper is not null, clean it up
        if (_dbHelper != null)
        {
            await _dbHelper.ExecuteCleanupAsync();
        }
    }
    
    /// <summary>
    /// Tests the end-to-end workflow of common operations in the system.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task MainFlow_ExecutesSuccessfully_FromCreationToCleanup()
    {
        //STEP 1: User creates an account in Keycloak
        var managerId = await _keycloakHelper.CreateUserAsync("manager", "manager@test.com", "Pass123!");

        //STEP 2: Manager logs into their account as a manager
        var managerToken = await _keycloakHelper.LoginAsync("manager@test.com", "Pass123!");
        _apiClient.SetAuthToken(managerToken);

        //STEP 3: Manager requests creating a free organization
        var organizationPayload = new { Name = "Test Org", Tier = "FREE", ManagerKeycloakId = managerId, ManagerEmail = "manager@test.com", SlaFirstResponseMinutes = 0, SlaHumanResponseMinutes = 0, SlaResolutionMinutes = 0, AiTrustThreshold = 0 };
        var organizationResponse = await _apiClient.PostAsync<CreateOrganizationResponse>("/api/organizations", organizationPayload);
        var organizationId = organizationResponse.OrganizationId;
        
        //Check if the organization was saved to the database
        var isOrganizationSaved = await _dbHelper.WaitForStateAsync("OrgDb", $"SELECT COUNT(1) FROM Organizations WHERE Id = '{organizationId}'", "1");
        Assert.True(isOrganizationSaved, "Organization was not saved to the database.");

        //Check if the organization registered kafka event was published
        var kafkaOrganizationRegisteredEvent = await _kafkaHelper.WaitForMessageAsync("OrganizationRegistered");
        Assert.True(kafkaOrganizationRegisteredEvent, "OrganizationRegisteredEvent was not received.");

        //STEP 4: Manager confirms the email (Fake confirmation by reading token directly from DB)
        var organizationToken = await _dbHelper.GetOrganizationCreationTokenAsync(organizationId);
        Assert.False(string.IsNullOrEmpty(organizationToken), "Organization token was not generated.");

        var confirmationPayload = new { managerId };
        await _apiClient.PutAsync<object>($"/api/organizations/{organizationId}/creation-confirmation/{organizationToken}", confirmationPayload);

        //Check if the organization was confirmed
        var confirmedStatus = await _dbHelper.GetStringAsync("OrgDb", $"SELECT CreationConfirmationStatus FROM Organizations WHERE Id = '{organizationId}'");
        Assert.Equal("ACCEPTED", confirmedStatus);
        
        //Check if the organization created kafka event was published
        var kafkaOrganizationCreatedEvent = await _kafkaHelper.WaitForMessageAsync("OrganizationCreated");
        Assert.True(kafkaOrganizationCreatedEvent, "OrganizationCreatedEvent was not received.");

        //STEP 5: Manager sets the organization rules
        var rulesPayload = new { SlaFirstResponseMinutes = 60, SlaHumanResponseMinutes = 120, SlaResolutionMinutes = 1440, AiTrustThreshold = 85 };
        await _apiClient.PutAsync<object>($"/api/organizations/{organizationId}", rulesPayload);
        
        //Check if the organization settings updated kafka event was published
        var kafkaOrganizationSettingsUpdatedEvent = await _kafkaHelper.WaitForMessageAsync("OrganizationSettingsUpdated");
        Assert.True(kafkaOrganizationSettingsUpdatedEvent, "OrganizationSettingsUpdatedEvent was not received.");

        //STEP 6: Manager creates 2 new users

        //Add the users to keycloak
        var employeeId = await _keycloakHelper.CreateUserAsync("employee", "employee@test.com", "Pass123!");
        var operatorId = await _keycloakHelper.CreateUserAsync("operator", "operator@test.com", "Pass123!");

        //Add the users to the organization
        var employeePayload = new { KeycloakId = employeeId, Email = "emp@test.com", Role = "EMPLOYEE", OrganizationId = organizationId.ToString(), IsSupportHubEmployee = false };
        var operatorPayload = new { KeycloakId = operatorId, Email = "op@test.com", Role = "LEVEL_1_OPERATOR", OrganizationId = organizationId.ToString(), IsSupportHubEmployee = false };

        await _apiClient.PostAsync<object>("/api/organizations/users", employeePayload);
        await _apiClient.PostAsync<object>("/api/organizations/users", operatorPayload);
        
        //Check if the user invitation created kafka event was published
        var kafkaUserInvitationCreatedEvent = await _kafkaHelper.WaitForMessageAsync("UserInvitationCreated");
        Assert.True(kafkaUserInvitationCreatedEvent, "UserInvitationCreatedEvent was not received.");

        //STEP 7: Users confirm emails
        var empTokenFromDb = await _dbHelper.GetUserInvitationTokenAsync(employeeId);
        var opTokenFromDb = await _dbHelper.GetUserInvitationTokenAsync(operatorId);

        await _apiClient.PutAsync<object>($"/api/organizations/users/{employeeId}/invitation-confirmation/{empTokenFromDb}", new { });
        await _apiClient.PutAsync<object>($"/api/organizations/users/{operatorId}/invitation-confirmation/{opTokenFromDb}", new { });

        //Verify that all the user snapshots are present in the database
        var usersPresent = await _dbHelper.WaitForStateAsync("TicketDb", $"SELECT COUNT(1) FROM UserSnapshots WHERE OrganizationId = '{organizationId}'", "3");
        Assert.True(usersPresent, "Users were not saved to the database.");
        
        //Check if the user invitation accepted kafka event was published
        var kafkaUserInvitationAcceptedEvent = await _kafkaHelper.WaitForMessageAsync("UserInvitationAccepted");
        Assert.True(kafkaUserInvitationAcceptedEvent, "UserInvitationAcceptedEvent was not received.");

        //STEP 8: New users log in
        var employeeToken = await _keycloakHelper.LoginAsync("employee@test.com", "Pass123!");
        var operatorToken = await _keycloakHelper.LoginAsync("operator@test.com", "Pass123!");

        //STEP 9: Employee creates a ticket
        _apiClient.SetAuthToken(employeeToken);
        var ticketPayload = new { subject = "System Down", description = "Cannot access the main dashboard." };
        var ticketResponse = await _apiClient.PostAsync<CreateTicketResponse>("/api/tickets", ticketPayload);
        var ticketId = ticketResponse.TicketId;

        //Verify that the ticket was saved to the database
        var ticketSaved = await _dbHelper.WaitForStateAsync("TicketDb", $"SELECT COUNT(1) FROM Tickets WHERE Id = '{ticketId}'", "1");
        Assert.True(ticketSaved, "Ticket was not saved to the database.");

        //Verify that the ticket created kafka event was published
        var kafkaTicketCreatedEvent = await _kafkaHelper.WaitForMessageAsync("TicketCreated");
        Assert.True(kafkaTicketCreatedEvent, "TicketCreatedEvent was not received.");

        //STEP 10: AI response fails
        var failurePayload = new 
        {
            AnalysisId = Guid.NewGuid(),
            TicketId = ticketId,
            OrganizationId = organizationId,
            FailureCode = "AI_SERVICE_UNAVAILABLE",
            FailureMessage = "Mocked infrastructure timeout during E2E test."
        };
        
        //Mock the AI service failure
        await _kafkaHelper.PublishMockEventAsync("ai-triage-events", "AiAnalysisFailed", failurePayload);

        //Verify the ticket escalated kafka event was published
        var kafkaTicketEscalatedEvent = await _kafkaHelper.WaitForMessageAsync("TicketEscalated", 600);
        Assert.True(kafkaTicketEscalatedEvent, "TicketEscalatedEvent was not received.");

        //STEP 11: Manager assigns the ticket to the operator
        _apiClient.SetAuthToken(managerToken);
        await _apiClient.PutAsync<object>($"/api/tickets/{ticketId}", new { OperatorId = operatorId });
        
        //Verify that the ticket's assigned operator changed
        var ticketReassigned = await _dbHelper.WaitForStateAsync("TicketDb", $"SELECT COUNT(1) FROM Tickets WHERE Id = '{ticketId}' AND AssignedOperatorId = '{operatorId}'", "1");
        Assert.True(ticketReassigned, "Ticket's assigned operator was not changed.");

        //Verify that the ticket reassigned kafka event was published
        var kafkaTicketReassignedEvent = await _kafkaHelper.WaitForMessageAsync("TicketReassigned");
        Assert.True(kafkaTicketReassignedEvent, "TicketReassignedEvent was not received.");

        //STEP 12: Operator and user leave a message
        _apiClient.SetAuthToken(operatorToken);
        await _apiClient.PostAsync<object>($"/api/tickets/{ticketId}/messages", new { Message = "We are looking into this." });

        _apiClient.SetAuthToken(employeeToken);
        await _apiClient.PostAsync<object>($"/api/tickets/{ticketId}/messages", new { Message = "Thank you." });

        //Verify that the messages were saved to the database
        var messagesSaved = await _dbHelper.WaitForStateAsync("TicketDb", $"SELECT COUNT(1) FROM TicketMessages WHERE TicketId = '{ticketId}' AND SenderType <> 0", "2");
        Assert.True(messagesSaved, "Messages were not saved to the database.");

        //Verify that the messages added kafka event was published
        var kafkaTicketMessageAddedEvent = await _kafkaHelper.WaitForMessageAsync("TicketMessageAdded");
        Assert.True(kafkaTicketMessageAddedEvent, "TicketMessageAddedEvent was not received.");

        //STEP 13: Operator marks the ticket as closed
        _apiClient.SetAuthToken(operatorToken);
        await _apiClient.PutAsync<object>($"/api/tickets/{ticketId}", new { Status = "CLOSED" });

        //Verify that the ticket's status changed
        var ticketClosed = await _dbHelper.WaitForStateAsync("TicketDb", $"SELECT COUNT(1) FROM Tickets WHERE Id = '{ticketId}' AND Status = 'CLOSED'", "1");
        Assert.True(ticketClosed, "Ticket was not closed.");

        //Verify that the ticket status changed kafka event was published
        var kafkaTicketStatusChangedEvent = await _kafkaHelper.WaitForMessageAsync("TicketStatusChanged");
        Assert.True(kafkaTicketStatusChangedEvent, "TicketStatusChangedEvent was not received.");

        //STEP 14: Manager deletes both users
        _apiClient.SetAuthToken(managerToken);
        await _apiClient.DeleteAsync($"/api/organizations/users/{employeeId}");
        await _apiClient.DeleteAsync($"/api/organizations/users/{operatorId}");

        //Verify that both users were deleted
        var usersDeleted = await _dbHelper.WaitForStateAsync("OrgDb", $"SELECT COUNT(1) FROM Users WHERE Id IN ('{employeeId}', '{operatorId}')", "0");
        Assert.True(usersDeleted, "Users were not deleted.");

        //Verify that the user deleted kafka event was published
        var kafkaUserDeletedEvent = await _kafkaHelper.WaitForMessageAsync("UserRemovedFromOrganization");
        Assert.True(kafkaUserDeletedEvent, "UserRemovedFromOrganizationEvent was not received.");

        //Verify that all the user snapshots are no longer present in the database
        var usersNotPresent = await _dbHelper.WaitForStateAsync("TicketDb", $"SELECT COUNT(1) FROM UserSnapshots WHERE OrganizationId = '{organizationId}'", "1");
        Assert.True(usersNotPresent, "Users were not removed from the database.");

        //STEP 15: System user attempts to clean up the organization
        var systemToken = await _keycloakHelper.LoginAsync("system-orchestrator@supporthub.internal", "Pass123!");
        _apiClient.SetAuthToken(systemToken);

        //Delete the organization
        await _apiClient.DeleteAsync($"/api/organizations/{organizationId}");

        //Verify that there aren't any organization in the database
        var organizationDeleted = await _dbHelper.WaitForStateAsync("OrgDb", $"SELECT COUNT(1) FROM Organizations WHERE Id = '{organizationId}'", "0");
        Assert.True(organizationDeleted, "Organization was not deleted.");

        //Verify that the organization deleted kafka event was published
        var kafkaOrganizationDeletedEvent = await _kafkaHelper.WaitForMessageAsync("OrganizationDeleted");
        Assert.True(kafkaOrganizationDeletedEvent, "OrganizationDeletedEvent was not received.");
    }
}