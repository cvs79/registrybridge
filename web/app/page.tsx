"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";

type CatalogRevision = {
  id: string;
  createdAt: string;
};

type CatalogEntry = {
  id: string;
  sourceReference: string;
  targetRepository: string;
  targetTag: string;
  credentialHandle: string | null;
  enabled: boolean;
  order: number;
  kind: CatalogEntryKind;
  sourceVersion: string | null;
  expectedDigest: string | null;
};

type CatalogEntryKind = "ImageMirror" | "WrapperBuild" | "HelmChart";

type VulnerabilityException = {
  id: string;
  imageDigest: string;
  vulnerabilityIds: string[];
  reason: string;
};

type Catalog = {
  currentRevision: CatalogRevision | null;
  entries: CatalogEntry[];
  vulnerabilityExceptions: VulnerabilityException[];
};

type CatalogRevisionSnapshot = {
  revision: CatalogRevision;
  entries: CatalogEntry[];
  vulnerabilityExceptions: VulnerabilityException[];
};

type CredentialHandle = {
  name: string;
  type: string;
};

type DeploymentConfiguration = {
  targetRegistry: string;
  credentialHandles: CredentialHandle[];
};

type SynchronizationRun = {
  id: string;
  catalogRevisionId: string | null;
  origin: string;
  status: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  logsTruncated: boolean;
};

type ArtifactOutcome = {
  catalogEntryId: string;
  order: number;
  disposition: string;
  detail: string | null;
};

type RunLog = {
  occurredAt: string;
  eventType: string;
  data: string;
};

type SynchronizationRunDetail = SynchronizationRun & {
  outcomes: ArtifactOutcome[];
  logs: RunLog[];
};

type EntryDraft = Omit<CatalogEntry, "id" | "order">;

const emptyDraft: EntryDraft = {
  sourceReference: "",
  targetRepository: "",
  targetTag: "",
  credentialHandle: null,
  enabled: true,
  kind: "ImageMirror",
  sourceVersion: null,
  expectedDigest: null,
};

const emptyExceptionDraft = {
  imageDigest: "",
  vulnerabilityIds: "",
  reason: "",
};

const glassCard =
  "rounded-[22px] border border-white/68 bg-gradient-to-br from-white/58 to-white/38 p-5 shadow-[0_22px_44px_-26px_rgba(40,52,96,.45)] backdrop-blur-xl backdrop-saturate-150";

class ApiError extends Error {}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  if (!response.ok) {
    const body = await response.text();
    let message = `Request failed with status ${response.status}.`;

    if (body.length > 0) {
      try {
        const problem = JSON.parse(body) as {
          detail?: string;
          reason?: string;
          errors?: Record<string, string[]>;
        };
        const validationMessages = Object.values(problem.errors ?? {})
          .flat()
          .join(" ");
        message =
          problem.reason ?? problem.detail ?? (validationMessages || message);
      } catch {
        message = body;
      }
    }

    throw new ApiError(message);
  }

  return (await response.json()) as T;
}

function formatRevisionDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "medium",
  }).format(new Date(value));
}

function StatusPill({
  children,
  muted = false,
}: {
  children: React.ReactNode;
  muted?: boolean;
}) {
  return (
    <span
      className={[
        "rounded-full px-2.5 py-1 text-[10px] font-bold uppercase tracking-wide",
        muted
          ? "bg-slate-500/10 text-[#53617d]"
          : "bg-emerald-500/15 text-emerald-800",
      ].join(" ")}
    >
      {children}
    </span>
  );
}

function CardHeading({
  eyebrow,
  title,
  action,
}: {
  eyebrow: string;
  title: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="mb-5 flex items-start justify-between gap-4">
      <div>
        <p className="font-mono text-[9px] font-bold uppercase tracking-[0.14em] text-[#7a8298]">
          {eyebrow}
        </p>
        <h2 className="mt-1 text-[16px] font-semibold text-[#1c2333]">
          {title}
        </h2>
      </div>
      {action}
    </div>
  );
}

export default function ControlPlanePage() {
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [deployment, setDeployment] = useState<DeploymentConfiguration | null>(
    null,
  );
  const [revisions, setRevisions] = useState<CatalogRevision[]>([]);
  const [selectedRevision, setSelectedRevision] =
    useState<CatalogRevisionSnapshot | null>(null);
  const [draft, setDraft] = useState<EntryDraft>(emptyDraft);
  const [editingEntryId, setEditingEntryId] = useState<string | null>(null);
  const [exceptionDraft, setExceptionDraft] = useState(emptyExceptionDraft);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [runs, setRuns] = useState<SynchronizationRun[]>([]);
  const [selectedRun, setSelectedRun] =
    useState<SynchronizationRunDetail | null>(null);
  const [runError, setRunError] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [isStartingRun, setIsStartingRun] = useState(false);

  const loadControlPlane = useCallback(async () => {
    try {
      const [nextCatalog, nextDeployment, nextRevisions, nextRuns] =
        await Promise.all([
          request<Catalog>("/api/catalog"),
          request<DeploymentConfiguration>("/api/deployment-configuration"),
          request<CatalogRevision[]>("/api/catalog/revisions"),
          request<SynchronizationRun[]>("/api/runs"),
        ]);

      setCatalog(nextCatalog);
      setDeployment(nextDeployment);
      setRevisions(nextRevisions);
      setRuns(nextRuns);
      setLoadError(null);
    } catch (error) {
      setLoadError(
        error instanceof Error
          ? error.message
          : "The Control Plane could not be loaded.",
      );
    }
  }, []);

  useEffect(() => {
    const timer = window.setTimeout(() => {
      void loadControlPlane();
    }, 0);

    return () => window.clearTimeout(timer);
  }, [loadControlPlane]);

  useEffect(() => {
    if (!runs.some((run) => run.status === "Running")) {
      return;
    }

    const timer = window.setInterval(() => void loadControlPlane(), 2_000);
    return () => window.clearInterval(timer);
  }, [loadControlPlane, runs]);

  const saveCatalog = async (
    entries: CatalogEntry[],
    vulnerabilityExceptions = catalog?.vulnerabilityExceptions ?? [],
  ) => {
    if (catalog === null) {
      return;
    }

    setIsSaving(true);
    setSaveError(null);
    try {
      const nextCatalog = await request<Catalog>("/api/catalog", {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          baseRevisionId: catalog.currentRevision?.id ?? null,
          entries: entries.map((entry) => ({
            id: entry.id,
            sourceReference: entry.sourceReference,
            targetRepository: entry.targetRepository,
            targetTag: entry.targetTag,
            credentialHandle: entry.credentialHandle,
            enabled: entry.enabled,
            kind: entry.kind,
            sourceVersion: entry.sourceVersion,
            expectedDigest: entry.expectedDigest,
          })),
          vulnerabilityExceptions,
        }),
      });
      const nextRevisions = await request<CatalogRevision[]>(
        "/api/catalog/revisions",
      );

      setCatalog(nextCatalog);
      setRevisions(nextRevisions);
      setDraft(emptyDraft);
      setEditingEntryId(null);
    } catch (error) {
      setSaveError(
        error instanceof Error
          ? error.message
          : "The Catalog could not be saved.",
      );
    } finally {
      setIsSaving(false);
    }
  };

  const viewRun = async (runId: string) => {
    try {
      setSelectedRun(
        await request<SynchronizationRunDetail>(`/api/runs/${runId}`),
      );
      setRunError(null);
    } catch (error) {
      setRunError(
        error instanceof Error
          ? error.message
          : "The Synchronization Run could not be loaded.",
      );
    }
  };

  const startRun = async () => {
    setIsStartingRun(true);
    setRunError(null);
    try {
      const run = await request<SynchronizationRun>("/api/runs", {
        method: "POST",
      });
      await Promise.all([loadControlPlane(), viewRun(run.id)]);
    } catch (error) {
      setRunError(
        error instanceof Error
          ? error.message
          : "The Synchronization Run could not be started.",
      );
    } finally {
      setIsStartingRun(false);
    }
  };

  const submitEntry = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (catalog === null) {
      return;
    }

    const entry: CatalogEntry = {
      ...draft,
      id: editingEntryId ?? crypto.randomUUID(),
      order: editingEntryId === null ? catalog.entries.length : 0,
    };
    const entries =
      editingEntryId === null
        ? [...catalog.entries, entry]
        : catalog.entries.map((candidate) =>
            candidate.id === editingEntryId ? entry : candidate,
          );

    void saveCatalog(entries);
  };

  const editEntry = (entry: CatalogEntry) => {
    setDraft({
      sourceReference: entry.sourceReference,
      targetRepository: entry.targetRepository,
      targetTag: entry.targetTag,
      credentialHandle: entry.credentialHandle,
      enabled: entry.enabled,
      kind: entry.kind,
      sourceVersion: entry.sourceVersion,
      expectedDigest: entry.expectedDigest,
    });
    setEditingEntryId(entry.id);
    setSaveError(null);
  };

  const moveEntry = (entryId: string, direction: -1 | 1) => {
    if (catalog === null) {
      return;
    }

    const index = catalog.entries.findIndex((entry) => entry.id === entryId);
    const nextIndex = index + direction;
    if (index < 0 || nextIndex < 0 || nextIndex >= catalog.entries.length) {
      return;
    }

    const entries = [...catalog.entries];
    [entries[index], entries[nextIndex]] = [entries[nextIndex], entries[index]];
    void saveCatalog(entries);
  };

  const viewRevision = (revisionId: string) => {
    void request<CatalogRevisionSnapshot>(
      `/api/catalog/revisions/${revisionId}`,
    )
      .then((revision) => {
        setSelectedRevision(revision);
        setSaveError(null);
      })
      .catch((error: unknown) => {
        setSaveError(
          error instanceof Error
            ? error.message
            : "The Catalog Revision could not be loaded.",
        );
      });
  };

  const submitException = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (catalog === null) {
      return;
    }

    const vulnerabilityIds = exceptionDraft.vulnerabilityIds
      .split(",")
      .map((vulnerabilityId) => vulnerabilityId.trim())
      .filter((vulnerabilityId) => vulnerabilityId.length > 0);
    void saveCatalog(catalog.entries, [
      ...catalog.vulnerabilityExceptions,
      {
        id: crypto.randomUUID(),
        imageDigest: exceptionDraft.imageDigest,
        vulnerabilityIds,
        reason: exceptionDraft.reason,
      },
    ]);
    setExceptionDraft(emptyExceptionDraft);
  };

  const expectedCredentialHandleType =
    draft.kind === "ImageMirror" ||
    (draft.kind === "HelmChart" && draft.sourceReference.startsWith("oci://"))
      ? "OciRegistry"
      : draft.kind === "WrapperBuild"
        ? "HttpsGit"
        : "HttpsHelm";
  const credentialHandles =
    deployment?.credentialHandles.filter(
      (handle) => handle.type === expectedCredentialHandleType,
    ) ?? [];
  const buttonClass =
    "rounded-[10px] border border-white/60 bg-white/55 px-3 py-1.5 text-xs font-semibold text-[#3b4b6b] shadow-sm transition-colors hover:bg-white/85 disabled:cursor-not-allowed disabled:opacity-50";

  return (
    <div className="min-h-full">
      <header className="sticky top-0 z-20 border-b border-white/70 bg-gradient-to-b from-white/72 to-white/50 px-6 shadow-[0_4px_24px_-12px_rgba(40,52,96,0.28)] backdrop-blur-2xl backdrop-saturate-150">
        <div className="mx-auto flex h-[62px] max-w-screen-2xl items-center gap-5">
          <div className="flex items-center gap-2.5 border-r border-[#7882a0]/20 pr-5">
            <div className="flex h-10 w-10 items-center justify-center rounded-[11px] bg-gradient-to-br from-[#4a82e0] via-[#6a6bd6] to-[#9a62cf] text-xs font-extrabold tracking-tight text-white shadow-[0_7px_18px_-6px_rgba(78,98,210,0.75)]">
              RB
            </div>
            <div>
              <p className="text-sm font-semibold tracking-tight text-[#1c2333]">
                RegistryBridge
              </p>
              <p className="font-mono text-[9px] uppercase tracking-wide text-[#7a8298]">
                Control Plane
              </p>
            </div>
          </div>
          <div className="rounded-[13px] border border-white/50 bg-white/28 p-1 shadow-[inset_0_1px_3px_rgba(40,52,96,0.08)]">
            <span className="inline-flex rounded-[9px] bg-gradient-to-br from-white/95 to-white/78 px-3 py-1.5 text-[12.5px] font-semibold text-[#1c2333] shadow-[0_2px_8px_-4px_rgba(40,52,96,0.22)]">
              Catalog
            </span>
          </div>
          <button
            className={`ml-auto ${buttonClass}`}
            disabled={isSaving}
            onClick={() => void loadControlPlane()}
            type="button"
          >
            Refresh
          </button>
        </div>
      </header>

      <main className="mx-auto max-w-screen-2xl px-6 py-8">
        <div className="mb-8 max-w-2xl">
          <p className="font-mono text-[10px] font-bold uppercase tracking-[0.16em] text-[#6a7287]">
            Deployment catalog
          </p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight text-[#1c2333]">
            Registry operations, clearly controlled.
          </h1>
          <p className="mt-2 text-sm leading-6 text-[#5f6982]">
            Configure immutable source-to-target image mirrors and review every
            Catalog Revision.
          </p>
        </div>

        {loadError !== null && (
          <p
            className="mb-5 rounded-[16px] border border-rose-300/60 bg-rose-50/70 px-4 py-3 text-sm text-rose-900"
            role="alert"
          >
            {loadError}
          </p>
        )}
        {saveError !== null && (
          <p
            className="mb-5 rounded-[16px] border border-rose-300/60 bg-rose-50/70 px-4 py-3 text-sm text-rose-900"
            role="alert"
          >
            {saveError}
          </p>
        )}

        <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_360px]">
          <div className="space-y-5">
            <section className={glassCard}>
              <CardHeading
                eyebrow="Catalog"
                title={
                  catalog === null
                    ? "Loading catalog"
                    : catalog.entries.length === 0
                      ? "No catalog entries"
                      : "Catalog entries"
                }
                action={
                  <StatusPill muted={catalog === null}>
                    {catalog === null
                      ? "Loading"
                      : `${catalog.entries.length} entries`}
                  </StatusPill>
                }
              />
              {catalog?.currentRevision === null && (
                <p className="mb-5 text-sm text-[#6a7287]">
                  Add an image mirror to create the first Catalog Revision.
                </p>
              )}
              {catalog !== null && catalog.currentRevision !== null && (
                <p className="mb-5 text-sm text-[#6a7287]">
                  Editing saves a new immutable revision based on{" "}
                  {formatRevisionDate(catalog.currentRevision.createdAt)}.
                </p>
              )}
              <div className="space-y-3">
                {catalog?.entries.map((entry, index) => (
                  <article
                    key={entry.id}
                    className="rounded-[16px] border border-white/55 bg-white/32 p-4"
                  >
                    <div className="flex flex-col gap-4 lg:flex-row lg:items-center">
                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-2">
                          <strong className="text-sm font-semibold text-[#1c2333]">
                            {entry.targetRepository}:{entry.targetTag}
                          </strong>
                          <StatusPill muted>
                            {entry.kind === "HelmChart"
                              ? "Helm Chart"
                              : entry.kind === "WrapperBuild"
                                ? "Wrapper Build"
                                : "Image Mirror"}
                          </StatusPill>
                          <StatusPill muted={!entry.enabled}>
                            {entry.enabled ? "Enabled" : "Disabled"}
                          </StatusPill>
                        </div>
                        <code className="mt-2 block wrap-anywhere font-mono text-[11px] leading-5 text-[#5f6982]">
                          {entry.sourceReference}
                        </code>
                        <p className="mt-1 text-xs text-[#7a8298]">
                          {entry.credentialHandle ?? "No Credential Handle"}
                        </p>
                      </div>
                      <div className="flex flex-wrap gap-2">
                        <button
                          className={buttonClass}
                          disabled={isSaving || index === 0}
                          onClick={() => moveEntry(entry.id, -1)}
                          type="button"
                        >
                          Move up
                        </button>
                        <button
                          className={buttonClass}
                          disabled={
                            isSaving || index === catalog.entries.length - 1
                          }
                          onClick={() => moveEntry(entry.id, 1)}
                          type="button"
                        >
                          Move down
                        </button>
                        <button
                          className={buttonClass}
                          disabled={isSaving}
                          onClick={() =>
                            void saveCatalog(
                              catalog.entries.map((candidate) =>
                                candidate.id === entry.id
                                  ? {
                                      ...candidate,
                                      enabled: !candidate.enabled,
                                    }
                                  : candidate,
                              ),
                            )
                          }
                          type="button"
                        >
                          {entry.enabled ? "Disable" : "Enable"}
                        </button>
                        <button
                          className={buttonClass}
                          disabled={isSaving}
                          onClick={() => editEntry(entry)}
                          type="button"
                        >
                          Edit
                        </button>
                        <button
                          className={`${buttonClass} text-rose-700`}
                          disabled={isSaving}
                          onClick={() =>
                            void saveCatalog(
                              catalog.entries.filter(
                                (candidate) => candidate.id !== entry.id,
                              ),
                            )
                          }
                          type="button"
                        >
                          Remove
                        </button>
                      </div>
                    </div>
                  </article>
                ))}
              </div>
            </section>

            <section className={glassCard}>
              <CardHeading
                eyebrow="Catalog entry"
                title={
                  editingEntryId === null
                    ? "Add Catalog Entry"
                    : "Edit Catalog Entry"
                }
              />
              <form
                className="grid gap-4 md:grid-cols-2"
                onSubmit={submitEntry}
              >
                <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b] md:col-span-2">
                  Entry kind
                  <select
                    className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setDraft({
                        ...draft,
                        credentialHandle: null,
                        expectedDigest: null,
                        kind: event.target.value as CatalogEntryKind,
                        sourceReference: "",
                        sourceVersion: null,
                        targetTag: "",
                      })
                    }
                    value={draft.kind}
                  >
                    <option value="ImageMirror">Image mirror</option>
                    <option value="WrapperBuild">Wrapper Build</option>
                    <option value="HelmChart">Helm Chart</option>
                  </select>
                </label>
                <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b] md:col-span-2">
                  {draft.kind === "ImageMirror"
                    ? "Pinned source image"
                    : draft.kind === "WrapperBuild"
                      ? "HTTPS Git repository"
                      : "Helm source"}
                  <input
                    className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setDraft({
                        ...draft,
                        credentialHandle:
                          draft.kind === "HelmChart"
                            ? null
                            : draft.credentialHandle,
                        sourceReference: event.target.value,
                      })
                    }
                    placeholder={
                      draft.kind === "ImageMirror"
                        ? "registry.example/team/image@sha256:..."
                        : draft.kind === "WrapperBuild"
                          ? "https://git.example/team/image.git"
                          : "https://charts.example/team or oci://registry.example/charts"
                    }
                    required
                    value={draft.sourceReference}
                  />
                </label>
                {draft.kind !== "ImageMirror" && (
                  <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b]">
                    {draft.kind === "WrapperBuild"
                      ? "Full Git commit ID"
                      : "Chart version"}
                    <input
                      className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                      disabled={isSaving || catalog === null}
                      onChange={(event) =>
                        setDraft({
                          ...draft,
                          sourceVersion: event.target.value || null,
                          targetTag:
                            draft.kind === "HelmChart"
                              ? event.target.value
                              : draft.targetTag,
                        })
                      }
                      required
                      value={draft.sourceVersion ?? ""}
                    />
                  </label>
                )}
                {draft.kind === "HelmChart" && (
                  <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b]">
                    Expected source digest
                    <input
                      className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                      disabled={isSaving || catalog === null}
                      onChange={(event) =>
                        setDraft({
                          ...draft,
                          expectedDigest: event.target.value || null,
                        })
                      }
                      placeholder="sha256:..."
                      required
                      value={draft.expectedDigest ?? ""}
                    />
                  </label>
                )}
                <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b]">
                  Target repository
                  <input
                    className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setDraft({
                        ...draft,
                        targetRepository: event.target.value,
                      })
                    }
                    placeholder="mirrors/team/image"
                    required
                    value={draft.targetRepository}
                  />
                </label>
                <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b]">
                  {draft.kind === "HelmChart"
                    ? "Target chart version"
                    : "Target tag"}
                  <input
                    className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setDraft({ ...draft, targetTag: event.target.value })
                    }
                    placeholder="2026.08.06"
                    required
                    value={draft.targetTag}
                  />
                </label>
                <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b]">
                  Credential Handle
                  <select
                    className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setDraft({
                        ...draft,
                        credentialHandle: event.target.value || null,
                      })
                    }
                    value={draft.credentialHandle ?? ""}
                  >
                    <option value="">No Credential Handle</option>
                    {credentialHandles.map((handle) => (
                      <option key={handle.name} value={handle.name}>
                        {handle.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label className="flex items-center gap-2 self-end pb-2 text-sm font-semibold text-[#3b4b6b]">
                  <input
                    checked={draft.enabled}
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setDraft({ ...draft, enabled: event.target.checked })
                    }
                    type="checkbox"
                  />
                  Enabled
                </label>
                <div className="flex gap-2 md:col-span-2">
                  <button
                    className="rounded-[10px] bg-[#3b78d8] px-4 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-[#2e63b5] disabled:cursor-not-allowed disabled:opacity-50"
                    disabled={isSaving || catalog === null}
                    type="submit"
                  >
                    {isSaving
                      ? "Saving..."
                      : editingEntryId === null
                        ? "Add entry"
                        : "Save entry"}
                  </button>
                  {editingEntryId !== null && (
                    <button
                      className={buttonClass}
                      disabled={isSaving}
                      onClick={() => {
                        setDraft(emptyDraft);
                        setEditingEntryId(null);
                        setSaveError(null);
                      }}
                      type="button"
                    >
                      Cancel edit
                    </button>
                  )}
                </div>
              </form>
            </section>

            <section className={glassCard}>
              <CardHeading
                eyebrow="Vulnerability policy"
                title="Vulnerability Exceptions"
                action={
                  <StatusPill muted>
                    {catalog?.vulnerabilityExceptions.length ?? 0} exceptions
                  </StatusPill>
                }
              />
              {catalog?.vulnerabilityExceptions.length === 0 && (
                <p className="mb-5 text-sm text-[#6a7287]">
                  No exceptions are recorded in the current Catalog Revision.
                </p>
              )}
              <div className="mb-5 space-y-3">
                {catalog?.vulnerabilityExceptions.map((exception) => (
                  <article
                    key={exception.id}
                    className="rounded-[16px] border border-white/55 bg-white/32 p-4"
                  >
                    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
                      <div className="min-w-0">
                        <code className="block wrap-anywhere text-xs font-semibold text-[#1c2333]">
                          {exception.imageDigest}
                        </code>
                        <p className="mt-2 text-xs text-[#5f6982]">
                          {exception.vulnerabilityIds.join(", ")}
                        </p>
                        <p className="mt-1 text-xs text-[#7a8298]">
                          {exception.reason}
                        </p>
                      </div>
                      <button
                        className={`${buttonClass} text-rose-700`}
                        disabled={isSaving}
                        onClick={() =>
                          void saveCatalog(
                            catalog.entries,
                            catalog.vulnerabilityExceptions.filter(
                              (candidate) => candidate.id !== exception.id,
                            ),
                          )
                        }
                        type="button"
                      >
                        Remove
                      </button>
                    </div>
                  </article>
                ))}
              </div>
              <form
                className="grid gap-4 md:grid-cols-2"
                onSubmit={submitException}
              >
                <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b] md:col-span-2">
                  Image digest
                  <input
                    className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setExceptionDraft({
                        ...exceptionDraft,
                        imageDigest: event.target.value,
                      })
                    }
                    placeholder="sha256:..."
                    required
                    value={exceptionDraft.imageDigest}
                  />
                </label>
                <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b]">
                  Vulnerability IDs
                  <input
                    className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setExceptionDraft({
                        ...exceptionDraft,
                        vulnerabilityIds: event.target.value,
                      })
                    }
                    placeholder="CVE-2026-0001, CVE-2026-0002"
                    required
                    value={exceptionDraft.vulnerabilityIds}
                  />
                </label>
                <label className="grid gap-1.5 text-xs font-semibold text-[#3b4b6b]">
                  Reason
                  <input
                    className="rounded-[10px] border border-white/70 bg-white/58 px-3 py-2 text-sm font-normal text-[#1c2333] outline-none transition focus:border-[#7192dd] focus:ring-2 focus:ring-[#7192dd]/20"
                    disabled={isSaving || catalog === null}
                    onChange={(event) =>
                      setExceptionDraft({
                        ...exceptionDraft,
                        reason: event.target.value,
                      })
                    }
                    required
                    value={exceptionDraft.reason}
                  />
                </label>
                <div className="md:col-span-2">
                  <button
                    className="rounded-[10px] bg-[#3b78d8] px-4 py-2 text-sm font-semibold text-white shadow-sm transition-colors hover:bg-[#2e63b5] disabled:cursor-not-allowed disabled:opacity-50"
                    disabled={isSaving || catalog === null}
                    type="submit"
                  >
                    Add exception
                  </button>
                </div>
              </form>
            </section>
          </div>

          <aside className="space-y-5">
            <section className={glassCard}>
              <CardHeading
                eyebrow="Deployment configuration"
                title="Target settings"
                action={<StatusPill muted>Read-only</StatusPill>}
              />
              {deployment === null ? (
                <p className="text-sm text-[#6a7287]">
                  Loading deployment configuration...
                </p>
              ) : (
                <dl className="space-y-4 text-sm">
                  <div>
                    <dt className="font-mono text-[9px] font-bold uppercase tracking-[0.14em] text-[#7a8298]">
                      Target registry
                    </dt>
                    <dd className="mt-1 font-medium text-[#1c2333]">
                      {deployment.targetRegistry || "Not configured"}
                    </dd>
                  </div>
                  <div>
                    <dt className="font-mono text-[9px] font-bold uppercase tracking-[0.14em] text-[#7a8298]">
                      Credential Handles
                    </dt>
                    <dd className="mt-2 space-y-2">
                      {deployment.credentialHandles.length === 0 ? (
                        <span className="text-[#6a7287]">None declared</span>
                      ) : (
                        deployment.credentialHandles.map((handle) => (
                          <div
                            className="rounded-[12px] border border-white/50 bg-white/28 px-3 py-2"
                            key={handle.name}
                          >
                            <code className="text-xs font-semibold text-[#1c2333]">
                              {handle.name}
                            </code>
                            <span className="ml-2 text-xs text-[#7a8298]">
                              {handle.type}
                            </span>
                          </div>
                        ))
                      )}
                    </dd>
                  </div>
                </dl>
              )}
            </section>

            <section className={glassCard}>
              <CardHeading
                eyebrow="Catalog revisions"
                title="Immutable history"
                action={
                  <StatusPill muted>{revisions.length} revisions</StatusPill>
                }
              />
              {revisions.length === 0 ? (
                <p className="text-sm text-[#6a7287]">
                  No Catalog Revisions have been created.
                </p>
              ) : (
                <ol className="space-y-3">
                  {revisions.map((revision) => (
                    <li
                      className="rounded-[14px] border border-white/50 bg-white/28 p-3"
                      key={revision.id}
                    >
                      <time
                        className="block text-xs font-medium text-[#3b4b6b]"
                        dateTime={revision.createdAt}
                      >
                        {formatRevisionDate(revision.createdAt)}
                      </time>
                      <code className="mt-1 block truncate font-mono text-[10px] text-[#7a8298]">
                        {revision.id}
                      </code>
                      <button
                        className={`mt-3 ${buttonClass}`}
                        onClick={() => viewRevision(revision.id)}
                        type="button"
                      >
                        View snapshot
                      </button>
                    </li>
                  ))}
                </ol>
              )}
              {selectedRevision !== null && (
                <div className="mt-4 border-t border-white/60 pt-4">
                  <p className="text-sm font-semibold text-[#1c2333]">
                    Revision snapshot
                  </p>
                  <p className="mt-1 text-xs text-[#6a7287]">
                    {formatRevisionDate(selectedRevision.revision.createdAt)}
                  </p>
                  <ol className="mt-3 space-y-2">
                    {selectedRevision.entries.map((entry) => (
                      <li
                        className="rounded-[12px] border border-white/45 bg-white/22 p-2.5 text-xs"
                        key={entry.id}
                      >
                        <strong className="block text-[#1c2333]">
                          {entry.targetRepository}:{entry.targetTag}
                        </strong>
                        <span className="mt-1 block text-[#6a7287]">
                          {entry.enabled ? "Enabled" : "Disabled"}
                        </span>
                      </li>
                    ))}
                  </ol>
                </div>
              )}
            </section>

            <section className={glassCard}>
              <CardHeading
                eyebrow="Synchronization Runs"
                title="Run history"
                action={
                  <button
                    className={buttonClass}
                    disabled={
                      isStartingRun ||
                      catalog === null ||
                      catalog.currentRevision === null
                    }
                    onClick={() => void startRun()}
                    type="button"
                  >
                    {isStartingRun ? "Starting..." : "Run now"}
                  </button>
                }
              />
              {runError !== null && (
                <p
                  className="mb-4 rounded-[12px] border border-rose-300/60 bg-rose-50/70 px-3 py-2 text-xs text-rose-900"
                  role="alert"
                >
                  {runError}
                </p>
              )}
              {runs.length === 0 ? (
                <p className="text-sm text-[#6a7287]">
                  No Synchronization Runs have been requested.
                </p>
              ) : (
                <ol className="space-y-3">
                  {runs.map((run) => (
                    <li
                      className="rounded-[14px] border border-white/50 bg-white/28 p-3"
                      key={run.id}
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <time
                          className="text-xs font-medium text-[#3b4b6b]"
                          dateTime={run.createdAt}
                        >
                          {formatRevisionDate(run.createdAt)}
                        </time>
                        <StatusPill muted={run.status !== "Completed"}>
                          {run.status}
                        </StatusPill>
                      </div>
                      <p className="mt-1 text-xs text-[#6a7287]">
                        {run.origin} request
                      </p>
                      <button
                        className={`mt-3 ${buttonClass}`}
                        onClick={() => void viewRun(run.id)}
                        type="button"
                      >
                        View details
                      </button>
                    </li>
                  ))}
                </ol>
              )}
              {selectedRun !== null && (
                <div className="mt-4 border-t border-white/60 pt-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <p className="text-sm font-semibold text-[#1c2333]">
                      Run details
                    </p>
                    <StatusPill muted={selectedRun.status !== "Completed"}>
                      {selectedRun.status}
                    </StatusPill>
                  </div>
                  {selectedRun.logsTruncated && (
                    <p
                      className="mt-3 rounded-[12px] border border-amber-300/60 bg-amber-50/70 px-3 py-2 text-xs text-amber-900"
                      role="status"
                    >
                      Persisted logs reached this deployment&apos;s configured
                      limit. Complete JSON logs remain available from the
                      application process.
                    </p>
                  )}
                  <p className="mt-4 text-xs font-semibold text-[#3b4b6b]">
                    Artifact Outcomes
                  </p>
                  {selectedRun.outcomes.length === 0 ? (
                    <p className="mt-2 text-xs text-[#6a7287]">
                      No outcomes have been recorded yet.
                    </p>
                  ) : (
                    <ol className="mt-2 space-y-2">
                      {selectedRun.outcomes.map((outcome) => (
                        <li
                          className="rounded-[12px] border border-white/45 bg-white/22 p-2.5 text-xs"
                          key={`${outcome.catalogEntryId}-${outcome.order}`}
                        >
                          <div className="flex flex-wrap items-center justify-between gap-2">
                            <span className="text-[#3b4b6b]">
                              Entry {outcome.order + 1}
                            </span>
                            <StatusPill
                              muted={outcome.disposition === "Not selected"}
                            >
                              {outcome.disposition}
                            </StatusPill>
                          </div>
                          {outcome.detail !== null && (
                            <p className="mt-1 wrap-anywhere text-[#6a7287]">
                              {outcome.detail}
                            </p>
                          )}
                        </li>
                      ))}
                    </ol>
                  )}
                  <p className="mt-4 text-xs font-semibold text-[#3b4b6b]">
                    Persisted Run Logs
                  </p>
                  {selectedRun.logs.length === 0 ? (
                    <p className="mt-2 text-xs text-[#6a7287]">
                      No log events were retained.
                    </p>
                  ) : (
                    <ol className="mt-2 max-h-80 space-y-2 overflow-y-auto">
                      {selectedRun.logs.map((log) => (
                        <li
                          className="rounded-[12px] border border-white/45 bg-white/22 p-2.5 text-xs"
                          key={`${log.occurredAt}-${log.eventType}`}
                        >
                          <p className="font-medium text-[#3b4b6b]">
                            {log.eventType}
                          </p>
                          <code className="mt-1 block wrap-anywhere font-mono text-[10px] leading-4 text-[#6a7287]">
                            {log.data}
                          </code>
                        </li>
                      ))}
                    </ol>
                  )}
                </div>
              )}
            </section>
          </aside>
        </div>
      </main>
    </div>
  );
}
