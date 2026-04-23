# Error Reference

Every `brainyz` error surfaces a code of the form `BZ_<CATEGORY>_<NAME>`. The code is stable: it does not change between releases without a migration note. CLI output links here with a `#<code-lowercased>` anchor.

Every entry follows the same shape:
- **Meaning:** what the error actually means
- **Common causes:** usual culprits
- **Recovery:** what to do next

---

## Export

### BZ_EXPORT_DB_NOT_FOUND
- **Meaning:** the local brainyz DB was not found at the resolved path.
- **Common causes:** brainyz not yet initialized (`brainz init`), or `--db` pointed at a missing file.
- **Recovery:** run `brainz init`, or pass `--db <path>` pointing to a valid file.

### BZ_EXPORT_WRITE_FAILED
- **Meaning:** the JSONL export could not be written to the requested path.
- **Common causes:** path not writable, parent directory missing, filesystem read-only.
- **Recovery:** check the `--to` path and its parent directory's write permissions.

### BZ_EXPORT_DISK_FULL
- **Meaning:** disk ran out of space while writing the export.
- **Recovery:** free space and retry; partial output is not retained.

### BZ_EXPORT_PROJECT_NOT_FOUND
- **Meaning:** `--project <id>` does not match any project in the DB.
- **Recovery:** list projects with `brainz list projects` and retry with a valid ID.

## Import

### BZ_IMPORT_FILE_NOT_FOUND
- **Meaning:** the `--from` JSONL file does not exist.
- **Recovery:** check the path and retry.

### BZ_IMPORT_MANIFEST_MISSING
- **Meaning:** the first line of the JSONL is missing or is not a `{"type":"manifest",...}` record.
- **Recovery:** confirm the file was produced by `brainz export` (not hand-edited or truncated).

### BZ_IMPORT_MANIFEST_INVALID_JSON
- **Meaning:** the first line is not valid JSON, or a required manifest field is missing / wrong type.
- **Recovery:** inspect with `head -n 1 <file> | jq .`. Re-export if the file is corrupt.

### BZ_IMPORT_FORMAT_VERSION_NEWER
- **Meaning:** the export was produced by a newer brainyz (manifest `format_version > 1`).
- **Recovery:** upgrade brainyz to the version that wrote the export, or re-export from a matching installation.

### BZ_IMPORT_SCHEMA_MISMATCH
- **Meaning:** the manifest's `schema_version` does not match the local DB's schema version.
- **Recovery:** automatic migration is not yet supported; re-export from an installation matching the local schema version.

### BZ_IMPORT_RECORD_MALFORMED
- **Meaning:** a record past the manifest is not valid JSON or is missing a required field. Error message includes the line number.
- **Recovery:** inspect that line with `sed -n '<N>p' <file>`; usually a hand-edit mistake. Regenerate the export.

### BZ_IMPORT_FK_VIOLATION
- **Meaning:** a record references a parent row that is not in the file or in the local DB (e.g., a decision whose project does not exist).
- **Recovery:** confirm the JSONL was produced with `brainz export` (dependency ordering is guaranteed by §3.2). If hand-crafted, reorder records.

### BZ_IMPORT_SAFETY_BACKUP_FAILED
- **Meaning:** could not write the pre-import safety backup next to the local DB.
- **Recovery:** check disk space and that the DB's directory is writable; or pass `--no-backup` if you have other backups and accept the risk.

### BZ_IMPORT_TRANSACTION_FAILED
- **Meaning:** the INSERT transaction rolled back. The local DB is unchanged.
- **Recovery:** inspect the error message for the specific SQLite/LibSQL cause (often disk, unique, or CHECK violation).

### BZ_IMPORT_DISK_FULL
- **Meaning:** disk ran out of space during the import transaction. The rollback has already restored the pre-import state.
- **Recovery:** free space and retry.

## Backup

### BZ_BACKUP_DB_NOT_FOUND
- **Meaning:** the local brainyz DB was not found at the resolved path.
- **Recovery:** run `brainz init` or pass `--db <path>` pointing to a valid file.

### BZ_BACKUP_VACUUM_FAILED
- **Meaning:** `VACUUM INTO` failed while taking the atomic snapshot.
- **Common causes:** DB corruption, another process holding an exclusive write lock.
- **Recovery:** run `brainz reindex` after the other process releases the DB; consider a full integrity check (`PRAGMA integrity_check`).

### BZ_BACKUP_ZIP_WRITE_FAILED
- **Meaning:** could not write the backup ZIP to the `--to` path.
- **Recovery:** check path and its parent directory.

### BZ_BACKUP_DISK_FULL
- **Meaning:** disk ran out of space while writing the backup.
- **Recovery:** free space and retry.

## Restore

### BZ_RESTORE_ZIP_NOT_FOUND
- **Meaning:** the `--from` ZIP file does not exist.
- **Recovery:** check the path.

### BZ_RESTORE_ZIP_CORRUPT
- **Meaning:** the file at `--from` is not a valid ZIP archive.
- **Recovery:** confirm the download/copy was complete; try `unzip -l <file>` to inspect.

### BZ_RESTORE_ZIP_MISSING_MANIFEST
- **Meaning:** the ZIP does not contain `manifest.json`.
- **Recovery:** the archive was not produced by `brainz backup` — get the original.

### BZ_RESTORE_ZIP_MISSING_DB
- **Meaning:** the ZIP does not contain `brainyz.db`.
- **Recovery:** the archive was not produced by `brainz backup` — get the original.

### BZ_RESTORE_FORMAT_VERSION_NEWER
- **Meaning:** the backup was produced by a newer brainyz (`manifest.format_version > 1`).
- **Recovery:** upgrade brainyz to the version that wrote the backup.

### BZ_RESTORE_SCHEMA_MISMATCH
- **Meaning:** the backup's `schema_version` does not match the local DB's.
- **Recovery:** automatic migration is not yet supported; restore on a matching brainyz version.

### BZ_RESTORE_SAFETY_BACKUP_FAILED
- **Meaning:** could not write the pre-restore safety backup next to the local DB.
- **Recovery:** check disk space and that the DB's directory is writable; or pass `--no-backup` to skip (risky).

### BZ_RESTORE_DB_LOCKED
- **Meaning:** another process (typically `brainz mcp` or an external DB tool) is holding the target DB open.
- **Recovery:** close the other process and retry. To find the culprit on Linux: `lsof <db-path>`; on Windows, Resource Monitor's "Associated Handles" panel.

### BZ_RESTORE_SWAP_FAILED
- **Meaning:** could not rename the extracted DB over the target path.
- **Common causes:** target directory not writable, target file held by another process, cross-filesystem move (rare).
- **Recovery:** check permissions; ensure the extract temp directory is on the same filesystem as the target DB.

### BZ_RESTORE_DISK_FULL
- **Meaning:** disk ran out of space during extraction or the safety backup.
- **Recovery:** free space and retry.

### BZ_RESTORE_USER_DECLINED
- **Meaning:** the interactive confirmation prompt was answered with `n` or anything that is not `y`/`Y`.
- **Recovery:** re-run with `--force` to skip the prompt, or pipe `y` on stdin, or run in a non-TTY context.
