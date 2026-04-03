#!/usr/bin/env bash
# sync-charts.sh — Pull Helm charts from public repos and push to ACR via OCI.
# Usage: sync-charts.sh <catalog.yaml>
#
# Outputs SYNC_CHART_RESULTS_FILE path (JSON array), consumed by generate-report.sh.

set -euo pipefail

CATALOG="${1:?Usage: sync-charts.sh <catalog.yaml>}"
TMPDIR=$(mktemp -d)
trap 'rm -rf "${TMPDIR}"' EXIT

RESULTS_FILE=$(mktemp --suffix=.json)
echo "[]" > "${RESULTS_FILE}"
export SYNC_CHART_RESULTS_FILE="${RESULTS_FILE}"

COUNT=$(yq '.charts | length' "${CATALOG}")

for i in $(seq 0 $((COUNT - 1))); do
  REPO=$(yq    ".charts[$i].repo"    "${CATALOG}")
  CHART=$(yq   ".charts[$i].chart"   "${CATALOG}")
  VERSION=$(yq ".charts[$i].version" "${CATALOG}")
  TARGET=$(yq  ".charts[$i].target"  "${CATALOG}")

  # The catalog target includes the chart name, e.g. myacr.azurecr.io/helm/grafana
  # helm push expects the registry root without the chart name:
  #   oci://myacr.azurecr.io/helm
  OCI_REGISTRY="oci://$(echo "${TARGET}" | sed 's|/[^/]*$||')"

  STATUS="success"
  SYNCED_AT=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

  echo "--- [chart $((i+1))/${COUNT}] chart=${CHART} version=${VERSION} -> ${OCI_REGISTRY}"

  helm pull "${CHART}" \
    --repo "${REPO}" \
    --version "${VERSION}" \
    --destination "${TMPDIR}" \
    --untar=false || STATUS="failure"

  if [[ "${STATUS}" == "success" ]]; then
    TGZ="${TMPDIR}/${CHART}-${VERSION}.tgz"
    if [[ ! -f "${TGZ}" ]]; then
      echo "ERROR: expected ${TGZ} not found after helm pull" >&2
      STATUS="failure"
    else
      helm push "${TGZ}" "${OCI_REGISTRY}" || STATUS="failure"
    fi
  fi

  echo "  status=${STATUS}"

  ENTRY=$(jq -n \
    --arg chart     "${CHART}" \
    --arg version   "${VERSION}" \
    --arg target    "${TARGET}" \
    --arg status    "${STATUS}" \
    --arg synced_at "${SYNCED_AT}" \
    '{chart: $chart, version: $version, target: $target, status: $status, synced_at: $synced_at}')

  jq ". + [\$entry]" --argjson entry "${ENTRY}" "${RESULTS_FILE}" \
    > "${RESULTS_FILE}.tmp" && mv "${RESULTS_FILE}.tmp" "${RESULTS_FILE}"

  if [[ "${STATUS}" == "failure" ]]; then
    echo "ERROR: chart sync failed for ${CHART}@${VERSION}" >&2
    exit 1
  fi
done

echo "Chart sync complete. Results written to ${RESULTS_FILE}"
