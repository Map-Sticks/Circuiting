# Native Revit panel integration plan

**Status:** Proposed design, recorded on 2026-11-08. Native panel integration
has not been implemented or validated in Revit. The API behavior, field
mappings, slot mapping and workflows below must be verified before they are
treated as implementation requirements.

This document records the discussion so that future coding sessions can
continue it. It is not a description of currently available features.

## Goals and decisions from the discussion

- Create an add-in distribution board (DB) from an existing Revit electrical
  panel, using the panel's name as the initial DB name.
- Import the panel's existing native circuits and connected elements into the
  add-in tree and organize them into user-defined chassis.
- Support projects that use standalone boards, linked boards, or a mixture.
  The mode is chosen **per board**, with an explicit consistency policy.
- Let users define how circuits are assigned to chassis: by connected-element
  category, load classification, a chosen parameter value, or manually.
- Respect the native panel's capacity. For example, a DB with a 30-pole
  Lighting chassis and a 30-pole Power chassis needs a panel with capacity for
  at least 60 single-pole positions, subject to Revit's panel and schedule
  constraints.
- Keep Revit's native **Circuit Number** useful for schedules and tags. The
  desired result is the add-in's configured circuit label, potentially by
  using a Revit circuit naming scheme driven by a shared parameter rather
  than attempting to edit Circuit Number directly.
- Consider native System Browser and panel schedule capabilities, existing
  project electrical settings, and the user's pyRevit Circuit Hub rather than
  duplicating functionality unnecessarily.

The proposed defaults and detailed workflows below are a starting point, not
proof that Revit supports them or that every interaction has been decided.

## Current baseline

At the time of this plan, Circuiting manages its own boards, chassis, circuits
and element assignments in Extensible Storage. It does not create or update
native Revit electrical circuits. The existing tree, circuit labels, selection,
bulk operations and standalone workflows should remain usable.

Relevant starting points:

- [BoardData](./Models/BoardData.cs), [ChassisData](./Models/ChassisData.cs)
  and [CircuitData](./Models/CircuitData.cs): the existing data model.
- [ProjectDataSerializer](./Storage/ProjectDataSerializer.cs) and
  [BoardRepository](./Storage/BoardRepository.cs): project data validation and
  persistence.
- [FixtureAssignmentSynchronizer](./Services/FixtureAssignmentSynchronizer.cs)
  and [RevitFixtureAssignmentStore](./Storage/RevitFixtureAssignmentStore.cs):
  standalone element assignments.
- [CircuitLabelCalculator](./Services/CircuitLabelCalculator.cs) and
  [CircuitLayout](./Services/CircuitLayout.cs): existing labels and pole
  layout.
- [BulkCircuitOperations](./Services/BulkCircuitOperations.cs): possible
  starting point for placement planning; it does not validate Revit schedule
  constraints.
- [RevitEventHandler](./Commands/RevitEventHandler.cs): the existing
  ExternalEvent-based boundary for Revit API actions.

When resolving existing fixtures, keep the current identity safeguards:
prefer `RevitUniqueId`, and use a legacy numeric ID only if the stored fixture
GUID confirms the identity. See [RevitElementIds](./Services/RevitElementIds.cs)
and [FixtureIdentity](./Services/FixtureIdentity.cs). Reconciliation must also
consider elements with multiple electrical connectors or native systems.

## Proposed architecture

### Board modes and source of truth

| Mode | Source of truth | Intended behavior |
|---|---|---|
| Standalone | Add-in project data and element assignments | Preserve the current behavior, without modifying native electrical circuits. |
| Linked | A native Revit panel and its electrical systems for panel name, circuit membership and native positions; add-in data for the chassis overlay and other agreed metadata | Show native circuits and connected elements in the existing tree. Changes that affect native data go through Revit transactions. |

Store stable Revit panel and circuit identifiers, such as their `UniqueId`,
alongside the add-in's existing identifiers. Treat missing, deleted, copied or
changed native elements as explicit reconciliation cases, not as permission
to silently attach a different element. A one-board-to-one-panel link is the
initial proposed model; confirm how duplicate links and panel renames should
be handled.

New persisted fields must remain compatible with existing projects. Follow the
optional-field validation pattern in
[ProjectDataSerializer](./Storage/ProjectDataSerializer.cs), with migration and
round-trip tests. Do not silently rewrite old standalone assignments.

**Proposed consistency rule:** an element should not simultaneously have an
independent standalone assignment and a conflicting native circuit assignment.
Detect existing conflicts and offer a preview or merge rather than changing
them automatically. Confirm this rule and the behavior of cross-mode moves
before enforcing it, especially for existing projects.

### Native UI versus the add-in

Review the native System Browser for navigating panels, circuits, connected and
unassigned elements. Review panel schedules for slot moves, spare/space
handling, locks and load-balancing tools. Verify which operations each Revit
version and schedule type actually supports.

The add-in's proposed value is the chassis overlay, configurable
demarcations, custom naming, cross-board search, bulk operations and a
reconciliation preview. Reuse native behavior where possible rather than
building a second independent electrical model.

### Candidate field mapping

| Add-in concept | Proposed native mapping | Verification or decision needed |
|---|---|---|
| Board and DB name | Electrical equipment panel and its Panel Name | Initial import, duplicates, rename synchronization and missing panels. |
| Circuit and connected elements | Native `ElectricalSystem` and its connected elements | Connector compatibility, membership and identity. |
| Chassis | Add-in-defined contiguous range of panel positions | Mapping for each supported panel schedule layout. |
| Position and pole count | Native panel slot and circuit poles | Slot coordinates, multi-pole groups and phase constraints. |
| Description | Load Name | Confirm the mapping and how differing values are reconciled. |
| Breaker size | Native circuit rating | Confirm parameter, units, writability and valid values. |
| Protection, earth leakage, trip curve and trip setting | Proposed shared parameters on Electrical Circuits | Parameter definitions, bindings, units and schedule visibility. |
| Spare and space | Native panel schedule spare/space where supported | Native semantics and API behavior need testing. |
| Add-in circuit label | Proposed shared text parameter used by a circuit naming scheme | Whether native Circuit Number, tags and schedules show the desired value. |

Do not treat these mappings as write-ready until the Phase 0 tests pass.

## Chassis layout and capacity

Chassis are proposed as consecutive blocks within one native panel. For the
Lighting 30 / Power 30 example, the intended layout is positions 1-30 and
31-60, respectively, and the panel must have sufficient capacity before any
native slot moves are attempted.

The current add-in uses `PoleIndex = row * 2 + side`, and its multi-pole
positions advance by two on the same side. A possible mapping for a
two-column schedule is:

```text
native position = 1 + sum(previous chassis pole counts) + local PoleIndex
```

This is an **unverified hypothesis**, not a universal Revit slot mapping.
Native schedule row/column coordinates, numbering, panel configuration,
phases, locked slots and multi-pole groups may require a different mapping.
Circuit Number is a label, not necessarily the same thing as a slot index.

Check whether the panel's **Max #1 Pole Breakers** parameter can be read and
changed, whether it is instance- or type-controlled, and whether the panel
schedule template imposes a different limit. Preview any capacity increase and
refuse a move that cannot fit or cannot be made safely.

## Circuit numbering and electrical settings

The proposed numbering approach is:

1. Define a shared text parameter on Electrical Circuits, for example
   `DBCM Circuit Label`.
2. Create or reuse an appropriate Revit circuit naming scheme that uses the
   parameter.
3. Apply the scheme to a linked panel, with explicit user approval.
4. Write the add-in's configured label to each native circuit when its layout
   or naming settings change, so the native Circuit Number can reflect it.

Verify scheme creation, parameter binding, available API versions, duplicate
labels, permitted characters and the resulting behavior in schedules and tags.
Preserve or explicitly restore the prior panel scheme when appropriate; do not
change project-wide or other panels' settings without showing the impact.

Rather than recreating all MEP Electrical Settings, consider an integration
settings page for the shared parameters, naming scheme, schedule template and
sorting rules, with access to Revit's native settings where supported.
Confirm what the API can inspect, modify or open in each supported version.

## Chassis demarcation and sorting

Configure an ordered set of rules per chassis:

- Connected-element category, such as Lighting Fixtures or Electrical
  Fixtures.
- Revit load classification.
- A selected parameter and value, such as `Circuit Type = Power`.
- Manual placement only.

The proposed policy is first matching rule wins, with a configurable fallback
chassis. Preserve native order where possible, and show any planned movement
before applying it. If elements on one native circuit match different
chassis, or a circuit does not fit, flag it for a user decision rather than
splitting or moving it automatically.

Confirm rule priority, fallback behavior, empty classifications and what
happens when a rule or chassis later changes. Rules and mappings should be
saved with the project so the result is consistent for project users.

## Link and merge workflow

Use one preview-driven reconciliation workflow for:

1. Creating a new add-in board from an existing panel.
2. Linking an existing add-in board to a populated panel.
3. Promoting a standalone board to a selected, initially empty panel.

### Match and classify without writing

Compare native circuits and elements with the board's add-in circuits and
assignments. Pairing circuits by their shared elements was suggested as a
starting point, but ambiguous matches, split circuits, multiple connectors and
cross-panel assignments must be exposed for review rather than silently
resolved.

| Case | Proposed handling |
|---|---|
| Matched on both sides | Keep the agreed native membership and import or retain the overlay. |
| Add-in only | Propose adding the element to the paired native circuit, or creating a circuit if necessary and supported. |
| Revit only | Import it into the paired add-in circuit, or create an overlay circuit and place it according to the rules. |
| Different circuit on this panel | Let the user choose the native or add-in assignment. |
| Native circuit on another panel | Show the affected panel and require an explicit choice before moving anything. |
| Cannot be circuited | Explain the connector, voltage, pole, distribution-system or other incompatibility. Offer unassignment from the linked board or cancel the link; never force an invalid circuit. |

Offer a default conflict policy, **Revit wins** or **add-in wins**, with
per-element or per-circuit overrides. For circuit details, propose using the
populated value if only one side has one and the chosen conflict policy when
both differ. Details with no native equivalent need an explicit storage or
shared-parameter policy. Preview the effects on other linked panels as well.

An add-in spare with details may need to become a native spare; an empty
space may remain empty. Verify the native representation and whether all
details can be preserved.

### Placement and preview

The proposed placement policy keeps accepted add-in positions where possible
and sorts imported native circuits by the configured rules, preserving native
order when possible. The existing `BulkCircuitOperations.PlanMoves` may help
plan candidate positions, but native schedule constraints must also be
checked.

Before any native changes, show:

- Counts for matches, imports, proposed additions, conflicts, incompatible
  elements, native circuit creation/removal and capacity changes.
- Per-circuit source and destination chassis/position, native slot, Circuit
  Number, Load Name and rating, including before/after values.
- Per-element source, destination, conflict decision and any rejection
  reason.
- Other affected panels and any replacement of occupied positions or spares.
- Unresolved conflicts, insufficient capacity and other blocking issues.

Consider CSV export for review. Apply must remain unavailable until required
decisions and validation are complete. Cancel must leave native data and
standalone assignments unchanged.

### Apply and rollback

After rechecking that the preview is current and the user has permission to
edit all affected elements, propose one Revit `TransactionGroup` and one
user-visible Revit Undo operation:

1. Increase panel capacity if required and supported.
2. Remove elements from circuits they are leaving.
3. Add elements to paired circuits or create and assign native circuits.
4. Create native spares where required and supported.
5. Move native slots, using temporary free positions if needed to avoid
   collisions and respecting groups and locked positions.
6. Write approved names, ratings, labels and extra parameters.
7. Save the panel/circuit links and overlay data; clear only the add-in
   element-assignment storage that is no longer authoritative.
8. Refresh all affected linked boards and report the outcome.

The exact order and atomicity must be proven in Revit, including whether
Extensible Storage and all native changes participate in the same undo and
rollback. If a step fails, roll back and retain the prior standalone state.
Never delete model elements as a side effect of unassigning them.

## Existing actions on linked boards

These are proposed behaviors requiring validation and, where noted, a product
decision:

| Existing action | Proposed linked-board behavior |
|---|---|
| Assign or move elements; create a circuit | Change native circuit membership or create a native circuit, subject to connector and panel validation. |
| Move a circuit | Move its native slot within a panel; for another panel, change panel and then place the circuit. |
| Delete a circuit | Delete or remove the native circuit as appropriate and unassign its elements, after confirmation. |
| Convert to three-phase | Do not pretend to change connector-driven poles; consider reserving a three-pole spare if Revit supports it. |
| Add, edit or reorder chassis | Preview the revised slot mapping, capacity and any affected circuits before applying. |
| Duplicate a board | Decide whether to link a new panel copy without circuits or create a standalone copy. |
| Clear or delete a linked board | Proposed safe default: unlink and retain a standalone snapshot, rather than deleting the native panel. Deleting the panel would be a separate explicit action. Confirm how this interacts with the existing Clear Selected/Clear All behavior. |
| Undo | Use Revit Undo for native edits; coordinate it with the add-in's standalone history, reload and refresh. |

Standalone actions must not accidentally mutate native circuits. Cross-mode
moves need an explicit reconciliation workflow rather than separate,
conflicting assignments.

## Phased roadmap

### Phase 0: Revit feasibility spike

Use disposable projects in Revit 2023 and 2027, then check other supported
versions as necessary. Record actual behavior and limitations before designing
write-through features:

- Panel capacity and schedule-template limits; locked slots; two-column and
  other schedule layouts; phase and multi-pole placement.
- Whether a schedule view is required for slot operations and what creating
  one changes in the project.
- Native circuit creation, panel selection, add/remove elements, invalid
  connectors and what happens when the last element is removed.
- Spare/space creation and representation; moves through occupied or temporary
  slots.
- Shared parameter binding, circuit naming scheme creation, panel scheme
  selection, duplicate labels, tags and schedule results.
- Native rating and Load Name access, parameter units and writability.
- Worksharing ownership, transaction rollback, Revit Undo, external changes
  and concurrent edits.
- What System Browser and panel schedules provide, and what varies by version.

Candidate APIs from the earlier review include `ElectricalSystem.Create`,
`SelectPanel`, `AddToCircuit`, `RemoveFromCircuit`, `CircuitNamingScheme.Create`,
and `PanelScheduleView` slot/spare operations. Their availability and exact
behavior remain to be verified, not assumed.

### Phase 1: Read-only native import and overlay

Discover existing panels, select one, configure chassis ranges, and display
its native circuits, positions, Circuit Numbers and elements in the tree.
Report unsupported layouts, missing identities and conflicts. Keep native
circuits, names, slots and project electrical settings unchanged.

Here, **read-only** means no mutation of the native panel or its circuits.
Persisting an add-in link or overlay still requires a project storage
transaction and should be disclosed to the user. Test cancel and reload,
native changes made outside the add-in, and existing standalone projects.

### Phase 2: Opt-in native numbering

Add the shared parameter and naming scheme workflow, preview changes, and
write labels only after explicit approval. Verify native Circuit Number,
tags and schedules, compatibility with existing schemes and undo/rollback.

### Phase 3: Rules, placement and merge

Add user-defined chassis rules, capacity checks, matching and conflict
decisions, a before/after preview, and transactional application. This phase
also covers promoting a standalone board to an empty panel. Verify the
Lighting 30 / Power 30 example and conflicts involving other panels.

### Phase 4: Write-through linked-board actions

Adapt individual and bulk moves, assignments, circuit creation/deletion,
chassis edits and other supported actions to native Revit behavior. Complete
the linked/standalone consistency policy and Revit Undo integration. Unsupported
operations must be clearly disabled or reported, not simulated as successful.

## Validation expectations for implementation

- Add Revit-free tests for mapping, rule precedence, identity, conflicts,
  placement, migration and preserving standalone behavior.
- Build the affected supported configurations. The existing baseline commands
  include:

  ```sh
  dotnet test tests/Circuiting.Tests/Circuiting.Tests.csproj -c Release --verbosity minimal
  dotnet build DBCircuitManager.csproj -c Debug.R23 \
    -p:EnableWindowsTargeting=true -p:DeployAddin=false -p:LaunchRevit=false
  dotnet build DBCircuitManager.csproj -c Release.R27 \
    -p:EnableWindowsTargeting=true -p:DeployAddin=false -p:LaunchRevit=false
  ```

- Exercise real Revit projects for the API, transactions, worksharing,
  schedules, numbering and WPF interactions. Domain tests and successful
  builds do not verify those behaviors.
- Test cancel, failure rollback, Undo/Redo, external edits, deleted panels,
  old standalone projects, mixed linked/standalone projects and reopening the
  saved project.

## References for future sessions

- [Circuit Hub in the user's pyRevit repository](https://github.com/Map-Sticks/Personal/tree/main/CustomPyRevit.extension/Custom%20Tools.tab/Electrical.panel/CircuitHub.pushbutton).
  The earlier discussion referred to its `CONTEXT.md`; its `script.py` was not
  reviewed. Check repository access and the actual implementation before
  relying on its behavior.
- [ETCS repository](https://github.com/Map-Sticks/ETCS): the earlier review
  identified a circuit-numbering command using a custom parameter and naming
  scheme. Verify the current implementation and its project assumptions.
- [README](./README.md): current supported versions, standalone behavior and
  manual validation checklist.
