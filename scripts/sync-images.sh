#!/usr/bin/env bash
# sync-images.sh — Mirror or build every image entry in catalog.yaml.
# Usage: sync-images.sh <catalog.yaml>
#
# Outputs SYNC_IMAGE_RESULTS (JSON array) to a temp file referenced by
# SYNC_IMAGE_RESULTS_FILE, which generate-report.sh consumes.

set -euo pipefail

CATALOG="${1:?Usage: sync-images.sh <catalog.yaml>}"
REPORT="docs/sync-report.json"
RESULTS_FILE=$(mktemp --suffix=.json)
echo "[]" > "${RESULTS_FILE}"
export SYNC_IMAGE_RESULTS_FILE="${RESULTS_FILE}"

COUNT=$(yq '.images | length' "${CATALOG}")

for i in $(seq 0 $((COUNT - 1))); do
  SOURCE=$(yq ".images[$i].source"     "${CATALOG}")
  TAG=$(yq    ".images[$i].tag"         "${CATALOG}")
  TARGET=$(yq ".images[$i].target"      "${CATALOG}")
  MODE=$(yq   ".images[$i].mode"        "${CATALOG}")

  STATUS="success"
  CONTEXT_HASH=""
  SYNCED_AT=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

  echo "--- [image $((i+1))/${COUNT}] mode=${MODE} source=${SOURCE}:${TAG} target=${TARGET}:${TAG}"

  if [[ "${MODE}" == "mirror" ]]; then
    crane copy "${SOURCE}:${TAG}" "${TARGET}:${TAG}" || STATUS="failure"

  elif [[ "${MODE}" == "build" ]]; then
    CONTEXT=$(yq ".images[$i].context"     "${CATALOG}")
    DOCKERFILE=$(yq ".images[$i].dockerfile" "${CATALOG}")

    # Compute deterministic hash of the entire build context
    CONTEXT_HASH=$(find "${CONTEXT}" -type f | sort | xargs sha256sum | sha256sum | awk '{print $1}')
    echo "  context_hash=${CONTEXT_HASH}"

    # Check if a previous build with the same tag AND context_hash already exists
    SKIP=false
    if [[ -f "${REPORT}" ]]; then
      EXISTING_HASH=$(jq -r \
        --arg target "${TARGET}" \
        --arg tag    "${TAG}" \
        '.images[] | select(.target == $target and .tag == $tag) | .context_hash // empty' \
        "${REPORT}" 2>/dev/null || true)
      if [[ "${EXISTING_HASH}" == "${CONTEXT_HASH}" ]]; then
        echo "  Skipping build — tag and context hash unchanged."
        SKIP=true
      fi
    fi

    if [[ "${SKIP}" == "false" ]]; then
      docker build \
        --build-arg "VERSION=${TAG}" \
        -f "${CONTEXT}/${DOCKERFILE}" \
        -t "${TARGET}:${TAG}" \
        "${CONTEXT}" || STATUS="failure"

      if [[ "${STATUS}" == "success" ]]; then
        docker push "${TARGET}:${TAG}" || STATUS="failure"
      fi
    fi

  else
    echo "  ERROR: unknown mode '${MODE}'" >&2
    STATUS="failure"
  fi

  echo "  status=${STATUS}"

  # Append result entry
  ENTRY=$(jq -n \
    --arg source       "${SOURCE}" \
    --arg target       "${TARGET}" \
    --arg tag          "${TAG}" \
    --arg mode         "${MODE}" \
    --arg status       "${STATUS}" \
    --arg context_hash "${CONTEXT_HASH}" \
    --arg synced_at    "${SYNCED_AT}" \
    '{source: $source, target: $target, tag: $tag, mode: $mode,
      status: $status, context_hash: $context_hash, synced_at: $synced_at}')

  jq ". + [\$entry]" --argjson entry "${ENTRY}" "${RESULTS_FILE}" \
    > "${RESULTS_FILE}.tmp" && mv "${RESULTS_FILE}.tmp" "${RESULTS_FILE}"

  # Propagate failures
  if [[ "${STATUS}" == "failure" ]]; then
    echo "ERROR: sync failed for ${SOURCE}:${TAG} -> ${TARGET}:${TAG}" >&2
    exit 1
  fi
done

echo "Image sync complete. Results written to ${RESULTS_FILE}"
