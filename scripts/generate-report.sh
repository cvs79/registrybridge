#!/usr/bin/env bash
# generate-report.sh — Merge sync results into docs/sync-report.json.
# Called after sync-images.sh and sync-charts.sh.
# Reads SYNC_IMAGE_RESULTS_FILE and SYNC_CHART_RESULTS_FILE env vars
# set by the sibling scripts; falls back to empty arrays if not set.

set -euo pipefail

REPORT="docs/sync-report.json"
NOW=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

# Read image and chart result arrays from temp files written by sync scripts
IMAGE_RESULTS="[]"
if [[ -n "${SYNC_IMAGE_RESULTS_FILE:-}" && -f "${SYNC_IMAGE_RESULTS_FILE}" ]]; then
  IMAGE_RESULTS=$(cat "${SYNC_IMAGE_RESULTS_FILE}")
fi

CHART_RESULTS="[]"
if [[ -n "${SYNC_CHART_RESULTS_FILE:-}" && -f "${SYNC_CHART_RESULTS_FILE}" ]]; then
  CHART_RESULTS=$(cat "${SYNC_CHART_RESULTS_FILE}")
fi

# Load existing report or start from scratch
if [[ -f "${REPORT}" ]]; then
  CURRENT=$(cat "${REPORT}")
else
  CURRENT='{"last_sync":null,"images":[],"charts":[]}'
fi

# Upsert each image entry (keyed on source + target + tag)
UPDATED=$(echo "${CURRENT}" | jq \
  --argjson new_images "${IMAGE_RESULTS}" \
  --arg last_sync "${NOW}" \
  '
  .last_sync = $last_sync |
  reduce $new_images[] as $entry (
    .;
    .images = (
      [.images[]? | select(
        .source != $entry.source or
        .target != $entry.target or
        .tag    != $entry.tag
      )] + [$entry]
    )
  )
  ')

# Upsert each chart entry (keyed on chart + version + target)
UPDATED=$(echo "${UPDATED}" | jq \
  --argjson new_charts "${CHART_RESULTS}" \
  '
  reduce $new_charts[] as $entry (
    .;
    .charts = (
      [.charts[]? | select(
        .chart   != $entry.chart or
        .version != $entry.version or
        .target  != $entry.target
      )] + [$entry]
    )
  )
  ')

# Atomic write
TMP=$(mktemp)
echo "${UPDATED}" | jq '.' > "${TMP}"
mv "${TMP}" "${REPORT}"

echo "sync-report.json updated (last_sync=${NOW})"
