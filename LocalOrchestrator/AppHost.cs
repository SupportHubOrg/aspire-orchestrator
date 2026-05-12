using Dutchskull.Aspire.PolyRepo;

var builder = DistributedApplication.CreateBuilder(args);

//Infrastructure Dependencies and Services

//Keycloak for identity and frontend application login
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithEnvironment("KEYCLOAK_ADMIN", "admin")
    .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", "admin");

//MSSQL for Ticket Management and Organization Access
var sqlServer = builder.AddSqlServer("sql-server");
var ticketDb = sqlServer.AddDatabase("TicketDb");
var orgDb = sqlServer.AddDatabase("OrgDb");

//PostgreSQL with pgvector for AI Triage with a pgvector image override
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg16");
var vectorDb = postgres.AddDatabase("VectorDb");

//Kafka for backend service communication
var kafka = builder.AddKafka("kafka");

//Polyrepo definitions
var githubToken = builder.Configuration["GitHubToken"];
var orgUrl = string.IsNullOrEmpty(githubToken) 
    ? "https://github.com/SupportHubOrg" 
    : $"https://{githubToken}@github.com/SupportHubOrg";
var clonePath = "../../repos";

var ticketRepo = builder.AddRepository("ticket-repo", $"{orgUrl}/service-ticket-management.git", c => c.WithTargetPath($"{clonePath}"));
var orgRepo = builder.AddRepository("organization-repo", $"{orgUrl}/service-organization-access.git", c => c.WithTargetPath($"{clonePath}"));
var aiTriageRepo = builder.AddRepository("ai-repo", $"{orgUrl}/service-ai-triage.git", c => c.WithTargetPath($"{clonePath}"));
var knowledgeRepo = builder.AddRepository("knowledge-repo", $"{orgUrl}/service-knowledge.git", c => c.WithTargetPath($"{clonePath}"));
var billingRepo = builder.AddRepository("billing-repo", $"{orgUrl}/service-billing.git", c => c.WithTargetPath($"{clonePath}"));
var notificationRepo = builder.AddRepository("notification-repo", $"{orgUrl}/service-notification.git", c => c.WithTargetPath($"{clonePath}"));
var frontendRepo = builder.AddRepository("frontend-repo", $"{orgUrl}/frontend-web.git", c => c.WithTargetPath($"{clonePath}"));
var gatewayRepo = builder.AddRepository("gateway-repo", $"{orgUrl}/api-gateway.git", c => c.WithTargetPath($"{clonePath}"));

//.NET Services (MSSQL)
var ticketManagementService = builder.AddProjectFromRepository("ticket-management", ticketRepo, "src/TicketManagement/TicketManagement.csproj")
    .WithReference(ticketDb)
    .WaitFor(ticketDb)
    .WithReference(kafka)
    .WaitFor(kafka);

var orgAccessService = builder.AddProjectFromRepository("organization-access", orgRepo, "src/OrganizationAccess/OrganizationAccess.csproj")
    .WithReference(orgDb)
    .WaitFor(orgDb)
    .WithReference(kafka)
    .WaitFor(kafka);

//Java/Spring Boot Services (PostgreSQL + pgvector)
var aiTriageService = builder.AddExecutable("ai-triage", "java", workingDirectory: $"{clonePath}/service-ai-triage", "-jar", "target/ai-triage.jar")
    .WithHttpEndpoint(env: "SERVER_PORT")
    .WithReference(vectorDb)
    .WaitFor(vectorDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithEnvironment("OPENAI_API_KEY", builder.Configuration["OpenAIKey"]);

var knowledgeService = builder.AddExecutable("knowledge-base", "java", workingDirectory: $"{clonePath}/service-knowledge", "-jar", "target/knowledge.jar")
    .WithHttpEndpoint(env: "SERVER_PORT")
    .WithReference(vectorDb)
    .WaitFor(vectorDb)
    .WithReference(kafka)
    .WaitFor(kafka);

//TypeScript / Node.js Services
var billingService = builder.AddNpmAppFromRepository("billing-service", billingRepo, "src/Billing", scriptName: "dev")
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithEnvironment("PAYU_API_KEY", builder.Configuration["PayUKey"]);

var notificationService = builder.AddNpmAppFromRepository("notification-service", notificationRepo, "src/Notification", scriptName: "dev")
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithEnvironment("GOOGLE_API_KEY", builder.Configuration["GoogleApiKey"]);

//YARP API Gateway
var apiGateway = builder.AddProjectFromRepository("api-gateway", gatewayRepo, "src/ApiGateway/ApiGateway.csproj")
    .WithReference(ticketManagementService)
    .WithReference(orgAccessService)
    .WithReference(aiTriageService.GetEndpoint("http"))
    .WithReference(knowledgeService.GetEndpoint("http"))
    .WithReference(billingService);

//React Frontend
var frontendApp = builder.AddNpmAppFromRepository("frontend-web", frontendRepo, "src/Frontend", scriptName: "start")
    .WithReference(apiGateway.GetEndpoint("http"))
    .WaitFor(apiGateway)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithHttpEndpoint(port: 3000, env: "PORT");

builder.Build().Run();