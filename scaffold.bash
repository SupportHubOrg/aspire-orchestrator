#!/bin/bash

set -e

REPOS_DIR="../repos"

echo "Starting poly-repo scaffolding in $REPOS_DIR..."

mkdir -p "$REPOS_DIR"
cd "$REPOS_DIR"

echo "Scaffolding .NET Services..."

mkdir -p service-ticket-management/src/TicketManagement
cd service-ticket-management/src/TicketManagement
dotnet new webapi -n TicketManagement -o . --force
cd ../../../

mkdir -p service-organization-access/src/OrganizationAccess
cd service-organization-access/src/OrganizationAccess
dotnet new webapi -n OrganizationAccess -o . --force
cd ../../../

echo "Scaffolding Spring Boot Services..."

mkdir -p service-ai-triage
cd service-ai-triage
curl -G https://start.spring.io/starter.zip \
  -d dependencies=web \
  -d name=ai-triage \
  -d artifactId=ai-triage \
  -d type=maven-project \
  -o ai-triage.zip
unzip -q ai-triage.zip
rm ai-triage.zip
./mvnw clean package -DskipTests
mv target/ai-triage-*.jar target/ai-triage.jar
cd ..

mkdir -p service-knowledge
cd service-knowledge
curl -G https://start.spring.io/starter.zip \
  -d dependencies=web \
  -d name=knowledge \
  -d artifactId=knowledge \
  -d type=maven-project \
  -o knowledge.zip
unzip -q knowledge.zip
rm knowledge.zip
./mvnw clean package -DskipTests
mv target/knowledge-*.jar target/knowledge.jar
cd ..


echo "Scaffolding Node.js Services..."

mkdir -p service-billing/src/Billing
cat << 'EOF' > service-billing/src/Billing/package.json
{
  "name": "billing-service",
  "version": "1.0.0",
  "scripts": {
    "dev": "node index.js"
  }
}
EOF
echo "console.log('Billing Service Running on port 3000'); setInterval(() => {}, 1000);" > service-billing/src/Billing/index.js

mkdir -p service-notification/src/Notification
cat << 'EOF' > service-notification/src/Notification/package.json
{
  "name": "notification-service",
  "version": "1.0.0",
  "scripts": {
    "dev": "node index.js"
  }
}
EOF
echo "console.log('Notification Service Running'); setInterval(() => {}, 1000);" > service-notification/src/Notification/index.js

echo "Scaffolding React Frontend..."

mkdir -p frontend-web/src/Frontend
cat << 'EOF' > frontend-web/src/Frontend/package.json
{
  "name": "frontend-web",
  "version": "1.0.0",
  "scripts": {
    "start": "node server.js"
  }
}
EOF
echo "console.log('Dummy React Server Running on port 3000'); setInterval(() => {}, 1000);" > frontend-web/src/Frontend/server.js

echo "Scaffolding API Gateway (YARP)..."

mkdir -p api-gateway/src/ApiGateway
cd api-gateway/src/ApiGateway

dotnet new web -n ApiGateway -o . --force

dotnet add package Yarp.ReverseProxy
dotnet add package Microsoft.Extensions.ServiceDiscovery.Yarp

cat << 'EOF' > Program.cs
var builder = WebApplication.CreateBuilder(args);

//Register Service Discovery and YARP
builder.Services.AddServiceDiscovery();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapGet("/", () => "YARP API Gateway is running");
app.MapReverseProxy();

app.Run();
EOF

cat << 'EOF' > appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ReverseProxy": {
    "Routes": {
      "ticket-management-route": {
        "ClusterId": "ticket-management-cluster",
        "Match": {
          "Path": "/api/tickets/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "{**catch-all}"
          }
        ]
      },
      "organization-access-route": {
        "ClusterId": "organization-access-cluster",
        "Match": {
          "Path": "/api/organizations/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "{**catch-all}"
          }
        ]
      },
      "ai-triage-route": {
        "ClusterId": "ai-triage-cluster",
        "Match": {
          "Path": "/api/ai-triage/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "{**catch-all}"
          }
        ]
      },
      "knowledge-base-route": {
        "ClusterId": "knowledge-base-cluster",
        "Match": {
          "Path": "/api/knowledge/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "{**catch-all}"
          }
        ]
      },
      "billing-service-route": {
        "ClusterId": "billing-service-cluster",
        "Match": {
          "Path": "/api/billing/{**catch-all}"
        },
        "Transforms": [
          {
            "PathPattern": "{**catch-all}"
          }
        ]
      }
    },
    "Clusters": {
      "ticket-management-cluster": {
        "Destinations": {
          "ticket-management-dest": {
            "Address": "http://ticket-management"
          }
        }
      },
      "organization-access-cluster": {
        "Destinations": {
          "organization-access-dest": {
            "Address": "http://organization-access"
          }
        }
      },
      "ai-triage-cluster": {
        "Destinations": {
          "ai-triage-dest": {
            "Address": "http://ai-triage"
          }
        }
      },
      "knowledge-base-cluster": {
        "Destinations": {
          "knowledge-base-dest": {
            "Address": "http://knowledge-base"
          }
        }
      },
      "billing-service-cluster": {
        "Destinations": {
          "billing-service-dest": {
            "Address": "http://billing-service"
          }
        }
      }
    }
  }
}
EOF

echo "Scaffolding complete!"