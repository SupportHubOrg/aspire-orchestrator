using Dutchskull.Aspire.PolyRepo;

var builder = DistributedApplication.CreateBuilder(args);

//MSSQL for Ticket Management and Organization Access
var sqlServer = builder.AddSqlServer("sql-server");
var ticketDb = sqlServer.AddDatabase("TicketDb");
var orgDb = sqlServer.AddDatabase("OrgDb");

//PostgreSQL with pgvector for AI Triage with a pgvector image override
var postgres = builder.AddPostgres("postgres")
    .WithImage("pgvector/pgvector", "pg16");
var vectorDb = postgres.AddDatabase("VectorDb");

//PostgreSQL for Notification Service
var notificationDb = postgres.AddDatabase("NotificationDb");

//Define static parameters for username and password to override Keycloak's random generation
var keycloakAdminUser = builder.AddParameter("keycloak-username", "admin");
var keycloakAdminPass = builder.AddParameter("keycloak-password", "admin", secret: true);

//Keycloak for identity and frontend application login
var keycloak = builder.AddKeycloak("keycloak", 8080, keycloakAdminUser, keycloakAdminPass)
    .WithImage("keycloak/keycloak", "26.6.1")
    .WithEnvironment("KC_DB", "dev-file")
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    .WithEnvironment("KC_METRICS_ENABLED", "true")
    .WithBindMount("../../repos/auth-keycloak/import", "/opt/keycloak/data/import");

//Kafka for backend service communication
var kafka = builder.AddKafka("kafka")
    .WithEnvironment("KAFKA_AUTO_CREATE_TOPICS_ENABLE", "true");

//Polyrepo definitions
var githubToken = builder.Configuration["GitHubToken"];
var orgUrl = string.IsNullOrEmpty(githubToken) 
    ? "https://github.com/SupportHubOrg" 
    : $"https://{githubToken}@github.com/SupportHubOrg";
var appHostDirectory = builder.Environment.ContentRootPath;
var clonePath = Path.GetFullPath(Path.Combine(appHostDirectory, "../../repos"));

var ticketRepo = builder.AddRepository("ticket-repo", $"{orgUrl}/service-ticket-management.git", c => c.WithTargetPath($"{clonePath}"));
var orgRepo = builder.AddRepository("organization-repo", $"{orgUrl}/service-organization-access.git", c => c.WithTargetPath($"{clonePath}"));
var aiTriageRepo = builder.AddRepository("ai-repo", $"{orgUrl}/service-ai-triage.git", c => c.WithTargetPath($"{clonePath}"));
var knowledgeRepo = builder.AddRepository("knowledge-repo", $"{orgUrl}/service-knowledge.git", c => c.WithTargetPath($"{clonePath}"));
var billingRepo = builder.AddRepository("billing-repo", $"{orgUrl}/service-billing.git", c => c.WithTargetPath($"{clonePath}"));
var notificationRepo = builder.AddRepository("notification-repo", $"{orgUrl}/service-notification.git", c => c.WithTargetPath($"{clonePath}"));
var frontendRepo = builder.AddRepository("frontend-repo", $"{orgUrl}/frontend-web.git", c => c.WithTargetPath($"{clonePath}"));
var gatewayRepo = builder.AddRepository("gateway-repo", $"{orgUrl}/api-gateway.git", c => c.WithTargetPath($"{clonePath}"));

//Get the Keycloak endpoint to include in the API Gateway configuration
var keycloakEndpoint = keycloak.GetEndpoint("http");

//.NET Services (MSSQL)
var ticketManagementService = builder.AddProjectFromRepository("ticket-management", ticketRepo, "src/TicketManagement/TicketManagement.csproj")
    .WithReference(ticketDb)
    .WaitFor(ticketDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("Authentication__Authority", $"{keycloakEndpoint}/realms/supporthub")
    .WithEnvironment("Authentication__Audience", "ticket-management-service");

var orgAccessService = builder.AddProjectFromRepository("organization-access", orgRepo, "src/OrganizationAccess/OrganizationAccess.csproj")
    .WithReference(orgDb)
    .WaitFor(orgDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("Authentication__Authority", $"{keycloakEndpoint}/realms/supporthub")
    .WithEnvironment("Authentication__Audience", "organization-access-service");

//Java/Spring Boot Services (PostgreSQL + pgvector)
var aiTriageService = builder.AddExecutable("ai-triage", "java", workingDirectory: $"{clonePath}/service-ai-triage", "-jar", "target/ai-triage.jar")
    .WithHttpEndpoint(env: "SERVER_PORT")
    .WithReference(vectorDb)
    .WaitFor(vectorDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("KEYCLOAK_AUTH_URL", $"{keycloakEndpoint}/realms/supporthub")
    .WithEnvironment("KEYCLOAK_CLIENT_ID", "ai-triage-service")
    .WithEnvironment("OPENAI_API_KEY", builder.Configuration["OpenAIKey"]);

var knowledgeService = builder.AddExecutable("knowledge-base", "java", workingDirectory: $"{clonePath}/service-knowledge", "-jar", "target/knowledge.jar")
    .WithHttpEndpoint(env: "SERVER_PORT")
    .WithReference(vectorDb)
    .WaitFor(vectorDb)
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("KEYCLOAK_AUTH_URL", $"{keycloakEndpoint}/realms/supporthub")
    .WithEnvironment("KEYCLOAK_CLIENT_ID", "ai-triage-service");

//TypeScript / Node.js Services
var billingService = builder.AddNpmAppFromRepository("billing-service", billingRepo, ".", scriptName: "dev")
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithEnvironment("PAYU_API_KEY", builder.Configuration["PayUKey"]);

//Get the Kafka endpoint to include in the Notification Service configuration
var kafkaEndpoint = kafka.GetEndpoint("tcp");

var notificationService = builder.AddNpmAppFromRepository("notification-service", notificationRepo, ".", scriptName: "dev")
    .WithReference(kafka)
    .WaitFor(kafka)
    .WithEnvironment("DATABASE_URL", notificationDb)
    .WithEnvironment("GOOGLE_API_KEY", builder.Configuration["GoogleApiKey"])
    .WithEnvironment("NODEMAILER_USER", builder.Configuration["SmtpConfig:User"])
    .WithEnvironment("NODEMAILER_PASS", builder.Configuration["SmtpConfig:Pass"]);

//YARP API Gateway
var apiGateway = builder.AddProjectFromRepository("api-gateway", gatewayRepo, "src/ApiGateway/ApiGateway.csproj")
    .WithReference(ticketManagementService)
    .WithReference(orgAccessService)
    .WithReference(aiTriageService.GetEndpoint("http"))
    .WithReference(knowledgeService.GetEndpoint("http"))
    .WithReference(billingService)
    .WithReference(notificationService)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("Authentication__Authority", $"{keycloakEndpoint}/realms/supporthub")
    .WithEnvironment("Authentication__Audience", "api-gateway")
    .WithHttpEndpoint();

//React Frontend
var frontendApp = builder.AddNpmAppFromRepository("frontend-web", frontendRepo, ".", scriptName: "dev")
    .WithReference(apiGateway)
    .WaitFor(apiGateway)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("REACT_APP_KEYCLOAK_URL", $"http://localhost:8080")
    .WithEnvironment("REACT_APP_KEYCLOAK_REALM", "supporthub")
    .WithEnvironment("REACT_APP_KEYCLOAK_CLIENT_ID", "supporthub-frontend")
    .WithHttpEndpoint(port: 3000, env: "PORT");

builder.Build().Run();