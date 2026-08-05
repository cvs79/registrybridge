# RegistryBridge

RegistryBridge is a single-tenant product for operating a deployment-specific catalog of registry artifacts and synchronizing them to its target registry.

## Language

**Control Plane**:
The unauthenticated capability used to administer a deployment and observe or initiate synchronization work. The first iteration runs it locally through Docker Compose, binds it to localhost, and accepts contention with an in-process Wrapper Build.
_Avoid_: Dashboard, admin site

**Catalog**:
The deployment's configured, operator-ordered collection of Catalog Entries and Vulnerability Exceptions to synchronize to its target registry. Its order establishes sequential execution order.
_Avoid_: Manifest, inventory

**Catalog Revision**:
An immutable full snapshot of a Catalog created when its configuration changes and used as the exact current input to a Synchronization Run. Revision history is view-only; saves based on a non-current revision are rejected.
_Avoid_: Version, artifact revision

**Catalog Entry**:
A configuration for synchronizing one source to a unique Target Repository. An entry may be enabled or disabled without being removed from the Catalog.
_Avoid_: Artifact, item

**Target Repository**:
An ACR-relative repository path owned by exactly one active Catalog Entry.
_Avoid_: Destination, target path

**Target Tag**:
The explicit immutable tag at which an image Artifact is published in a Target Repository. It cannot later identify a different digest.
_Avoid_: Image tag, version

**Artifact**:
A versioned registry object that a Catalog Entry mirrors or builds during a Synchronization Run. Image Artifacts are identified to consumers by their Target Tag.
_Avoid_: Catalog entry, image

**Helm Chart**:
A versioned chart Artifact whose configured Target Repository includes its chart name and must match its packaged name. Its source and target version are identical and immutable, and its configured source digest must match the fetched chart.
_Avoid_: Chart package, Helm artifact

**Vulnerability Exception**:
An explicit allowance, with a written reason, for named vulnerability IDs on one image Artifact digest. It is versioned with the Catalog and does not apply to a different Artifact or to newly discovered findings.
_Avoid_: Bypass, waiver

**Vulnerability Finding**:
A vulnerability reported for an image Artifact digest by a particular scan. An initial promotion requires a usable scan; unexcepted CRITICAL findings block the Artifact Outcome, including when that Artifact already exists in the Target Repository.
_Avoid_: CVE status, scan result

**Wrapper Build**:
A build of an image Artifact from a customer-controlled HTTPS source revision pinned by a full Git commit ID, whose external base images are all pinned by digest. The source revision fully defines its build arguments and cannot use Git submodules. Its build instructions may use the network but never receive deployment secrets. Deployments with Wrapper Builds use one CPU architecture.
_Avoid_: Custom image, Docker build

**Credential Handle**:
A Catalog Entry's non-secret reference to one named, typed credential injected into its deployment by the platform. A Catalog Revision cannot reference a handle that the deployment has not declared or whose type is incompatible with the entry.
_Avoid_: Secret, password

**Deployment Configuration**:
The deployment-supplied, read-only settings that govern one RegistryBridge instance, including its fixed target registry, one shared 90-day-default retention duration for operational records, runtime settings, and declared Credential Handles.
_Avoid_: Catalog, application settings

**Run Log**:
A structured event emitted during a Synchronization Run, persisted for the Control Plane and emitted as JSON to the deployment's standard output. Configured secret values and credential-bearing URL fragments are redacted before either destination. Persisted log volume is capped per run by a deployment-configured limit that defaults to 10 MiB.
_Avoid_: Console output, trace

**Artifact Outcome**:
The recorded disposition of a Catalog Entry in a Synchronization Run: Promoted for a newly published Artifact, Verified for an already-present Artifact that passed scanning, Blocked by policy, Conflict with an existing immutable target, Failed, or Not selected.
_Avoid_: Status, result

**Synchronization Run**:
An immutable record of one requested catalog synchronization, including an execution, a recorded scheduled skip when another run is already active, or an Abandoned execution that ended without completing. A manual request made during an active run is rejected without creating a new record. Its Run Origin identifies whether it was requested manually or by a schedule.
_Avoid_: Job, task

**Run Origin**:
The source that requested a Synchronization Run: manual or scheduled. It is not an operator identity.
_Avoid_: User, owner
