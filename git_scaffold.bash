#!/bin/bash

#Exit immediately if a command exits with a non-zero status
set -e

GITHUB_ORG="https://github.com/SupportHubOrg"
REPOS_DIR="../repos"

#Declare an associative array mapping directories to their GitHub repository names
declare -A REPOSITORIES=(
    ["service-ticket-management"]="service-ticket-management.git"
    ["service-organization-access"]="service-organization-access.git"
    ["service-ai-triage"]="service-ai-triage.git"
    ["service-knowledge"]="service-knowledge.git"
    ["service-billing"]="service-billing.git"
    ["service-notification"]="service-notification.git"
    ["frontend-web"]="frontend-web.git"
    ["api-gateway"]="api-gateway.git"
)

#Navigate to the base directory where the repos are cloned/scaffolded
cd "$REPOS_DIR" || { echo "Directory $REPOS_DIR not found. Please run the scaffold script first."; exit 1; }

echo "Starting Git initialization and push for all projects..."

#Loop through the associative array
for REPO_DIR in "${!REPOSITORIES[@]}"; do
    REMOTE_REPO="${REPOSITORIES[$REPO_DIR]}"
    REMOTE_URL="${GITHUB_ORG}/${REMOTE_REPO}"

    echo "========================================"
    if [ -d "$REPO_DIR" ]; then
        echo "Processing project: $REPO_DIR"
        cd "$REPO_DIR"

        #Check if it's already a git repository to prevent double initialization
        if [ ! -d ".git" ]; then
            git init
            git branch -M main
            git remote add origin "$REMOTE_URL"
            
            git add .
            git commit -m "Initial scaffold"
            
            git pull origin main --allow-unrelated-histories -X ours --no-edit || echo "No existing remote repository found or nothing to pull."
            
            echo "Pushing to $REMOTE_URL..."
            git push -u origin main
        else
            echo "Git repository is already initialized in $REPO_DIR. Skipping."
        fi

        #Return to the base repos directory
        cd - > /dev/null
    else
        echo "Warning: Directory $REPO_DIR does not exist. Skipping."
    fi
done

echo "All repositories have been processed and pushed successfully!"