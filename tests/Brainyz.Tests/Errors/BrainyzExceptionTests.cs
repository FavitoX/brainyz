// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

using Brainyz.Core.Errors;

namespace Brainyz.Tests.Errors;

public class BrainyzExceptionTests
{
    [Fact]
    public void Carries_code_message_and_optional_tip()
    {
        var ex = new BrainyzException(
            ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH,
            "schema 2 does not match local schema 1",
            tip: "re-export from a matching installation");

        Assert.Equal(ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH, ex.Code);
        Assert.Equal("schema 2 does not match local schema 1", ex.Message);
        Assert.Equal("re-export from a matching installation", ex.Tip);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Preserves_inner_exception()
    {
        var inner = new IOException("disk full");
        var ex = new BrainyzException(
            ErrorCode.BZ_BACKUP_DISK_FULL,
            "cannot write backup",
            inner: inner);

        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void Code_names_are_stable_strings_for_user_facing_logs()
    {
        Assert.Equal("BZ_EXPORT_WRITE_FAILED", ErrorCode.BZ_EXPORT_WRITE_FAILED.ToString());
        Assert.Equal("BZ_IMPORT_SCHEMA_MISMATCH", ErrorCode.BZ_IMPORT_SCHEMA_MISMATCH.ToString());
        Assert.Equal("BZ_RESTORE_DB_LOCKED", ErrorCode.BZ_RESTORE_DB_LOCKED.ToString());
    }
}
