// Copyright 2026 Favio Andres Leyva
// SPDX-License-Identifier: Apache-2.0

namespace Brainyz.Core;

/// <summary>
/// ULID generation for all entity IDs.
/// 26 chars, Crockford base32, time-sortable, safe for multi-device sync. See D-015.
/// </summary>
public static class Ids
{
    public static string NewUlid() => Ulid.NewUlid().ToString();
}
