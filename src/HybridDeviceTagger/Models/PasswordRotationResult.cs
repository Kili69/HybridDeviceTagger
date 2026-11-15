/*
 * Author: Andreas Lucas (aka Kili) <andreas.lucas@outlook.com>
 * Copyright 2026 Andreas Lucas
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 *
 * SPDX-License-Identifier: Apache-2.0
 */

namespace HybridDeviceTagger.Models;

/// <summary>Identifies the result of a password rotation check.</summary>
public enum PasswordRotationOutcome
{
    /// <summary>The active password has not reached its configured rotation age.</summary>
    NotDue,
    /// <summary>A new password was staged, applied, validated, and promoted.</summary>
    Rotated,
    /// <summary>The staged password worked and was promoted after the active credential failed.</summary>
    RecoveredFromNext,
    /// <summary>Rotation was due but deferred because no secure LDAP channel was available.</summary>
    DeferredNoSecureConnection,
    /// <summary>Rotation was requested or due but the domain's minimum password age has not elapsed yet.</summary>
    DeferredMinimumPasswordAge
}

/// <summary>Describes the outcome of a password rotation check.</summary>
/// <param name="Outcome">The action performed.</param>
/// <param name="CurrentPasswordAgeDays">The known age of the active password, if available.</param>
/// <param name="EarliestChangeUtc">When Active Directory allows the next change, for a minimum-age deferral.</param>
public sealed record PasswordRotationResult(
    PasswordRotationOutcome Outcome,
    int? CurrentPasswordAgeDays,
    DateTimeOffset? EarliestChangeUtc = null);