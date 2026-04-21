#!/bin/bash

#Exit immediately if a command exits with a non-zero status
set -e

echo "Starting Local Environment Setup for SupportHubOrg..."

#Verify Prerequisites
command -v docker >/dev/null 2>&1 || { echo >&2 "Docker is required but it's not installed or running. Aborting."; exit 1; }
command -v dotnet >/dev/null 2>&1 || { echo >&2 ".NET SDK is required but it's not installed. Aborting."; exit 1; }
command -v npm >/dev/null 2>&1 || { echo >&2 "npm is required but it's not installed. Aborting."; exit 1; }
command -v java >/dev/null 2>&1 || { echo >&2 "Java (JDK 17+) is required but it's not installed. Aborting."; exit 1; }

#GitHub Organization URL
ORG_URL="https://github.com/SupportHubOrg"
REPOS_DIR="../repos"

mkdir -p "$REPOS_DIR"
cd "$REPOS_DIR"

#Define repositories to clone
REPOS=(
  "service-ticket-management"
  "service-organization-access"
  "service-ai-triage"
  "service-knowledge"
  "service-billing"
  "service-notification"
  "frontend-web"
  "api-gateway"
)

#Clone Repositories
echo "Cloning repositories..."
for REPO in "${REPOS[@]}"; do
  if [ ! -d "$REPO" ]; then
    git clone "$ORG_URL/$REPO.git"
  else
    echo "$REPO already exists. Pulling latest..."
    cd "$REPO" && git pull && cd ..
  fi
done

#Build Java Services
echo "Compiling Java (Spring Boot) Services..."
if [ -d "service-ai-triage" ]; then
  cd service-ai-triage
  ./mvnw clean package -DskipTests
  mv target/ai-triage-*.jar target/ai-triage.jar 2>/dev/null || true
  cd ..
fi

if [ -d "service-knowledge" ]; then
  cd service-knowledge
  ./mvnw clean package -DskipTests
  mv target/knowledge-*.jar target/knowledge.jar 2>/dev/null || true
  cd ..
fi

#Install Node.js Dependencies
echo "Installing Node.js Dependencies..."
if [ -d "service-billing/src/Billing" ]; then
  cd service-billing/src/Billing && npm install && cd ../../../
fi

if [ -d "service-notification/src/Notification" ]; then
  cd service-notification/src/Notification && npm install && cd ../../../
fi

if [ -d "frontend-web/src/Frontend" ]; then
  cd frontend-web/src/Frontend && npm install && cd ../../../
fi

#Configure Local Secrets and Certificates
echo "Configuring Local Secrets and Dev Certificates..."
#Navigate back to the Aspire Orchestrator directory
cd ../aspire-orchestrator

#Initialize and set dummy secrets
dotnet user-secrets init || true
dotnet user-secrets set "OpenAIKey" "REPLACE_ME_OPENAI"
dotnet user-secrets set "PayUKey" "REPLACE_ME_PAYU"
dotnet user-secrets set "GoogleApiKey" "REPLACE_ME_GOOGLE"
dotnet user-secrets set "GitHubToken" "REPLACE_ME_GITHUB_PAT"

#Trust the HTTPS development certificate
dotnet dev-certs https --trust

echo "Environment setup complete! You can now start the orchestrator."
echo "Command: dotnet run"