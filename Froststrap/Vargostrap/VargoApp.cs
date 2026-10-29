// SPDX-FileCopyrightText: 2026 Vargostrap
//
// SPDX-License-Identifier: MPL-2.0

using Froststrap.Vargostrap.FastFlags;
using Froststrap.Vargostrap.Models;

namespace Froststrap.Vargostrap
{
    /// <summary>
    /// Vargostrap's counterpart to the singletons on <see cref="App"/>. Everything is lazy so that
    /// nothing here runs unless a Vargostrap feature is actually used.
    /// </summary>
    internal static class VargoApp
    {
        public static readonly LazyJsonManager<VargostrapSettings> Settings = new(nameof(VargostrapSettings));

        private static readonly Lazy<AllowlistService> _allowlist = new(() => new AllowlistService());
        public static AllowlistService Allowlist => _allowlist.Value;
    }
}
