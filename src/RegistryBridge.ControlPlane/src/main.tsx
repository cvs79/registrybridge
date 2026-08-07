import { FormEvent, useCallback, useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import "./styles.css";

type CatalogRevision = { id: string; createdAt: string };
type CatalogEntry = {
  id: string;
  sourceReference: string;
  targetRepository: string;
  targetTag: string;
  credentialHandle: string | null;
  enabled: boolean;
  order: number;
  kind: "ImageMirror" | "WrapperBuild" | "HelmChart";
  sourceVersion: string | null;
  expectedDigest: string | null;
};
type Catalog = { currentRevision: CatalogRevision | null; entries: CatalogEntry[] };
type CatalogEntryDraft = Omit<CatalogEntry, "id" | "order">;
type Deployment = {
  targetRegistry: string;
  credentialHandles: { name: string; type: string }[];
};
type SynchronizationRun = {
  id: string;
  origin: string;
  status: string;
  createdAt: string;
  completedAt: string | null;
};

const blankEntry: CatalogEntryDraft = {
  sourceReference: "",
  targetRepository: "",
  targetTag: "",
  credentialHandle: null as string | null,
  enabled: true,
  kind: "ImageMirror" as const,
  sourceVersion: null as string | null,
  expectedDigest: null as string | null,
};

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init);
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as
      | { detail?: string; reason?: string; errors?: Record<string, string[]> }
      | null;
    const errors = problem?.errors ? Object.values(problem.errors).flat().join(" ") : "";
    throw new Error(problem?.reason ?? problem?.detail ?? (errors || `Request failed (${response.status}).`));
  }
  return response.json() as Promise<T>;
}

function App() {
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [deployment, setDeployment] = useState<Deployment | null>(null);
  const [synchronizationRuns, setSynchronizationRuns] = useState<SynchronizationRun[]>([]);
  const [entry, setEntry] = useState<CatalogEntryDraft>(blankEntry);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const [nextCatalog, nextDeployment, nextRuns] = await Promise.all([
        api<Catalog>("/api/catalog"),
        api<Deployment>("/api/deployment-configuration"),
        api<SynchronizationRun[]>("/api/runs"),
      ]);
      setCatalog(nextCatalog);
      setDeployment(nextDeployment);
      setSynchronizationRuns(nextRuns);
      setError(null);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Unable to load the Control Plane.");
    }
  }, []);

  useEffect(() => {
    void load();
    const poll = window.setInterval(() => void load(), 5000);
    return () => window.clearInterval(poll);
  }, [load]);

  async function saveCatalog(entries: CatalogEntry[]) {
    if (catalog === null) {
      return;
    }
    setBusy(true);
    try {
      const nextCatalog = await api<Catalog>("/api/catalog", {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          baseRevisionId: catalog.currentRevision?.id ?? null,
          entries,
        }),
      });
      setCatalog(nextCatalog);
      setEntry(blankEntry);
      setError(null);
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Unable to save the Catalog.");
    } finally {
      setBusy(false);
    }
  }

  function submitEntry(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (catalog === null) {
      return;
    }
    void saveCatalog([
      ...catalog.entries,
      { ...entry, id: crypto.randomUUID(), order: catalog.entries.length },
    ]);
  }

  async function startRun() {
    setBusy(true);
    try {
      await api<SynchronizationRun>("/api/runs", { method: "POST" });
      await load();
    } catch (runError) {
      setError(runError instanceof Error ? runError.message : "Unable to start a Synchronization Run.");
    } finally {
      setBusy(false);
    }
  }

  const sourceLabel = entry.kind === "WrapperBuild"
    ? "HTTPS Git repository"
    : entry.kind === "HelmChart"
      ? "HTTPS or OCI chart source"
      : "Pinned source image";
  const requiredCredentialType = entry.kind === "WrapperBuild"
    ? "HttpsGit"
    : entry.kind === "HelmChart" && entry.sourceReference.startsWith("oci://")
      ? "OciRegistry"
      : entry.kind === "HelmChart"
        ? "HttpsHelm"
        : "OciRegistry";

  return (
    <main>
      <header>
        <div>
          <p className="eyebrow">RegistryBridge</p>
          <h1>Control Plane</h1>
        </div>
        <button disabled={busy} onClick={() => void startRun()}>Start Synchronization Run</button>
      </header>

      {error && <p className="error" role="alert">{error}</p>}

      <section className="grid">
        <article>
          <p className="eyebrow">Deployment Configuration</p>
          <h2>{deployment?.targetRegistry || "Loading..."}</h2>
          <p>Target registry</p>
        </article>
        <article>
          <p className="eyebrow">Current Catalog Revision</p>
          <h2>{catalog?.currentRevision ? new Date(catalog.currentRevision.createdAt).toLocaleString() : "Empty Catalog"}</h2>
          <p>{catalog?.entries.length ?? 0} Catalog Entries</p>
        </article>
      </section>

      <section>
        <div className="section-heading">
          <div>
            <p className="eyebrow">Catalog</p>
            <h2>Catalog Entries</h2>
          </div>
        </div>
        <form onSubmit={submitEntry}>
          <label>
            Entry kind
            <select
              value={entry.kind}
              onChange={(event) => setEntry({ ...entry, kind: event.target.value as CatalogEntry["kind"] })}
            >
              <option value="ImageMirror">Image mirror</option>
              <option value="WrapperBuild">Wrapper Build</option>
              <option value="HelmChart">Helm Chart</option>
            </select>
          </label>
          <label>
            {sourceLabel}
            <input
              value={entry.sourceReference}
              placeholder={entry.kind === "ImageMirror" ? "registry.example/team/image@sha256:..." : "https://source.example/team/widget"}
              onChange={(event) => setEntry({ ...entry, sourceReference: event.target.value })}
              required
            />
          </label>
          {entry.kind !== "ImageMirror" && (
            <label>
              {entry.kind === "WrapperBuild" ? "Full Git commit ID" : "Chart version"}
              <input
                value={entry.sourceVersion ?? ""}
                placeholder={entry.kind === "WrapperBuild" ? "40-character commit ID" : "1.2.3"}
                onChange={(event) => setEntry({ ...entry, sourceVersion: event.target.value || null })}
                required
              />
            </label>
          )}
          {entry.kind === "HelmChart" && (
            <label>
              Expected source digest
              <input
                value={entry.expectedDigest ?? ""}
                placeholder="sha256:..."
                onChange={(event) => setEntry({ ...entry, expectedDigest: event.target.value || null })}
                required
              />
            </label>
          )}
          <label>
            Target Repository
            <input
              value={entry.targetRepository}
              placeholder="mirrors/team/image"
              onChange={(event) => setEntry({ ...entry, targetRepository: event.target.value })}
              required
            />
          </label>
          <label>
            Target Tag
            <input
              value={entry.targetTag}
              placeholder="stable"
              onChange={(event) => setEntry({ ...entry, targetTag: event.target.value })}
              required
            />
          </label>
          <label>
            Credential Handle
            <select
              value={entry.credentialHandle ?? ""}
              onChange={(event) => setEntry({ ...entry, credentialHandle: event.target.value || null })}
            >
              <option value="">None</option>
              {deployment?.credentialHandles
                .filter((handle) => handle.type === requiredCredentialType)
                .map((handle) => <option key={handle.name} value={handle.name}>{handle.name}</option>)}
            </select>
          </label>
          <label className="checkbox">
            <input
              type="checkbox"
              checked={entry.enabled}
              onChange={(event) => setEntry({ ...entry, enabled: event.target.checked })}
            />
            Enabled
          </label>
          <button disabled={busy} type="submit">Add Catalog Entry</button>
        </form>
        <div className="entries">
          {catalog?.entries.map((catalogEntry) => (
            <article key={catalogEntry.id}>
              <div>
                <strong>{catalogEntry.kind} - {catalogEntry.targetRepository}:{catalogEntry.targetTag}</strong>
                <p>{catalogEntry.sourceReference}</p>
              </div>
              <span>{catalogEntry.enabled ? "Enabled" : "Disabled"}</span>
            </article>
          ))}
        </div>
      </section>

      <section>
        <p className="eyebrow">Synchronization Runs</p>
        <h2>Recent run history</h2>
        <div className="runs">
          {synchronizationRuns.length === 0 && <p>No Synchronization Runs yet.</p>}
          {synchronizationRuns.map((run) => (
            <article key={run.id}>
              <strong>{run.status}</strong>
              <span>{run.origin}</span>
              <time>{new Date(run.createdAt).toLocaleString()}</time>
            </article>
          ))}
        </div>
      </section>
    </main>
  );
}

createRoot(document.getElementById("root")!).render(<App />);
