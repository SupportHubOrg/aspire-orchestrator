#!/bin/bash

set -e

REPOS_DIR="../repos"

echo "Generating Kubernetes manifests..."

cd "$REPOS_DIR" || { echo "Directory $REPOS_DIR not found"; exit 1; }

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

for REPO in "${REPOS[@]}"; do
  echo "Scaffolding k8s manifests for $REPO..."
  mkdir -p "$REPO/k8s"
  
  TARGET_PORT=8080
  if [[ "$REPO" == *"billing"* || "$REPO" == *"notification"* ||  "$REPO" == *"frontend"* ]]; then
    TARGET_PORT=3000
  fi

  cat <<EOF > "$REPO/k8s/deployment.yaml"
apiVersion: apps/v1
kind: Deployment
metadata:
  name: $REPO
  labels:
    app: $REPO
spec:
  replicas: 2
  selector:
    matchLabels:
      app: $REPO
  template:
    metadata:
      labels:
        app: $REPO
    spec:
      containers:
      - name: $REPO
        #Replace with actual container registry URL
        image: your-registry.com/issueresolutionorg/$REPO:latest
        ports:
        - containerPort: $TARGET_PORT
        envFrom:
        - configMapRef:
            name: ${REPO}-config
EOF

  cat <<EOF > "$REPO/k8s/service.yaml"
apiVersion: v1
kind: Service
metadata:
  name: $REPO
spec:
  selector:
    app: $REPO
  ports:
    - protocol: TCP
      port: 80
      targetPort: $TARGET_PORT
EOF

  cat <<EOF > "$REPO/k8s/configmap.yaml"
apiVersion: v1
kind: ConfigMap
metadata:
  name: ${REPO}-config
data:
  #Inject standard production flags
  ASPNETCORE_ENVIRONMENT: "Production"
  NODE_ENV: "production"
  #Service-specific environment variables will be added here
EOF

done

echo "Kubernetes manifests successfully generated in all repositories."