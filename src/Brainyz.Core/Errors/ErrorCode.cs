// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core.Errors;

/// <summary>
/// Stable, user-facing error codes emitted by every brainyz operation that
/// can fail. The enum name (e.g. <c>BZ_IMPORT_SCHEMA_MISMATCH</c>) is the
/// exact string printed in CLI output and the anchor in
/// <c>docs/errors.md</c>; never rename a member without updating both.
/// </summary>
public enum ErrorCode
{
    // Export
    BZ_EXPORT_DB_NOT_FOUND,
    BZ_EXPORT_WRITE_FAILED,
    BZ_EXPORT_DISK_FULL,
    BZ_EXPORT_PROJECT_NOT_FOUND,

    // Import
    BZ_IMPORT_FILE_NOT_FOUND,
    BZ_IMPORT_MANIFEST_MISSING,
    BZ_IMPORT_MANIFEST_INVALID_JSON,
    BZ_IMPORT_FORMAT_VERSION_NEWER,
    BZ_IMPORT_SCHEMA_MISMATCH,
    BZ_IMPORT_RECORD_MALFORMED,
    BZ_IMPORT_FK_VIOLATION,
    BZ_IMPORT_SAFETY_BACKUP_FAILED,
    BZ_IMPORT_TRANSACTION_FAILED,
    BZ_IMPORT_DISK_FULL,

    // Backup
    BZ_BACKUP_DB_NOT_FOUND,
    BZ_BACKUP_VACUUM_FAILED,
    BZ_BACKUP_ZIP_WRITE_FAILED,
    BZ_BACKUP_DISK_FULL,

    // Restore
    BZ_RESTORE_ZIP_NOT_FOUND,
    BZ_RESTORE_ZIP_CORRUPT,
    BZ_RESTORE_ZIP_MISSING_MANIFEST,
    BZ_RESTORE_ZIP_MISSING_DB,
    BZ_RESTORE_FORMAT_VERSION_NEWER,
    BZ_RESTORE_SCHEMA_MISMATCH,
    BZ_RESTORE_SAFETY_BACKUP_FAILED,
    BZ_RESTORE_DB_LOCKED,
    BZ_RESTORE_SWAP_FAILED,
    BZ_RESTORE_DISK_FULL,
    BZ_RESTORE_USER_DECLINED,
}
