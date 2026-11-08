# Circuiting - DB Circuit Manager

Circuiting is a Revit add-in with a dockable WPF panel for managing
**boards → chassis → circuits → fixtures**, independently of Revit's native
electrical circuits. Circuit data is stored in the project using Revit
Extensible Storage.

## Supported builds

| Revit | Configurations | Target framework |
|---|---|---|
| 2023 | `Debug.R23`, `Release.R23` | .NET Framework 4.8 |
| 2024 | `Debug.R24`, `Release.R24` | .NET Framework 4.8 |
| 2025 | `Debug.R25`, `Release.R25` | .NET 8 for Windows |
| 2026 | `Debug.R26`, `Release.R26` | .NET 8 for Windows |
| 2027 | `Debug.R27`, `Release.R27` | .NET 10 for Windows |

Build with the .NET 10 SDK and the .NET Framework 4.8 targeting pack. Revit API
references are restored through the Nice3point NuGet packages; running the
add-in requires the corresponding Revit installation on Windows.

```sh
# Select the configuration matching the Revit version.
dotnet build DBCircuitManager.csproj -c Debug.R23 \
  -p:EnableWindowsTargeting=true -p:DeployAddin=false -p:LaunchRevit=false

dotnet test tests/Circuiting.Tests/Circuiting.Tests.csproj
```

The deployment and launch properties are disabled in these commands and in
the [CI workflow](.github/workflows/ci.yml). CI builds every Debug and Release
configuration and runs the cross-platform domain tests. Those tests use fake
fixture stores; they do not run Revit, exercise its transactions, or verify WPF
appearance. Build and restore intermediates are isolated by configuration so
switching Revit targets does not reuse another version's assets; CI also
checks switching between Revit 2023 and 2027.

## Using the panel

Open a project, then use **Circuit Manager → Open Circuits**. Add a board and
edit its chassis and circuit rows using the tree's context menus or a row
double-click. Multi-pole circuits occupy subsequent positions on the same
side. A new or resized multi-pole circuit must start on the first phase and fit
without overlapping another circuit. Convert multi-pole circuits to single
poles before changing their chassis to single phase.
Legacy chassis with one or two phase labels retain that pattern when edited;
the chassis dialog offers an explicit option to remove it.

- **+ Add Board** sets up the whole board in one dialog: name (must be unique),
  supply, number of chassis (1-20), and a quick setup of poles, circuit prefix
  and separator applied to every chassis with consecutive numbering. Edit any
  chassis (name, start number, phase labels) by double-clicking it in the list,
  and reorder with ▲/▼. **Base on** copies the supply and chassis setup of an
  existing board; circuits are never copied.
- **Edit Board...** in a board's context menu opens the same dialog for an
  existing board: rename it (names stay unique), change supply, add, remove,
  reorder or edit chassis, and apply quick setup. Existing circuits keep their
  positions, details and fixtures and are relabelled. Saving is refused if a
  circuit would no longer fit, a three-phase chassis remains on a single-phase
  board, or a multi-pole circuit would end up on a single-phase chassis or off
  its first phase. Removing chassis that hold circuits asks for confirmation and
  deletes their circuits and fixture assignments on save. Cancel discards all
  changes; Save is a single Undo step.
- **Duplicate Board...** in a board's context menu creates 1-50 copies of its
  supply and chassis setup without circuits or fixture assignments. Names are
  numbered from a prefix and start number (e.g. `DB-L1` suggests `DB-L2`,
  `DB-L3`, ...; leading zeros set the width) and names already in use are
  skipped. Copies are inserted after the source board and support Undo.
- **Search** (below the toolbar, or Ctrl+F) filters the tree as you type. The
  filter list limits it to **DBs**, **Chassis**, or **Circuits**; **All** searches
  every type. Circuits match on label, number, description, breaker, protection
  and earth leakage; every word must match (e.g. `16A RCBO`). A matching DB or
  chassis shows all its contents; otherwise only rows with matching circuits are
  shown, with every pole of a multi-pole circuit. Matches are highlighted and
  counted in the status bar. Enter applies immediately, Esc or ✕ clears and
  restores the previous expand/collapse state. Adding a board clears the search.
- A position is **occupied** only when model elements are assigned to it.
  Unoccupied positions are greyed out: **Space** when nothing is saved, or
  **Spare — <description>** (with breaker and protection) when details are
  saved. Adding a description, breaker or protection keeps a position spare.
- **Convert to Three-Phase Circuit** in a row's context menu (three-phase
  chassis only) merges the R, W and B positions on that side into one
  three-pole circuit; right-click any of the three. The circuit keeps the
  details of the position with fixtures, or else the right-clicked position;
  the other spares are deleted after confirmation. It is refused if more than
  one position has fixtures or the group would run past the chassis. Creating a
  multi-pole circuit in the row editor also replaces spares on its poles.
- **Move Left/Right** in a circuit row's context menu opens a destination picker:
  choose a **board** (filter by board or chassis name), then a **chassis**, then
  the **target position**. Boards and chassis show how many positions fit the
  circuit, and the current board and chassis are preselected. The position list
  shows each position's current contents and status (Available, Replaces spare,
  Unavailable with the reason as a tooltip), can be filtered by label, number,
  side or contents, and hides unavailable positions unless **Available only** is
  cleared. Double-click a position or click **Move**.
  Alternatively, hold **Alt** and drag a circuit with the left mouse button onto
  the desired left or right position. Collapsed boards and chassis expand when
  dragged over. Moves work across chassis and boards and preserve circuit identity,
  details, and fixtures. Positions with fixtures assigned are never overwritten
  or swapped; moving onto a spare replaces it after confirming that its saved
  details will be deleted.
  All poles must fit; multi-pole destinations must start on the first phase of
  a three-phase chassis. Dragging a consumed pole moves its parent circuit.
  Moves update fixture references in the save transaction and support panel
  Undo/Redo. Esc cancels a drag; Cancel closes the destination picker unchanged.
- **Assign Fixtures** highlights eligible lighting fixtures, electrical
  fixtures, and electrical equipment in the active model view. Select
  elements and click **Finish**; Esc cancels the selection. Existing
  assignments can be moved or skipped. Fixtures with orphaned or conflicting
  assignment data must be reconciled with **Validate Model** first.
- **Select in Model** selects existing circuit fixtures. **Pick Fixtures to
  Remove** removes only the elements selected in that pick session.
- Deleting a circuit can unassign its fixtures or move them to another
  existing circuit. Deleting a chassis or board, clearing a chassis, and
  **Clear All** unassign their referenced fixtures. **Clear All** requires two
  confirmations.
- **Validate Model** previews fixture repairs before applying them.
  **Resolution Rules** configures copied- and deleted-fixture handling.
  The AUTO/MANUAL toggle controls automatic reconciliation.
- Panel Undo/Redo stores up to 50 project snapshots per document. Changes are
  saved as Revit transactions, including corresponding fixture assignments.
  Cancelled picks do not create an undo step.

The panel is disabled while a Revit request is pending. A request is tied to
its originating document and expected project snapshot; a document switch,
external change, or failed transaction is reported instead of silently
overwriting newer data.

## Identity, storage, and migration

Circuit GUIDs identify circuits. `PoleIndex` identifies their physical position
within a chassis; display labels and numbers are recomputed from that layout
and need not be unique. Changing a prefix, phase sequence, start number, or
chassis layout updates fixture `CircuitReference` values in the same
transaction as the project data.

| Extensible Storage schema | Location | Use |
|---|---|---|
| `DBCircuitManager_Project` | `ProjectInformation` | Versioned JSON containing settings and the complete board tree; source of truth |
| `DBCircuitManager_Fixture` | Each assigned fixture | Fixture, board, chassis and circuit GUIDs, plus the display reference |
| `DBCircuitManager_Board` | Defined for panel elements | Reserved; not currently used by the panel |

The existing schema GUIDs are unchanged. Fixture `UniqueId` is used when
available. A legacy numeric element ID is used only when its fixture entity
confirms the stored fixture GUID; ambiguous or conflicting assignments are not
guessed.

Legacy project data stored as a JSON board array is migrated in memory to
schema version 1. Its circuit labels, and circuit numbers where necessary,
must identify unique pole positions. Ambiguous layouts, invalid JSON, duplicate
identities, and unsupported schema versions are reported and are not
overwritten. Migration is persisted on a subsequent successful save.
There is currently no repair interface for an ambiguous legacy layout.

**Back up the model before upgrading or migrating.** Older versions of this
add-in may not understand the versioned JSON format; retain the backup and
previous add-in if rollback is needed. Errors are shown to the user and
written to `System.Diagnostics.Trace`; the add-in does not configure a
persistent log file.

## Reconciliation and undo

In AUTO mode, after a committed transaction in the currently tracked document,
the add-in queues a separate Revit transaction to process recognizable copied
fixtures and known deleted fixtures. Copies can be cleared or assigned to the
same circuit with a new fixture GUID; optional notifications and trace logging
are available. Automatic cleanup is deliberately limited to added copies whose
stored GUIDs match their source and deleted fixtures whose stored unique and
numeric IDs can be verified. Legacy, orphaned, conflicting, or otherwise
ambiguous cases require explicit review.

**Validate Model** scans eligible fixture categories, previews missing records,
orphaned or copied assignments, mismatched fixture entities, and legacy ID
updates, then applies the confirmed repair using project JSON as the
authoritative source. Invalid fixture metadata or ambiguous legacy identity
can block validation and requires manual recovery. Validation also applies the
configured copied-fixture rule in MANUAL mode.

Automatic repairs and validation are separate Revit transactions and clear
panel history when they change data. Use **Revit Undo** to reverse them.
Native Revit Undo/Redo reloads the project state but does not automatically
replay copy/delete reconciliation; run **Validate Model** when needed. A
missing referenced fixture is reported rather than silently reassigned.

Fixture highlighting uses a rolled-back transaction group so pre-existing view
overrides can be restored. Assigning requires a view that supports element
iteration and graphics overrides. Verify picking, rollback, and failure
handling in the actual Revit versions used by the project.

## Project structure

| Path | Purpose |
|---|---|
| `Application.cs`, `Commands/` | Ribbon, document events, queued external Revit requests |
| `Models/`, `Services/` | Project models, layout/identity rules, fixture synchronization and validation |
| `Storage/` | Extensible Storage schemas, versioned JSON, Revit fixture adapter |
| `ViewModels/` | Tree projection, commands, field options, and per-document history |
| `UI/DockablePanel/`, `UI/Dialogs/` | XAML views, dialog handling, and panel action coordination |
| `tests/Circuiting.Tests/` | Domain and view-model regression tests |

## Limitations and future work

Boards are logical project records, not linked to physical panel elements.
Consequently copied/deleted **physical boards** cannot yet be identified or
resolved automatically; the board-level schema and the existing
`CopiedBoardAction` setting are not used. No physical-board association is
inferred from fixture data.

Unresolved work outside the maintenance scope includes load and phase
balancing, schedule or CSV export/import, parameter/tag write-back, native electrical-system integration, localization, and a
persistent logging sink. The circuit options remain IEC-oriented.

## Manual Revit validation checklist

Run on a disposable copy of a model in each supported Revit version:

1. Back up the model. Open legacy and current data, verify migration and
   reopening, and confirm invalid or ambiguous data is reported without being
   replaced.
2. Exercise board/chassis/circuit dialogs, multi-pole positions, tree context
   menus, keyboard navigation, expansion state, and bulk edit/undo.
3. Assign fixtures, edit labels and phase continuation, then inspect project
   and fixture Extensible Storage with a Revit inspection tool to confirm
   identities and references stay consistent.
4. Test circuit, chassis, board and Clear All deletion; reassign fixtures;
   undo and redo each operation. Check both JSON and fixture entities.
5. Test Finish and Esc during selection, pre-existing graphics overrides,
   exceptions/cancellation, and unsupported views. Confirm original overrides
   are restored and no transaction is left open.
6. Copy and delete fixtures under each resolution rule and in MANUAL mode;
   preview validation, cancel it, apply it, and use Revit Undo/Redo. Include
   orphaned, missing, legacy, and conflicting assignments.
7. Switch, close, and reopen multiple projects, including while a request is
   pending. Confirm no request or history is applied to the wrong document.
8. Test read-only/workshared or otherwise failing writes and confirm errors
   are visible and model data is not partially changed.
9. Search each scope across several DBs: type in the search box, Ctrl+F,
   Enter, Esc and ✕. Edit, move and assign from filtered rows, switch
   documents while searching, then clear and confirm expansion is restored.
10. Add boards with several chassis, quick setup, per-chassis edits, both
    supplies, and **Base on** an existing board. Duplicate boards with several
    copies and confirm unique names, no copied circuits, and Undo/Redo.
11. Edit a board with circuits and fixtures: rename, reorder, add and remove
    chassis, change poles/prefix/supply, and confirm labels and fixture
    references update, invalid layouts are refused, and Undo restores them.
12. Add details without fixtures and confirm the position shows greyed
    **Spare — ...**; clear them and confirm **Space**. Convert to three-phase
    from each of R, W and B (spaces, spares, and one position with fixtures),
    confirm two fixture positions are refused, and move a circuit onto a spare
    (picker and Alt-drag) confirming the replacement prompt and Undo.
13. Move circuits with the picker across many boards: filter boards, switch
    chassis, filter positions, toggle **Available only**, and confirm
    unavailable positions cannot be chosen and the move lands where selected.
