// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Errors;

namespace Brainyz.Cli;

/// <summary>
/// Renders a <see cref="BrainyzException"/> into the canonical
/// <c>Error [CODE]: ... \n  Tip: ... \n  See: ...</c> block described
/// in §7.3 of the v0.3 spec, and maps each code to its documented
/// exit code (§7.4).
/// </summary>
public static class ErrorFormatter
{
    private const string DocsBase =
        "https://github.com/FavitoX/brainyz/blob/master/docs/errors.md";

    public static string Format(BrainyzException ex)
    {
        var code = ex.Code.ToString();
        var anchor = code.ToLowerInvariant();
        var sb = new System.Text.StringBuilder();
        sb.Append("Error [").Append(code).Append("]: ").AppendLine(ex.Message);
        if (!string.IsNullOrEmpty(ex.Tip))
            sb.Append("  Tip: ").AppendLine(ex.Tip);
        sb.Append("  See: ").Append(DocsBase).Append('#').Append(anchor);
        return sb.ToString();
    }

    public static int ExitCodeFor(ErrorCode code) => code switch
    {
        ErrorCode.BZ_RESTORE_USER_DECLINED => 3,

        ErrorCode.BZ_EXPORT_DB_NOT_FOUND or
        ErrorCode.BZ_EXPORT_PROJECT_NOT_FOUND or
        ErrorCode.BZ_IMPORT_FILE_NOT_FOUND or
        ErrorCode.BZ_IMPORT_MANIFEST_MISSING or
        ErrorCode.BZ_IMPORT_MANIFEST_INVALID_JSON or
        ErrorCode.BZ_IMPORT_FORMAT_VERSION_NEWER or
        ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH or
        ErrorCode.BZ_IMPORT_RECORD_MALFORMED or
        ErrorCode.BZ_BACKUP_DB_NOT_FOUND or
        ErrorCode.BZ_RESTORE_ZIP_NOT_FOUND or
        ErrorCode.BZ_RESTORE_ZIP_CORRUPT or
        ErrorCode.BZ_RESTORE_ZIP_MISSING_MANIFEST or
        ErrorCode.BZ_RESTORE_ZIP_MISSING_DB or
        ErrorCode.BZ_RESTORE_FORMAT_VERSION_NEWER or
        ErrorCode.BZ_RESTORE_SCHEMA_MISMATCH => 2,

        _ => 1,
    };
}
