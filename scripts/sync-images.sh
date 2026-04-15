#!/usr/bin/env bash
# sync-images.sh — Mirror or build every image entry in catalog.yaml.
# Usage: sync-images.sh <catalog.yaml>
#
# Outputs SYNC_IMAGE_RESULTS (JSON array) to a temp file referenced by
# SYNC_IMAGE_RESULTS_FILE, which generate-report.sh consumes.
#
# Set SCAN_FAIL_ON_CRITICAL=true to block images with CRITICAL CVEs from
# being copied to the registry. Blocked images are reported at the end and
# the workflow exits non-zero, but all non-blocked images are still synced.

set -euo pipefail

CATALOG="${1:?Usage: sync-images.sh <catalog.yaml>}"
REPORT="docs/sync-report.json"
RESULTS_FILE=$(mktemp --suffix=.json)
echo "[]" > "${RESULTS_FILE}"
export SYNC_IMAGE_RESULTS_FILE="${RESULTS_FILE}"

BLOCKED_IMAGES=()

# scan_image REF
# Runs Trivy against the given image reference and sets:
#   SCAN_CRITICAL, SCAN_HIGH, SCAN_MEDIUM, SCAN_LOW, SCAN_BLOCKED, SCAN_TIME
scan_image() {
  local ref="$1"
  local trivy_out
  trivy_out=$(mktemp --suffix=.json)
  trivy image --quiet --format json --output "${trivy_out}" \
    --timeout 10m "${ref}" 2>/dev/null || true

  SCAN_CRITICAL=$(jq '[.Results[]?.Vulnerabilities[]? | select(.Severity=="CRITICAL")] | length' "${trivy_out}" 2>/dev/null || echo 0)
  SCAN_HIGH=$(jq     '[.Results[]?.Vulnerabilities[]? | select(.Severity=="HIGH")]     | length' "${trivy_out}" 2>/dev/null || echo 0)
  SCAN_MEDIUM=$(jq   '[.Results[]?.Vulnerabilities[]? | select(.Severity=="MEDIUM")]   | length' "${trivy_out}" 2>/dev/null || echo 0)
  SCAN_LOW=$(jq      '[.Results[]?.Vulnerabilities[]? | select(.Severity=="LOW")]      | length' "${trivy_out}" 2>/dev/null || echo 0)
  SCAN_CRITICAL=${SCAN_CRITICAL:-0}
  SCAN_HIGH=${SCAN_HIGH:-0}
  SCAN_MEDIUM=${SCAN_MEDIUM:-0}
  SCAN_LOW=${SCAN_LOW:-0}
  SCAN_TIME=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  rm -f "${trivy_out}"

  echo "  scan: CRITICAL=${SCAN_CRITICAL} HIGH=${SCAN_HIGH} MEDIUM=${SCAN_MEDIUM} LOW=${SCAN_LOW}"

  SCAN_BLOCKED=false
  if [[ "${SCAN_FAIL_ON_CRITICAL:-false}" == "true" && "${SCAN_CRITICAL}" -gt 0 ]]; then
    SCAN_BLOCKED=true
    echo "  BLOCKED: ${SCAN_CRITICAL} critical CVEs — skipping copy"
  fi
}

COUNT=$(yq '.images | length' "${CATALOG}")

for i in $(seq 0 $((COUNT - 1))); do
  SOURCE=$(yq ".images[$i].source"     "${CATALOG}")
  TAG=$(yq    ".images[$i].tag"         "${CATALOG}")
  TARGET=$(yq ".images[$i].target"      "${CATALOG}")
  MODE=$(yq   ".images[$i].mode"        "${CATALOG}")

  STATUS="success"
  CONTEXT_HASH=""
  SYNCED_AT=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
  SCAN_CRITICAL=0; SCAN_HIGH=0; SCAN_MEDIUM=0; SCAN_LOW=0
  SCAN_BLOCKED=false; SCAN_TIME=""

  echo "--- [image $((i+1))/${COUNT}] mode=${MODE} source=${SOURCE}:${TAG} target=${TARGET}:${TAG}"

  if [[ "${MODE}" == "mirror" ]]; then
    scan_image "${SOURCE}:${TAG}"
    if [[ "${SCAN_BLOCKED}" == "true" ]]; then
      BLOCKED_IMAGES+=("${SOURCE}:${TAG}")
      STATUS="scan_blocked"
    else
      crane copy "${SOURCE}:${TAG}" "${TARGET}:${TAG}" || STATUS="failure"
    fi

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
        scan_image "${TARGET}:${TAG}"
        if [[ "${SCAN_BLOCKED}" == "true" ]]; then
          BLOCKED_IMAGES+=("${TARGET}:${TAG}")
          STATUS="scan_blocked"
        else
          docker push "${TARGET}:${TAG}" || STATUS="failure"
        fi
      fi
    fi

  else
    echo "  ERROR: unknown mode '${MODE}'" >&2
    STATUS="failure"
  fi

  echo "  status=${STATUS}"

  # Append result entry
  ENTRY=$(jq -n \
    --arg source            "${SOURCE}" \
    --arg target            "${TARGET}" \
    --arg tag               "${TAG}" \
    --arg mode              "${MODE}" \
    --arg status            "${STATUS}" \
    --arg context_hash      "${CONTEXT_HASH}" \
    --arg synced_at         "${SYNCED_AT}" \
    --argjson scan_critical "${SCAN_CRITICAL}" \
    --argjson scan_high     "${SCAN_HIGH}" \
    --argjson scan_medium   "${SCAN_MEDIUM}" \
    --argjson scan_low      "${SCAN_LOW}" \
    --argjson scan_blocked  "${SCAN_BLOCKED}" \
    --arg scan_time         "${SCAN_TIME}" \
    '{source: $source, target: $target, tag: $tag, mode: $mode,
      status: $status, context_hash: $context_hash, synced_at: $synced_at,
      scan_critical: $scan_critical, scan_high: $scan_high,
      scan_medium: $scan_medium, scan_low: $scan_low,
      scan_blocked: $scan_blocked, scan_time: $scan_time}')

  jq ". + [\$entry]" --argjson entry "${ENTRY}" "${RESULTS_FILE}" \
    > "${RESULTS_FILE}.tmp" && mv "${RESULTS_FILE}.tmp" "${RESULTS_FILE}"

  # Propagate hard failures immediately (scan_blocked images are tallied at the end)
  if [[ "${STATUS}" == "failure" ]]; then
    echo "ERROR: sync failed for ${SOURCE}:${TAG} -> ${TARGET}:${TAG}" >&2
    exit 1
  fi
done

echo "Image sync complete. Results written to ${RESULTS_FILE}"

# Propagate the results file path to subsequent GitHub Actions steps
if [[ -n "${GITHUB_ENV:-}" ]]; then
  echo "SYNC_IMAGE_RESULTS_FILE=${RESULTS_FILE}" >> "${GITHUB_ENV}"
fi

# Fail at the end so all images are processed first
if [[ "${#BLOCKED_IMAGES[@]}" -gt 0 ]]; then
  echo "ERROR: ${#BLOCKED_IMAGES[@]} image(s) blocked due to CRITICAL CVEs:" >&2
  printf '  - %s\n' "${BLOCKED_IMAGES[@]}" >&2
  exit 1
fi
