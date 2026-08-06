import { useEffect, useState } from 'react'
import './App.css'

type Catalog = {
  currentRevision: { id: string; createdAt: string } | null
  entries: unknown[]
}

function App() {
  const [catalog, setCatalog] = useState<Catalog | null>(null)
  const [loadError, setLoadError] = useState(false)

  useEffect(() => {
    const controller = new AbortController()

    void fetch('/api/catalog', { signal: controller.signal })
      .then((response) => {
        if (!response.ok) {
          throw new Error('The Catalog could not be loaded.')
        }

        return response.json() as Promise<Catalog>
      })
      .then(setCatalog)
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setLoadError(true)
      })

    return () => controller.abort()
  }, [])

  const catalogHeading =
    catalog === null ? 'Loading Catalog...' : catalog.entries.length === 0 ? 'No Catalog Entries' : 'Catalog Entries'

  return (
    <main>
      <header>
        <p className="eyebrow">RegistryBridge</p>
        <h1>Control Plane</h1>
        <p>Operate this deployment&apos;s Catalog and synchronization work.</p>
      </header>
      <section aria-labelledby="catalog-heading">
        <div className="section-heading">
          <div>
            <p className="eyebrow">Catalog</p>
            <h2 id="catalog-heading">{catalogHeading}</h2>
          </div>
          <span className="status">{catalog === null ? 'Loading' : 'Empty'}</span>
        </div>
        {loadError ? (
          <p role="alert">The Catalog could not be loaded. Check that the application is ready.</p>
        ) : (
          <p>
            This deployment has no Catalog Entries yet. A future Catalog Revision
            will record the Catalog Entries that configure synchronization.
          </p>
        )}
      </section>
    </main>
  )
}

export default App
