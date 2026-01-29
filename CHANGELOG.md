# Changelog
All notable changes to this package will be documented in this file.

## [1.0.0] - 2026-01-29

### Added
- **EditorWindow** accessible via `Tools > Namespace Path`
- **Folder-based namespace suggestions** - Automatically generates namespace based on folder structure
- **Customizable prefix** - Add your project's root namespace as prefix
- **Batch processing** - Select multiple scripts and apply changes at once
- **Affected files preview** - See which files will be modified before applying changes
- **Smart reference updates** - Updates `using` statements and fully-qualified references
- **Type-aware detection** - Only updates files that actually use the types being moved
- **Unused using cleanup** - Removes `using` statements that are no longer needed
- **Duplicate using removal** - Automatically removes duplicate `using` statements
- **Conflict detection** - Warns when namespace segment matches a type name (e.g., `Utils.Utils`)
- **Filters** - Filter by "needs change", "no namespace", or search by name
- **Clickable file links** - Click on file names to ping them in the Project window
