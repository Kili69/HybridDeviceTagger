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

/// <summary>Represents the Microsoft Entra properties required by the tagging workflow.</summary>
/// <param name="Id">The Entra object identifier.</param>
/// <param name="DeviceId">The device identifier shared with Active Directory.</param>
/// <param name="DisplayName">The optional display name.</param>
/// <param name="TrustType">The optional device trust type.</param>
/// <param name="ExtensionAttributes">The current extension attributes keyed by Graph property name.</param>
public sealed record EntraDevice(
    string Id,
    Guid DeviceId,
    string? DisplayName,
    string? TrustType,
    IReadOnlyDictionary<string, string?> ExtensionAttributes);