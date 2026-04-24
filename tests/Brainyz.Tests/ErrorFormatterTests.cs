// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Cli;
using Brainyz.Core.Errors;

namespace Brainyz.Tests;

public class ErrorFormatterTests
{
    [Fact]
    public void Prints_code_bracketed_with_message()
    {
        var ex = new BrainyzException(
            ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH,
            "schema 2 does not match local schema 1");

        var text = ErrorFormatter.Format(ex);

        Assert.Contains("Error [BZ_IMPORT_SCHEMA_MISMATCH]:", text);
        Assert.Contains("schema 2 does not match local schema 1", text);
    }

    [Fact]
    public void Appends_tip_line_when_present()
    {
        var ex = new BrainyzException(
            ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH,
            "schema 2 does not match local schema 1",
            tip: "re-export from a matching installation");

        var text = ErrorFormatter.Format(ex);

        Assert.Contains("Tip: re-export from a matching installation", text);
    }

    [Fact]
    public void Appends_docs_pointer_using_lowercased_anchor()
    {
        var ex = new BrainyzException(
            ErrorCode.BZ_RESTORE_DB_LOCKED, "DB locked");

        var text = ErrorFormatter.Format(ex);

        Assert.Contains(
            "See: https://github.com/FavitoX/brainyz/blob/master/docs/errors.md#bz_restore_db_locked",
            text);
    }

    [Theory]
    [InlineData(ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH, 2)]
    [InlineData(ErrorCode.BZ_IMPORT_MANIFEST_INVALID_JSON, 2)]
    [InlineData(ErrorCode.BZ_IMPORT_FILE_NOT_FOUND, 2)]
    [InlineData(ErrorCode.BZ_RESTORE_ZIP_MISSING_DB, 2)]
    [InlineData(ErrorCode.BZ_IMPORT_TRANSACTION_FAILED, 1)]
    [InlineData(ErrorCode.BZ_BACKUP_DISK_FULL, 1)]
    [InlineData(ErrorCode.BZ_RESTORE_USER_DECLINED, 3)]
    public void Maps_code_to_documented_exit_code(ErrorCode code, int expected)
    {
        Assert.Equal(expected, ErrorFormatter.ExitCodeFor(code));
    }
}
