#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 3 ]; then
  echo "❌ Usage: $0 <ssh_host> <ssh_user> <ssh_password>"
  exit 1
fi

SSH_HOST="$1"
SSH_USER="$2"
SSH_PASS="$3"
SSH_WORKDIR="/root"

IMAGE_NAME="telebot"
IMAGE_TAG="latest"
NAMESPACE="prandyhub"
FULL_IMAGE_NAME="$NAMESPACE/$IMAGE_NAME:$IMAGE_TAG"

echo "▶ Building image: $FULL_IMAGE_NAME"
docker build -t "$FULL_IMAGE_NAME" .

echo "▶ Pushing image"
docker push "$FULL_IMAGE_NAME"

sshpass -p "$SSH_PASS" ssh -o StrictHostKeyChecking=no "$SSH_USER@$SSH_HOST" <<EOF
set -e

cd "$SSH_WORKDIR"

echo "▶ docker compose stop telebot"
docker compose stop telebot

echo "▶ docker pull $FULL_IMAGE_NAME"
docker pull $FULL_IMAGE_NAME

echo "▶ docker compose up telebot -d"
docker compose up telebot -d

EOF

echo "✅ Done"