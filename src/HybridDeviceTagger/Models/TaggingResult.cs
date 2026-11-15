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

/// <summary>Summarizes one idempotent device-tagging run.</summary>
/// <param name="Discovered">Number of AD computers discovered.</param>
/// <param name="Updated">Number of Entra devices updated.</param>
/// <param name="Unchanged">Number already carrying the configured value.</param>
/// <param name="NotFound">Number without a matching Entra device.</param>
/// <param name="NotHybridJoined">Number whose Entra trust type was not <c>ServerAd</c>.</param>
/// <param name="MembershipsAdded">Number of Entra memberships added.</param>
/// <param name="MembershipsRemoved">Number of owned Entra memberships removed.</param>
/// <param name="RemovedFromScope">Number of managed devices no longer found in any configured OU.</param>
/// <param name="ChangedDeviceNames">Entra display names of the changed devices, for the activity log.</param>
public sealed record TaggingResult(
    int Discovered,
    int Updated,
    int Unchanged,
    int NotFound,
    int NotHybridJoined,
    int MembershipsAdded = 0,
    int MembershipsRemoved = 0,
    int RemovedFromScope = 0,
    IReadOnlyList<string>? ChangedDeviceNames = null);