#!/usr/bin/env bash
set -euo pipefail

IMAGE_NAME="telebot"
IMAGE_TAG="latest"
NAMESPACE="prandyhub"

FULL_IMAGE_NAME="$NAMESPACE/$IMAGE_NAME:$IMAGE_TAG"

echo "▶ Building image: $FULL_IMAGE_NAME"
docker build -t "$FULL_IMAGE_NAME" .

echo "▶ Pushing image"
docker push "$FULL_IMAGE_NAME"

echo "✅ Done"