import { useCallback, useEffect, useState, type FormEvent } from 'react'
import './App.css'

type CatalogRevision = {
  id: string
  createdAt: string
}

type CatalogEntry = {
  id: string
  sourceReference: string
  targetRepository: string
  targetTag: string
  credentialHandle: string | null
  enabled: boolean
  order: number
}

type Catalog = {
  currentRevision: CatalogRevision | null
  entries: CatalogEntry[]
}

type CatalogRevisionSnapshot = {
  revision: CatalogRevision
  entries: CatalogEntry[]
}

type CredentialHandle = {
  name: string
  type: string
}

type DeploymentConfiguration = {
  targetRegistry: string
  credentialHandles: CredentialHandle[]
}

type EntryDraft = Omit<CatalogEntry, 'id' | 'order'>

const emptyDraft: EntryDraft = {
  sourceReference: '',
  targetRepository: '',
  targetTag: '',
  credentialHandle: null,
  enabled: true,
}

class ApiError extends Error {}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, init)
  if (!response.ok) {
    const body = await response.text()
    let message = `Request failed with status ${response.status}.`

    if (body.length > 0) {
      try {
        const problem = JSON.parse(body) as {
          detail?: string
          reason?: string
          errors?: Record<string, string[]>
        }
        const validationMessages = Object.values(problem.errors ?? {}).flat().join(' ')
        message =
          problem.reason ??
          problem.detail ??
          (validationMessages || message)
      } catch {
        message = body
      }
    }

    throw new ApiError(message)
  }

  return (await response.json()) as T
}

function formatRevisionDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'medium',
  }).format(new Date(value))
}

function App() {
  const [catalog, setCatalog] = useState<Catalog | null>(null)
  const [deployment, setDeployment] = useState<DeploymentConfiguration | null>(null)
  const [revisions, setRevisions] = useState<CatalogRevision[]>([])
  const [selectedRevision, setSelectedRevision] = useState<CatalogRevisionSnapshot | null>(null)
  const [draft, setDraft] = useState<EntryDraft>(emptyDraft)
  const [editingEntryId, setEditingEntryId] = useState<string | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [saveError, setSaveError] = useState<string | null>(null)
  const [isSaving, setIsSaving] = useState(false)

  const loadControlPlane = useCallback(async () => {
    try {
      const [nextCatalog, nextDeployment, nextRevisions] = await Promise.all([
        request<Catalog>('/api/catalog'),
        request<DeploymentConfiguration>('/api/deployment-configuration'),
        request<CatalogRevision[]>('/api/catalog/revisions'),
      ])

      setCatalog(nextCatalog)
      setDeployment(nextDeployment)
      setRevisions(nextRevisions)
      setLoadError(null)
    } catch (error) {
      setLoadError(error instanceof Error ? error.message : 'The Control Plane could not be loaded.')
    }
  }, [])

  useEffect(() => {
    void loadControlPlane()
  }, [loadControlPlane])

  const saveCatalog = async (entries: CatalogEntry[]) => {
    if (catalog === null) {
      return
    }

    setIsSaving(true)
    setSaveError(null)

    try {
      const nextCatalog = await request<Catalog>('/api/catalog', {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({
          baseRevisionId: catalog.currentRevision?.id ?? null,
          entries: entries.map(({ order: _order, ...entry }) => entry),
        }),
      })
      const nextRevisions = await request<CatalogRevision[]>('/api/catalog/revisions')

      setCatalog(nextCatalog)
      setRevisions(nextRevisions)
      setDraft(emptyDraft)
      setEditingEntryId(null)
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : 'The Catalog could not be saved.')
    } finally {
      setIsSaving(false)
    }
  }

  const submitEntry = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (catalog === null) {
      return
    }

    const entry: CatalogEntry = {
      ...draft,
      id: editingEntryId ?? crypto.randomUUID(),
      order: editingEntryId === null ? catalog.entries.length : 0,
    }
    const entries =
      editingEntryId === null
        ? [...catalog.entries, entry]
        : catalog.entries.map((candidate) => (candidate.id === editingEntryId ? entry : candidate))

    void saveCatalog(entries)
  }

  const editEntry = (entry: CatalogEntry) => {
    setDraft({
      sourceReference: entry.sourceReference,
      targetRepository: entry.targetRepository,
      targetTag: entry.targetTag,
      credentialHandle: entry.credentialHandle,
      enabled: entry.enabled,
    })
    setEditingEntryId(entry.id)
    setSaveError(null)
  }

  const removeEntry = (entryId: string) => {
    if (catalog !== null) {
      void saveCatalog(catalog.entries.filter((entry) => entry.id !== entryId))
    }
  }

  const moveEntry = (entryId: string, direction: -1 | 1) => {
    if (catalog === null) {
      return
    }

    const index = catalog.entries.findIndex((entry) => entry.id === entryId)
    const nextIndex = index + direction
    if (index < 0 || nextIndex < 0 || nextIndex >= catalog.entries.length) {
      return
    }

    const entries = [...catalog.entries]
    ;[entries[index], entries[nextIndex]] = [entries[nextIndex], entries[index]]
    void saveCatalog(entries)
  }

  const toggleEntry = (entry: CatalogEntry) => {
    if (catalog !== null) {
      void saveCatalog(
        catalog.entries.map((candidate) =>
          candidate.id === entry.id ? { ...candidate, enabled: !candidate.enabled } : candidate,
        ),
      )
    }
  }

  const viewRevision = (revisionId: string) => {
    void request<CatalogRevisionSnapshot>(`/api/catalog/revisions/${revisionId}`)
      .then((revision) => {
        setSelectedRevision(revision)
        setSaveError(null)
      })
      .catch((error: unknown) => {
        setSaveError(error instanceof Error ? error.message : 'The Catalog Revision could not be loaded.')
      })
  }

  const catalogHeading =
    catalog === null ? 'Loading Catalog...' : catalog.entries.length === 0 ? 'No Catalog Entries' : 'Catalog Entries'
  const credentialHandles = deployment?.credentialHandles.filter((handle) => handle.type === 'OciRegistry') ?? []

  return (
    <main>
      <header>
        <p className="eyebrow">RegistryBridge</p>
        <h1>Control Plane</h1>
        <p>Operate this deployment&apos;s Catalog and synchronization work.</p>
      </header>

      {loadError !== null ? <p className="alert" role="alert">{loadError}</p> : null}

      <section aria-labelledby="deployment-heading">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Deployment Configuration</p>
            <h2 id="deployment-heading">Read-only deployment settings</h2>
          </div>
          <span className="status">Read-only</span>
        </div>
        {deployment === null ? (
          <p>Loading deployment configuration...</p>
        ) : (
          <dl className="deployment-details">
            <div>
              <dt>Target registry</dt>
              <dd>{deployment.targetRegistry || 'Not configured'}</dd>
            </div>
            <div>
              <dt>Credential Handles</dt>
              <dd>
                {deployment.credentialHandles.length === 0 ? (
                  'None declared'
                ) : (
                  <ul className="credential-list">
                    {deployment.credentialHandles.map((handle) => (
                      <li key={handle.name}>
                        <code>{handle.name}</code> <span>{handle.type}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </dd>
            </div>
          </dl>
        )}
      </section>

      <section aria-labelledby="catalog-heading">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Catalog</p>
            <h2 id="catalog-heading">{catalogHeading}</h2>
          </div>
          <span className="status">{catalog === null ? 'Loading' : `${catalog.entries.length} entries`}</span>
        </div>
        {catalog === null || catalog.currentRevision === null ? (
          <p>This deployment has no Catalog Revision yet. Add an image mirror to create the first one.</p>
        ) : (
          <p className="revision-note">
            Editing saves a new Catalog Revision based on {formatRevisionDate(catalog.currentRevision.createdAt)}.
          </p>
        )}

        {catalog !== null && catalog.entries.length > 0 ? (
          <ol className="catalog-entries">
            {catalog.entries.map((entry, index) => (
              <li className="catalog-entry" key={entry.id}>
                <div className="entry-summary">
                  <strong>{entry.targetRepository}:{entry.targetTag}</strong>
                  <span className={entry.enabled ? 'entry-state enabled' : 'entry-state'}>{entry.enabled ? 'Enabled' : 'Disabled'}</span>
                  <code>{entry.sourceReference}</code>
                  <span>{entry.credentialHandle ?? 'No Credential Handle'}</span>
                </div>
                <div className="entry-actions">
                  <button disabled={isSaving || index === 0} onClick={() => moveEntry(entry.id, -1)} type="button">Move up</button>
                  <button disabled={isSaving || index === (catalog?.entries.length ?? 0) - 1} onClick={() => moveEntry(entry.id, 1)} type="button">Move down</button>
                  <button disabled={isSaving} onClick={() => toggleEntry(entry)} type="button">
                    {entry.enabled ? 'Disable' : 'Enable'}
                  </button>
                  <button disabled={isSaving} onClick={() => editEntry(entry)} type="button">Edit</button>
                  <button className="danger" disabled={isSaving} onClick={() => removeEntry(entry.id)} type="button">Remove</button>
                </div>
              </li>
            ))}
          </ol>
        ) : null}

        <form className="entry-form" onSubmit={submitEntry}>
          <h3>{editingEntryId === null ? 'Add image mirror' : 'Edit image mirror'}</h3>
          <label>
            Pinned source image
            <input
              disabled={isSaving || catalog === null}
              onChange={(event) => setDraft({ ...draft, sourceReference: event.target.value })}
              placeholder="registry.example/team/image@sha256:..."
              required
              value={draft.sourceReference}
            />
          </label>
          <label>
            Target Repository
            <input
              disabled={isSaving || catalog === null}
              onChange={(event) => setDraft({ ...draft, targetRepository: event.target.value })}
              placeholder="mirrors/team/image"
              required
              value={draft.targetRepository}
            />
          </label>
          <label>
            Target Tag
            <input
              disabled={isSaving || catalog === null}
              onChange={(event) => setDraft({ ...draft, targetTag: event.target.value })}
              placeholder="2026.08.06"
              required
              value={draft.targetTag}
            />
          </label>
          <label>
            Credential Handle
            <select
              disabled={isSaving || catalog === null}
              onChange={(event) => setDraft({ ...draft, credentialHandle: event.target.value || null })}
              value={draft.credentialHandle ?? ''}
            >
              <option value="">No Credential Handle</option>
              {credentialHandles.map((handle) => (
                <option key={handle.name} value={handle.name}>{handle.name}</option>
              ))}
            </select>
          </label>
          <label className="checkbox">
            <input
              checked={draft.enabled}
              disabled={isSaving || catalog === null}
              onChange={(event) => setDraft({ ...draft, enabled: event.target.checked })}
              type="checkbox"
            />
            Enabled
          </label>
          {saveError !== null ? <p className="alert" role="alert">{saveError}</p> : null}
          <div className="form-actions">
            <button disabled={isSaving || catalog === null} type="submit">
              {isSaving ? 'Saving...' : editingEntryId === null ? 'Add entry' : 'Save entry'}
            </button>
            {editingEntryId !== null ? (
              <button
                disabled={isSaving}
                onClick={() => {
                  setDraft(emptyDraft)
                  setEditingEntryId(null)
                  setSaveError(null)
                }}
                type="button"
              >
                Cancel edit
              </button>
            ) : null}
          </div>
        </form>
      </section>

      <section aria-labelledby="revision-heading">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Catalog Revisions</p>
            <h2 id="revision-heading">Immutable history</h2>
          </div>
          <span className="status">{revisions.length} revisions</span>
        </div>
        {revisions.length === 0 ? (
          <p>No Catalog Revisions have been created.</p>
        ) : (
          <ol className="revision-history">
            {revisions.map((revision) => (
              <li key={revision.id}>
                <time dateTime={revision.createdAt}>{formatRevisionDate(revision.createdAt)}</time>
                <span>
                  <code>{revision.id}</code>
                  <button onClick={() => viewRevision(revision.id)} type="button">View snapshot</button>
                </span>
              </li>
            ))}
          </ol>
        )}
        {selectedRevision !== null ? (
          <div className="revision-snapshot">
            <h3>Revision snapshot</h3>
            <p>{formatRevisionDate(selectedRevision.revision.createdAt)}</p>
            <ol>
              {selectedRevision.entries.map((entry) => (
                <li key={entry.id}>
                  <strong>{entry.targetRepository}:{entry.targetTag}</strong>
                  <span>{entry.enabled ? 'Enabled' : 'Disabled'}</span>
                  <code>{entry.sourceReference}</code>
                </li>
              ))}
            </ol>
          </div>
        ) : null}
        <p className="revision-note">Catalog Revisions are view-only and cannot be restored.</p>
      </section>
    </main>
  )
}

export default App
